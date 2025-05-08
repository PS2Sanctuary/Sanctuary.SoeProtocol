using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;
using System.Diagnostics;
using System.Text;

namespace SingleSessionPeersSample;

/// <summary>
/// Represents a protocol handler for the Ping application.
/// </summary>
public class PingApplication : IApplicationProtocolHandler
{
    private readonly ILogger<PingApplication> _logger;

    private ISessionHandler? _sessionHandler;
    private long _sessionStart;
    private long _receiveCount;

    /// <inheritdoc />
    public ApplicationParameters SessionParams { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PingApplication"/> class.
    /// </summary>
    public PingApplication(ILogger<PingApplication> logger)
    {
        _logger = logger;

        SessionParams = new ApplicationParameters(null);
    }

    /// <inheritdoc />
    public void Initialise(ISessionHandler sessionHandler)
        => _sessionHandler = sessionHandler;

    /// <inheritdoc />
    public void OnSessionOpened()
    {
        _sessionStart = Stopwatch.GetTimestamp();
        _logger.LogInformation("{Mode} Session opened", GetModePrefix());

        if (_sessionHandler!.Mode is SessionMode.Server)
            _sessionHandler.EnqueueData("Ping!"u8);
    }

    /// <inheritdoc />
    public void HandleAppData(ReadOnlySpan<byte> data)
    {
        _receiveCount++;
        string message = Encoding.UTF8.GetString(data);
        _logger.LogInformation("{Mode} Received {Message}", GetModePrefix(), message);

        _sessionHandler!.EnqueueData
        (
            message is "Ping!" ? "Pong!"u8 : "Ping!"u8
        );

        if (_sessionHandler!.Mode is SessionMode.Client && Stopwatch.GetElapsedTime(_sessionStart) >= TimeSpan.FromSeconds(10))
            _sessionHandler!.TerminateSession();
    }

    /// <inheritdoc />
    public void OnSessionClosed(DisconnectReason disconnectReason)
        => _logger.LogInformation
        (
            "{Mode} Session closed. Throughput: {Throughput}/s",
            GetModePrefix(),
            _receiveCount / Stopwatch.GetElapsedTime(_sessionStart).Seconds
        );

    private string GetModePrefix()
        => _sessionHandler is not null
            ? $"<{_sessionHandler.Mode.ToString()}>"
            : "<Unknown>";
}
