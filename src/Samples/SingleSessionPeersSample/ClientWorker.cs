using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Managers;
using Sanctuary.SoeProtocol.Objects;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SingleSessionPeersSample;

/// <summary>
/// A background worker for the client session manager.
/// </summary>
public class ClientWorker : BackgroundService
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientWorker"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    public ClientWorker(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        SingleSessionManager manager = new
        (
            _services.GetRequiredService<ILogger<SingleSessionManager>>(),
            new SessionParameters
            {
                ApplicationProtocol = "Ping_1"
            },
            _services.GetRequiredService<PingApplication>(),
            new IPEndPoint(IPAddress.Loopback, Program.Port),
            SessionMode.Client
        );

        // Give the server manager some time to spool up
        await Task.Delay(500, ct);
        await manager.RunAsync(ct);
    }
}
