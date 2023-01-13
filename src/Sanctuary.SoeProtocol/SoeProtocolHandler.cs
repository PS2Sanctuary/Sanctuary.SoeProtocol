using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Abstractions.Services;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using static Sanctuary.SoeProtocol.Util.SoePacketUtils;

namespace Sanctuary.SoeProtocol;

public partial class SoeProtocolHandler : ISessionHandler, IDisposable
{
    private readonly NativeSpanPool _spanPool;
    private readonly INetworkWriter _networkWriter;
    private readonly IApplicationProtocolHandler _application;
    private readonly ConcurrentQueue<NativeSpan> _packetQueue;
    private readonly ReliableDataInputChannel _dataInputChannel;

    private bool _isDisposed;

    /// <summary>
    /// Gets the session parameters in use by the session.
    /// </summary>
    internal SessionParameters SessionParams { get; }

    /// <inheritdoc />
    public SessionMode Mode { get; }

    /// <inheritdoc />
    public SessionState State { get; private set; }

    /// <inheritdoc />
    public uint SessionId { get; private set; }

    /// <inheritdoc />
    public DisconnectReason TerminationReason { get; private set; }

    public SoeProtocolHandler
    (
        SessionMode mode,
        SessionParameters sessionParameters,
        NativeSpanPool spanPool,
        INetworkWriter networkWriter,
        IApplicationProtocolHandler application,
        Rc4KeyState cipherState
    )
    {
        Mode = mode;
        SessionParams = sessionParameters;
        _spanPool = spanPool;
        _networkWriter = networkWriter;
        _application = application;

        _packetQueue = new ConcurrentQueue<NativeSpan>();
        _contextualSendBuffer = GC.AllocateArray<byte>((int)sessionParameters.UdpLength, true);
        _dataInputChannel = new ReliableDataInputChannel(this, _spanPool, cipherState, _application.HandleAppData);

        State = SessionState.Negotiating;
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
    /// Asynchronously runs the protocol handler. This method will not return
    /// until either it is cancelled, or the remote party terminates the session.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    public async Task RunAsync(CancellationToken ct)
    {
        await Task.Yield();

        while (!ct.IsCancellationRequested && State is not SessionState.Terminated)
        {
            SendHeartbeatIfRequired();

            if (!ProcessOneFromPacketQueue())
                await Task.Delay(10, ct).ConfigureAwait(false);
        }

        if (State is SessionState.Running)
            TerminateSession(DisconnectReason.Application, true);
    }

    protected void TerminateSession(DisconnectReason reason, bool notifyRemote)
    {
        _application.OnSessionClosed(reason);
        TerminationReason = reason;

        if (notifyRemote)
        {
            if (State is not SessionState.Running)
            {
                throw new InvalidOperationException
                (
                    "Can only notify the remote of a termination while the session is running"
                );
            }

            Disconnect disconnect = new(SessionId, reason);
            Span<byte> buffer = stackalloc byte[Disconnect.Size];
            disconnect.Serialize(buffer);
            SendContextualPacket(SoeOpCode.Disconnect, buffer);
        }

        State = SessionState.Terminated;
    }

    private bool ProcessOneFromPacketQueue()
    {
        if (!_packetQueue.TryDequeue(out NativeSpan? packet))
            return false;

        if (ValidatePacket(packet.FullSpan, SessionParams, out SoeOpCode opCode) is not SoePacketValidationResult.Valid)
        {
            TerminateSession(DisconnectReason.CorruptPacket, true);
            return true;
        }

        Span<byte> packetData = packet.FullSpan[sizeof(SoeOpCode)..];
        bool isSessionless = IsContextlessPacket(opCode);

        if (isSessionless)
            HandleContextlessPacket(opCode, packetData);
        else
            HandleContextualPacket(opCode, packetData);

        _spanPool.Return(packet);
        return true;
    }

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

            while (_packetQueue.TryDequeue(out NativeSpan? packet))
                _spanPool.Return(packet);
        }

        _isDisposed = true;
    }
}
