using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Abstractions.Services;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading;
using static Sanctuary.SoeProtocol.Util.SoePacketUtils;

namespace Sanctuary.SoeProtocol;

/// <summary>
/// Represents a generic handler for the SOE protocol.
/// </summary>
public partial class SoeProtocolHandler : ISessionHandler, ISoeConnection, IDisposable
{
    private readonly NativeSpanPool _spanPool;
    private readonly INetworkWriter _networkWriter;
    private readonly IApplicationProtocolHandler _application;
    private readonly ConcurrentQueue<NativeSpan> _packetQueue;
    private readonly ReliableDataInputChannel _dataInputChannel;
    private readonly ReliableDataOutputChannel _dataOutputChannel;

    private bool _isDisposed;
    private bool _openSessionOnNextClientPacket;
    private long _lastReceivedPacketTick;

    /// <summary>
    /// Gets the address of the remote that this session is connected to.
    /// </summary>
    public SocketAddress Remote { get; }

    /// <summary>
    /// Gets the session parameters in use by the session.
    /// </summary>
    public SessionParameters SessionParams { get; }

    /// <summary>
    /// Gets the application parameters in use by the session.
    /// </summary>
    public ApplicationParameters ApplicationParams { get; }

    /// <inheritdoc />
    public SessionMode Mode { get; }

    /// <inheritdoc />
    public SessionState State { get; private set; }

    /// <inheritdoc cref="ISoeConnection.SessionId" />
    public uint SessionId { get; private set; }

    /// <inheritdoc />
    public DisconnectReason TerminationReason { get; private set; }

