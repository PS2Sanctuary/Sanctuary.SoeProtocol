using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;

namespace Sanctuary.SoeProtocol;

public partial class SoeProtocolHandler
{
    /// <summary>
    /// Sends a session request to the remote. The underlying network writer must be connected,
    /// and the handler must be in client mode, and ready for negotiation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the handler is not ready, or the <see cref="SessionParameters.ApplicationProtocol"/> is too long.
    /// </exception>
    public void SendSessionRequest()
    {
        if (State is not SessionState.Negotiating)
            throw new InvalidOperationException("Can only send a session request while in the Negotiating state");

        if (Mode is not SessionMode.Client)
            throw new InvalidOperationException("Can only send a session request while in the Client mode");

        uint id = (uint)Random.Shared.NextInt64();
        SessionRequest request = new
        (
            SoeConstants.SoeProtocolVersion,
            id,
            SessionParams.UdpLength,
            SessionParams.ApplicationProtocol
        );

        int packetSize = request.GetSize();
        // Unfortunately we can only guarantee our UDP length here, and not the remote's
        if (packetSize > SessionParams.UdpLength)
            throw new InvalidOperationException("The ApplicationProtocol string is too long");

        byte[] buffer = new byte[packetSize];
        request.Serialize(buffer);
        _networkWriter.Send(buffer);
    }

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
            {
                throw new InvalidOperationException($"{nameof(HandleContextlessPacket)} cannot handle {opCode} packets");
            }
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
        _dataOutputChannel.SetMaxDataLength(CalculateMaxDataLength());

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
        _dataOutputChannel.SetMaxDataLength(CalculateMaxDataLength());

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
