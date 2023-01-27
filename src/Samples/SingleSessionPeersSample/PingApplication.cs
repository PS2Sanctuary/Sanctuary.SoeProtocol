using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;
using System.Text;
using System.Threading.Tasks;

namespace SingleSessionPeersSample;

/// <summary>
/// Represents a protocol handler for the Ping application.
/// </summary>
public class PingApplication : IApplicationProtocolHandler
{
    private readonly ILogger<PingApplication> _logger;

    private ISessionHandler? _sessionHandler;
    private int pingCount;

    /// <inheritdoc />
    public SessionParameters SessionParams { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PingApplication"/> class.
    /// </summary>
    public PingApplication(ILogger<PingApplication> logger)
    {
        _logger = logger;

        SessionParams = new SessionParameters
        {
            ApplicationProtocol = "Ping_1",
            EncryptionKeyState = new Rc4KeyState(new byte[1])
        };
    }

    /// <inheritdoc />
    public void Initialise(ISessionHandler sessionHandler)
        => _sessionHandler = sessionHandler;

    /// <inheritdoc />
    public void OnSessionOpened()
    {
        _logger.LogInformation("{Mode} Session opened", GetModePrefix());

        if (_sessionHandler!.Mode is SessionMode.Client)
            _sessionHandler.EnqueueData("Ping!"u8);
    }

    /// <inheritdoc />
    public void HandleAppData(ReadOnlySpan<byte> data)
    {
        string message = Encoding.UTF8.GetString(data);
        _logger.LogInformation("{Mode} Received {Message}", GetModePrefix(), message);

        if (pingCount++ is 10)
        {
            _sessionHandler!.TerminateSession();
            return;
        }

        Task.Run(() =>
        {
            Task.Delay(1000).Wait();
            _sessionHandler!.EnqueueData
            (
                message is "Ping!" ? "Pong!"u8 : "Ping!"u8
            );
        });
    }

    /// <inheritdoc />
    public void OnSessionClosed(DisconnectReason disconnectReason)
        => _logger.LogInformation("{Mode} Session closed", GetModePrefix());

    private string GetModePrefix()
        => _sessionHandler is not null
            ? $"<{_sessionHandler.Mode.ToString()}>"
            : "<Unknown>";
}
