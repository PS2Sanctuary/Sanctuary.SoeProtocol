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
        SingleSessionManager manager = new
        (
            _services.GetRequiredService<ILogger<SingleSessionManager>>(),
            new SessionParameters
            {
                ApplicationProtocol = "Ping_1",
                EncryptionKeyState = new Rc4KeyState(new byte[1])
            },
            _services.GetRequiredService<PingApplication>(),
            new IPEndPoint(IPAddress.Loopback, 12345),
            SessionMode.Server
        );

        await manager.RunAsync(ct);
    }
}
