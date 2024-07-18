using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;
using System.Net;
using ZLogger;

namespace Sanctuary.SoeProtocol.Managers;

public class SoeConnection
{
    private readonly ILogger<SoeConnection> _logger;
    private readonly SessionManager _manager;
    private readonly SessionParameters _sessionParams;

    public SocketAddress RemoteEndPoint { get; }
    public uint SessionId { get; private set; }
    public DisconnectReason TerminationReason { get; private set; }
    public bool TerminatedByRemote { get; private set; }

    public SoeConnection
    (
        ILogger<SoeConnection> logger,
        SessionManager manager,
        SocketAddress remote
    )
    {
        _logger = logger;
        _manager = manager;
        _sessionParams = manager.SessionParams;
        RemoteEndPoint = remote;
    }

    public void HandleSoePacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        switch (opCode)
        {
            case SoeOpCode.SessionRequest:
                HandleSessionRequest(packetData);
                break;
        }
    }

    /// <summary>
    /// Terminates the session. This may be called whenever the session needs to close,
    /// e.g. when the other party has disconnected, or an internal error has occurred.
    /// </summary>
    /// <param name="reason">The termination reason.</param>
    /// <param name="notifyRemote">Whether to notify the remote party.</param>
    /// <param name="terminatedByRemote">Indicates whether this termination has come from the remote party.</param>
    public void TerminateSession(DisconnectReason reason, bool notifyRemote, bool terminatedByRemote = false)
    {
        try
        {
            TerminationReason = reason;

            if (notifyRemote)
            {
                Disconnect disconnect = new(SessionId, reason);
                Span<byte> buffer = stackalloc byte[Disconnect.Size];
                disconnect.Serialize(buffer);
                SendContextualPacket(SoeOpCode.Disconnect, buffer);
            }
        }
        finally
        {
            TerminatedByRemote = terminatedByRemote;
            //_application.OnSessionClosed(reason);
        }
    }

    /// <summary>
    /// Sends a contextual packet.
    /// </summary>
    /// <param name="opCode">The OP code of the packet to send.</param>
    /// <param name="packetData">The packet data, not including the OP code.</param>
    internal void SendContextualPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        // int extraBytes = sizeof(SoeOpCode)
        //     + (_sessionParams.IsCompressionEnabled ? 1 : 0)
        //     + _sessionParams.CrcLength;
        //
        // if (packetData.Length + extraBytes > _sessionParams.RemoteUdpLength)
        //     throw new InvalidOperationException("Cannot send a packet larger than the remote UDP length");
        //
        // BinaryWriter writer = new(_contextualSendBuffer);
        //
        // writer.WriteUInt16BE((ushort)opCode);
        // if (_sessionParams.IsCompressionEnabled)
        //     writer.WriteBool(false); // Compression is not implemented at the moment
        // writer.WriteBytes(packetData);
        // SoePacketUtils.AppendCrc(ref writer, _sessionParams.CrcSeed, _sessionParams.CrcLength);
        //
        // _networkWriter.Send(writer.Consumed);
    }

    private void HandleSessionRequest(ReadOnlySpan<byte> packetData)
    {
        SessionRequest request = SessionRequest.Deserialize(packetData, false);
        DisconnectReason reason = DisconnectReason.None;

        if (packetData.Length < SessionRequest.MinSize)
        {
            _logger.ZLogWarning
            (
                $"Received SessionRequest from {RemoteEndPoint} that was too short: {packetData.Length} bytes"
            );
            reason = DisconnectReason.CorruptPacket;
        }

        if (request.SoeProtocolVersion != SoeConstants.SoeProtocolVersion)
        {
            _logger.ZLogWarning
            (
                $"Received SessionRequest from {RemoteEndPoint} with invalid SOE protocol version: " +
                $"{request.SoeProtocolVersion}"
            );
            reason = DisconnectReason.ProtocolMismatch;
        }

        if (request.ApplicationProtocol != _sessionParams.ApplicationProtocol)
        {
            _logger.ZLogWarning
            (
                $"Received SessionRequest from {RemoteEndPoint} with invalid application protocol: " +
                $"{request.ApplicationProtocol}"
            );
            reason = DisconnectReason.ProtocolMismatch;
        }

        if (reason is not DisconnectReason.None)
        {
            TerminateSession(reason, true);
            return;
        }

        // TODO: Send a response
    }
}
