using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol;
using Sanctuary.SoeProtocol.Objects;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ThroughputBenchmark;

/// <summary>
/// Background worker managing the client-side SOE socket and echo reflection session.
/// </summary>
public sealed class ClientWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly CommandLineOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientWorker"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="options">Parsed CLI options.</param>
    public ClientWorker(IServiceProvider services, CommandLineOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        SoeSocketHandler manager = new
        (
            _services.GetRequiredService<ILogger<SoeSocketHandler>>(),
            new SocketHandlerParams
            {
                DefaultSessionParams = new SessionParameters
                {
                    ApplicationProtocol = "Benchmark_1",
                    MaxQueuedOutgoingReliableDataPackets = 512,
                    MaxQueuedIncomingReliableDataPackets = 512,
                    MaxQueuedRawPackets = 1024,
                    DataAckWindow = 64,
                    MaximumAcknowledgeDelay = TimeSpan.FromMilliseconds(1)
                },
                AppCreationCallback = () => _services.GetRequiredService<BenchmarkClientApplication>(),
                StopOnLastSessionTerminated = true
            }
        );

        // Allow server a brief moment to bind
        await Task.Delay(200, ct);
        IPAddress bindAddress = _options.Address.Equals(IPAddress.Loopback) ? IPAddress.Loopback : IPAddress.Any;
        manager.Bind(new IPEndPoint(bindAddress, 0));
        manager.Connect(new IPEndPoint(_options.Address, _options.Port));

        await manager.RunAsync(ct);
    }
}
