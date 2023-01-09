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
                // TODO: Request a remap here
                TerminateSession(DisconnectReason.UnreachableConnection, false);
                break;
            }
            case SoeOpCode.RemapConnection:
            {
                // TODO: Implement. Not that this can be handled here, anyway
                break;
            }
            default:
                throw new InvalidOperationException($"{nameof(HandleContextlessPacket)} cannot handle {opCode} packets");
        }
    }

    private void HandleSessionRequest(ReadOnlySpan<byte> packetData)
    {
        if (Mode is SessionMode.Client)
        {
            TerminateSession(DisconnectReason.ConnectingToSelf, false);
            return;
        }

        SessionRequest request = SessionRequest.Deserialize(packetData, false);
        _sessionParams.RemoteUdpLength = request.UdpLength;
        SessionId = request.SessionId;

        if (State is not SessionState.Negotiating)
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
            SessionId,
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

        State = SessionState.Running;
    }

    private void HandleSessionResponse(ReadOnlySpan<byte> packetData)
    {
        if (Mode is SessionMode.Server)
        {
            TerminateSession(DisconnectReason.ConnectingToSelf, false);
            return;
        }

        SessionResponse response = SessionResponse.Deserialize(packetData, false);
        _sessionParams.RemoteUdpLength = response.UdpLength;
        _sessionParams.CrcLength = response.CrcLength;
        _sessionParams.CrcSeed = response.CrcSeed;
        _sessionParams.IsCompressionEnabled = response.IsCompressionEnabled;
        SessionId = response.SessionId;

        if (State is not SessionState.Negotiating)
        {
            TerminateSession(DisconnectReason.ConnectError, true);
            return;
        }

        if (response.SoeProtocolVersion is not SoeConstants.SoeProtocolVersion)
        {
            TerminateSession(DisconnectReason.ProtocolMismatch, true);
            return;
        }

        State = SessionState.Running;
    }
}
