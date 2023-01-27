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
                throw new InvalidOperationException("Remap requests must be handled by the connection manager");
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
        SessionParams.RemoteUdpLength = request.UdpLength;
        SessionId = request.SessionId;

        if (State is not SessionState.Negotiating)
        {
            TerminateSession(DisconnectReason.ConnectError, true);
            return;
        }

        bool protocolsMatch = request.SoeProtocolVersion == SoeConstants.SoeProtocolVersion
            && request.ApplicationProtocol == SessionParams.ApplicationProtocol;
        if (!protocolsMatch)
        {
            TerminateSession(DisconnectReason.ProtocolMismatch, true);
            return;
        }

        SessionParams.CrcLength = SoeConstants.CrcLength;
        SessionParams.CrcSeed = (uint)Random.Shared.NextInt64();

        SessionResponse response = new
        (
            SessionId,
            SessionParams.CrcSeed,
            SessionParams.CrcLength,
            SessionParams.IsCompressionEnabled,
            0,
            SessionParams.UdpLength,
            SoeConstants.SoeProtocolVersion
        );

        Span<byte> buffer = stackalloc byte[SessionResponse.Size];
        response.Serialize(buffer);
        _networkWriter.Send(buffer);

        State = SessionState.Running;
        _openSessionOnNextClientPacket = true;
    }

    private void HandleSessionResponse(ReadOnlySpan<byte> packetData)
    {
        if (Mode is SessionMode.Server)
        {
            TerminateSession(DisconnectReason.ConnectingToSelf, false);
            return;
        }

        SessionResponse response = SessionResponse.Deserialize(packetData, false);
        SessionParams.RemoteUdpLength = response.UdpLength;
        SessionParams.CrcLength = response.CrcLength;
        SessionParams.CrcSeed = response.CrcSeed;
        SessionParams.IsCompressionEnabled = response.IsCompressionEnabled;
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
        _application.OnSessionOpened();
    }
}
