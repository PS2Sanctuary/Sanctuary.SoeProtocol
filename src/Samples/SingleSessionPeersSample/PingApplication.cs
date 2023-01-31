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

    /// <summary>
    /// Initializes a new instance of the <see cref="PingApplication"/> class.
    /// </summary>
    public PingApplication(ILogger<PingApplication> logger)
    {
        _logger = logger;
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

        if (message is not "Ping!")
            return;

        _sessionHandler!.EnqueueData("Pong!"u8);

        Task.Run(() =>
        {
            Task.Delay(1000).Wait();
            _sessionHandler!.TerminateSession();
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
