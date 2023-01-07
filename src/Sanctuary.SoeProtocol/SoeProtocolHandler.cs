using Sanctuary.SoeProtocol.Abstractions.Services;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using static Sanctuary.SoeProtocol.Util.SoePacketUtils;

namespace Sanctuary.SoeProtocol;

public class SoeProtocolHandler : IDisposable
{
    private readonly SessionMode _mode;
    private readonly SessionParameters _sessionParams;
    private readonly NativeSpanPool _spanPool;
    private readonly INetworkWriter _networkWriter;
    private readonly ConcurrentQueue<NativeSpan> _packetQueue;

    private uint _sessionId;
    private SessionState _state;
    private bool _isDisposed;

    /// <summary>
    /// Gets the reason that the protocol handler was terminated.
    /// </summary>
    public DisconnectReason DisconnectReason { get; private set; }

    public SoeProtocolHandler
    (
        SessionMode mode,
        SessionParameters sessionParameters,
        NativeSpanPool spanPool,
        INetworkWriter networkWriter
    )
    {
        _mode = mode;
        _sessionParams = sessionParameters;
        _spanPool = spanPool;
        _networkWriter = networkWriter;

        _packetQueue = new ConcurrentQueue<NativeSpan>();
        _state = SessionState.Negotiating;
    }

    /// <summary>
    /// Enqueues a packet for processing. The packet may be dropped
    /// if the handler is overloaded.
    /// </summary>
    /// <param name="packetData">The packet data.</param>
    /// <returns><c>True</c> if the packet was successfully enqueued, otherwise <c>false</c>.</returns>
    public bool EnqueuePacket(NativeSpan packetData)
    {
        if (_packetQueue.Count >= _sessionParams.MaxQueuedRawPackets)
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

        while (!ct.IsCancellationRequested && _state is not SessionState.Terminated)
        {
            if (!ProcessOneFromPacketQueue())
                await Task.Delay(10, ct).ConfigureAwait(false);
        }

        if (_state is SessionState.Running)
            TerminateSession(DisconnectReason.Application, true);
    }

    protected void TerminateSession(DisconnectReason reason, bool notifyRemote)
    {
        DisconnectReason = reason;

        if (notifyRemote)
        {
            if (_state is not SessionState.Running)
            {
                throw new InvalidOperationException
                (
                    "Can only notify the remote of a termination while the session is running"
                );
            }

            Disconnect disconnect = new(_sessionId, reason);
            Span<byte> buffer = stackalloc byte[Disconnect.Size];
            disconnect.Serialize(buffer);
            SendSessionPacket(SoeOpCode.Disconnect, buffer);
        }

        _state = SessionState.Terminated;
    }

    private bool ProcessOneFromPacketQueue()
    {
        if (!_packetQueue.TryDequeue(out NativeSpan packet))
            return false;

        if (ValidatePacket(packet.Span, _sessionParams, out SoeOpCode opCode) is not SoePacketValidationResult.Valid)
        {
            TerminateSession(DisconnectReason.CorruptPacket, true);
            return true;
        }

        ReadOnlySpan<byte> packetData = packet.Span[sizeof(SoeOpCode)..];
        bool isSessionless = IsSessionlessPacket(opCode);

        if (isSessionless)
            HandleSessionlessPacket(opCode, packetData);
        else
            HandleSessionPacket(opCode, packetData);

        _spanPool.Return(packet);
        return true;
    }

    private void HandleSessionlessPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (opCode)
        {
            case SoeOpCode.SessionRequest:
            {
                HandleSessionRequest(packetData);
                break;
            }
            case SoeOpCode.SessionResponse:
            {
                HandleSessionResponse(packetData);
                break;
            }
            case SoeOpCode.UnknownSender:
            {
                // TODO: Implement
                break;
            }
            case SoeOpCode.RemapConnection:
            {
                // TODO: Implement
                break;
            }
            default:
                throw new InvalidOperationException($"{nameof(HandleSessionlessPacket)} cannot handle {opCode} packets");
        }
    }

    private void HandleSessionPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        
    }

    private void SendSessionPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        int extraBytes = sizeof(SoeOpCode)
            + (_sessionParams.IsCompressionEnabled ? 1 : 0)
            + _sessionParams.CrcLength;

        if (packetData.Length + extraBytes > _sessionParams.RemoteUdpLength)
            throw new InvalidOperationException("Cannot send a packet larger than the remote UDP length");

        NativeSpan sendBuffer = _spanPool.Rent();
        BinaryWriter writer = new(sendBuffer.Span);

        writer.WriteUInt16BE((ushort)opCode);
        writer.WriteBool(false); // Compression is not implemented at the moment
        writer.WriteBytes(packetData);
        AppendCrc(ref writer, _sessionParams.CrcSeed, _sessionParams.CrcLength);

        _networkWriter.Send(sendBuffer.Span);
        _spanPool.Return(sendBuffer);
    }

    private void HandleSessionRequest(ReadOnlySpan<byte> packetData)
    {
        if (_mode is SessionMode.Client)
        {
            TerminateSession(DisconnectReason.ConnectingToSelf, false);
            return;
        }

        SessionRequest request = SessionRequest.Deserialize(packetData);
        _sessionParams.RemoteUdpLength = request.UdpLength;
        _sessionId = request.SessionId;

        if (_state is not SessionState.Negotiating)
        {
            TerminateSession(DisconnectReason.ConnectError, true);
            return;
        }

        bool protocolsMatch = request.SoeProtocolVersion == SoeConstants.SoeProtocolVersion
            && request.ApplicationProtocol == _sessionParams.ApplicationProtocol;
        if (!protocolsMatch)
        {
            TerminateSession(DisconnectReason.ProtocolMismatch, true);
            return;
        }

        _sessionParams.CrcLength = SoeConstants.CrcLength;
        _sessionParams.CrcSeed = (uint)Random.Shared.NextInt64();

        SessionResponse response = new
        (
            _sessionId,
            _sessionParams.CrcSeed,
            _sessionParams.CrcLength,
            _sessionParams.IsCompressionEnabled,
            0,
            _sessionParams.UdpLength,
            SoeConstants.SoeProtocolVersion
        );

        Span<byte> buffer = stackalloc byte[SessionResponse.Size];
        response.Serialize(buffer);
        _networkWriter.Send(buffer);

        _state = SessionState.Running;
    }

    private void HandleSessionResponse(ReadOnlySpan<byte> packetData)
    {
        if (_mode is SessionMode.Server)
        {
            TerminateSession(DisconnectReason.ConnectingToSelf, false);
            return;
        }

        SessionResponse response = SessionResponse.Deserialize(packetData);
        _sessionParams.RemoteUdpLength = response.UdpLength;
        _sessionParams.CrcLength = response.CrcLength;
        _sessionParams.CrcSeed = response.CrcSeed;
        _sessionParams.IsCompressionEnabled = response.IsCompressionEnabled;
        _sessionId = response.SessionId;

        if (_state is not SessionState.Negotiating)
        {
            TerminateSession(DisconnectReason.ConnectError, true);
            return;
        }

        if (response.SoeProtocolVersion is not SoeConstants.SoeProtocolVersion)
        {
            TerminateSession(DisconnectReason.ProtocolMismatch, true);
            return;
        }

        _state = SessionState.Running;
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
            while (_packetQueue.TryDequeue(out NativeSpan packet))
                _spanPool.Return(packet);
        }

        _isDisposed = true;
    }
}
