using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;

namespace Sanctuary.SoeProtocol;

public partial class SoeProtocolHandler
{
    private void HandleContextlessPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
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
                throw new InvalidOperationException($"{nameof(HandleContextlessPacket)} cannot handle {opCode} packets");
        }
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
}