    /// <inheritdoc />
    public bool TerminatedByRemote { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SoeProtocolHandler"/> class.
    /// </summary>
    /// <param name="remote">The address of the remote that this session is connected to.</param>
    /// <param name="mode">The mode that the handler should operate in.</param>
    /// <param name="sessionParameters">
    /// The session parameters to conform to. This object will be disposed with the handler.
    /// </param>
    /// <param name="spanPool">The native span pool to use.</param>
    /// <param name="networkWriter">The network writer to send packets on.</param>
    /// <param name="application">The proxied application.</param>
    public SoeProtocolHandler
    (
        SocketAddress remote,
        SessionMode mode,
        SessionParameters sessionParameters,
        NativeSpanPool spanPool,
        INetworkWriter networkWriter,
        IApplicationProtocolHandler application
    )
    {
        Remote = remote;
        Mode = mode;
        SessionParams = sessionParameters;
        ApplicationParams = application.SessionParams;
        _spanPool = spanPool;
        _networkWriter = networkWriter;
        _application = application;

        _packetQueue = new ConcurrentQueue<NativeSpan>();
        _contextualSendBuffer = GC.AllocateArray<byte>((int)sessionParameters.UdpLength, true);
        _dataInputChannel = new ReliableDataInputChannel
        (
            this,
            SessionParams,
            ApplicationParams,
            _spanPool,
            _application.HandleAppData
        );
        _dataOutputChannel = new ReliableDataOutputChannel(this, _spanPool, CalculateMaxDataLength());

        State = SessionState.Negotiating;
        _application.Initialise(this);
    }

    /// <summary>
    /// Enqueues a packet for processing. The packet may be dropped
    /// if the handler is overloaded.
    /// </summary>
    /// <param name="packetData">The packet data.</param>
    /// <returns><c>True</c> if the packet was successfully enqueued, otherwise <c>false</c>.</returns>
    public bool EnqueuePacket(NativeSpan packetData)
    {
        if (_packetQueue.Count >= SessionParams.MaxQueuedRawPackets)
            return false;

        _packetQueue.Enqueue(packetData);
        return true;
    }

    /// <summary>
    /// Initializes the handler. This method should be called before
    /// <see cref="RunTick"/>.
    /// </summary>
    public void Initialize()
        => _lastReceivedPacketTick = Stopwatch.GetTimestamp();

    /// <summary>
    /// Runs a single iteration of the handler's logic.
    /// </summary>
    /// <param name="needsMoreTime">
    /// A value indicating whether <see cref="RunTick"/> should be called again as soon as possible.
    /// </param>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    /// <returns>
    /// <c>True</c> if the iteration ran successfully, otherwise <c>false</c>. In this case,
    /// the handler should be considered terminated.
    /// </returns>
    public bool RunTick(out bool needsMoreTime, CancellationToken ct)
    {
        needsMoreTime = false;

        if (State is SessionState.Terminated)
            return false;

        needsMoreTime = ProcessOneFromPacketQueue();
        SendHeartbeatIfRequired();

        if (Stopwatch.GetElapsedTime(_lastReceivedPacketTick) > SessionParams.InactivityTimeout)
        {
            TerminateSession(DisconnectReason.Timeout, false);
            return false;
        }

        _dataInputChannel.RunTick();
        _dataOutputChannel.RunTick(ct);

        return true;
    }

    /// <inheritdoc />
    public bool EnqueueData(ReadOnlySpan<byte> data)
    {
        if (State is not SessionState.Running)
            return false;

        _dataOutputChannel.EnqueueData(data);
        return true;
    }

    /// <inheritdoc />
    public void TerminateSession()
        => TerminateSession(DisconnectReason.Application, true);

    /// <summary>
    /// Terminates the session. This may be called whenever the session needs to close,
    /// e.g. when the other party has disconnected, or an internal error has occurred.
    /// </summary>
    /// <param name="reason">The termination reason.</param>
    /// <param name="notifyRemote">Whether to notify the remote party.</param>
    /// <param name="terminatedByRemote">Indicates whether this termination has come from the remote party.</param>
    public void TerminateSession(DisconnectReason reason, bool notifyRemote, bool terminatedByRemote = false)
    {
        if (State is SessionState.Terminated)
            return;

        try
        {
            // Naive flush of the output channel
            _dataOutputChannel.RunTick(CancellationToken.None);
            TerminationReason = reason;

            if (notifyRemote && State is SessionState.Running)
            {
                Disconnect disconnect = new(SessionId, reason);
                Span<byte> buffer = stackalloc byte[Disconnect.Size];
                disconnect.Serialize(buffer);
                SendContextualPacket(SoeOpCode.Disconnect, buffer);
            }
        }
        finally
        {
            State = SessionState.Terminated;
            TerminatedByRemote = terminatedByRemote;
            _application.OnSessionClosed(reason);
        }
    }

    private bool ProcessOneFromPacketQueue()
    {
        if (!_packetQueue.TryDequeue(out NativeSpan? packet))
            return false;

        ProcessPacketCore(packet.UsedSpan, true);
        _spanPool.Return(packet);
        return true;
    }

    private void ProcessPacketCore(Span<byte> packetData, bool validate, SoeOpCode opCode = SoeOpCode.Invalid)
    {
        if (validate)
        {
            SoePacketValidationResult validationResult = ValidatePacket(packetData, SessionParams, out opCode);
            if (validationResult is not SoePacketValidationResult.Valid)
            {
                TerminateSession(DisconnectReason.CorruptPacket, true);
                return;
            }
        }

        if (_openSessionOnNextClientPacket)
        {
            _application.OnSessionOpened();
            _openSessionOnNextClientPacket = false;
        }

        // We set this after packet validation as a primitive method of stopping the connection
        // if all we've received is multiple corrupt packets in a row
        _lastReceivedPacketTick = Stopwatch.GetTimestamp();
        packetData = packetData[sizeof(SoeOpCode)..];
        bool isSessionless = IsContextlessPacket(opCode);

        if (isSessionless)
            HandleContextlessPacket(opCode, packetData);
        else
            HandleContextualPacket(opCode, packetData[..^SessionParams.CrcLength]);
    }

    private int CalculateMaxDataLength()
        => (int)SessionParams.UdpLength - sizeof(SoeOpCode)
            - (SessionParams.IsCompressionEnabled ? 1 : 0)
            - SessionParams.CrcLength;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of managed and unmanaged resources.
    /// </summary>
    /// <param name="disposeManaged">Whether to dispose of managed resources.</param>
    protected virtual void Dispose(bool disposeManaged)
    {
        if (_isDisposed)
            return;

        if (disposeManaged)
        {
            _dataInputChannel.Dispose();
            _dataOutputChannel.Dispose();

            while (_packetQueue.TryDequeue(out NativeSpan? packet))
                _spanPool.Return(packet);
        }

        _isDisposed = true;
    }
}
