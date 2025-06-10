using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;

namespace SimpleServer;

public class LoginApplication : IApplicationProtocolHandler
{
    private readonly ILogger<LoginApplication> _logger;

    private ISessionHandler? _sessionHandler;

    public ApplicationParameters SessionParams { get; }

    public LoginApplication(ILogger<LoginApplication> logger)
    {
        _logger = logger;

        Span<byte> keyBytes = Convert.FromBase64String("MY_KEY");
        SessionParams = new ApplicationParameters(new Rc4KeyState(keyBytes))
        {
            IsEncryptionEnabled = true
        };
    }

    public void Initialise(ISessionHandler sessionHandler)
        => _sessionHandler = sessionHandler;

    public void OnSessionOpened()
        => _logger.LogDebug("Login session opened - ID {SessionId}", _sessionHandler?.SessionId);

    public void HandleAppData(ReadOnlySpan<byte> data)
    {
        byte loginOpCode = data[0];
        _logger.LogInformation("Received application packet with OP code {OpCode}", loginOpCode);
        //TerminateLoginSession();
    }

    public void OnSessionClosed(DisconnectReason disconnectReason)
    {
        _logger.LogDebug
        (
            "Login session closed with reason {DisconnectRsn} - ID {SessionId}",
            disconnectReason,
            _sessionHandler?.SessionId
        );
    }

    // N.B. login sessions don't terminate at the app level, only at the SOE level
    private void TerminateLoginSession()
        => _sessionHandler?.TerminateSession();
}
