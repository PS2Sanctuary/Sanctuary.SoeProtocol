using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;

namespace ThroughputBenchmark;

/// <summary>
/// Implements high-throughput zero-copy packet reflection on the benchmark client.
/// </summary>
public sealed class BenchmarkClientApplication : IApplicationProtocolHandler
{
    private readonly ILogger<BenchmarkClientApplication> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private ISessionHandler? _sessionHandler;

    /// <inheritdoc />
    public ApplicationParameters SessionParams { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BenchmarkClientApplication"/> class.
    /// </summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="lifetime">The host application lifetime.</param>
    public BenchmarkClientApplication
    (
        ILogger<BenchmarkClientApplication> logger,
        IHostApplicationLifetime lifetime
    )
    {
        _logger = logger;
        _lifetime = lifetime;
        SessionParams = new ApplicationParameters(null);
    }

    /// <inheritdoc />
    public void Initialise(ISessionHandler sessionHandler)
    {
        _sessionHandler = sessionHandler;
    }

    /// <inheritdoc />
    public void OnSessionOpened()
    {
        _logger.LogInformation("Client session opened. Sending initial handshake and ready to echo.");

        // Send an initial handshake packet to prompt the server to open its session immediately
        byte[] handshake = new byte[BenchmarkPacket.HeaderSize];
        BenchmarkPacket packet = new(BenchmarkMessageType.None, 0, 0, 0);
        packet.WriteTo(handshake);
        _sessionHandler?.EnqueueData(handshake);
    }

    /// <inheritdoc />
    public void HandleAppData(ReadOnlySpan<byte> data)
    {
        if (BenchmarkPacket.TryRead(data, out BenchmarkPacket packet) && packet.Type == BenchmarkMessageType.None)
            return;

        _sessionHandler?.EnqueueData(data);
    }

    /// <inheritdoc />
    public void OnSessionClosed(DisconnectReason disconnectReason)
    {
        _logger.LogInformation("Client session closed with reason: {Reason}. Shutting down.", disconnectReason);
        _lifetime.StopApplication();
    }
}
