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
/// Background worker managing the server-side SOE socket and benchmark session.
/// </summary>
public sealed class ServerWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly CommandLineOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerWorker"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="options">Parsed CLI options.</param>
    public ServerWorker(IServiceProvider services, CommandLineOptions options)
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
                AppCreationCallback = () => _services.GetRequiredService<BenchmarkServerApplication>(),
                StopOnLastSessionTerminated = true
            }
        );

        manager.Bind(new IPEndPoint(_options.Address, _options.Port));
        await manager.RunAsync(ct);
    }
}
