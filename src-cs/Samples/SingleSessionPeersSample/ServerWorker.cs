using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol;
using Sanctuary.SoeProtocol.Objects;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SingleSessionPeersSample;

/// <summary>
/// A background worker for the server session manager.
/// </summary>
public class ServerWorker : BackgroundService
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerWorker"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    public ServerWorker(IServiceProvider services)
    {
        _services = services;
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
                    ApplicationProtocol = "Ping_1"
                },
                AppCreationCallback = () => _services.GetRequiredService<PingApplication>(),
                StopOnLastSessionTerminated = true
            }
        );
        manager.Bind(new IPEndPoint(IPAddress.Loopback, Program.Port));

        await manager.RunAsync(ct);
    }
}
