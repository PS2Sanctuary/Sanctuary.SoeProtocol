using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleServer;

public class SoeSessionWorker : BackgroundService
{
    private readonly ILogger<SoeSessionWorker> _logger;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;

    public SoeSessionWorker
    (
        ILogger<SoeSessionWorker> logger,
        IServiceProvider services,
        IConfiguration configuration
    )
    {
        _logger = logger;
        _services = services;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string? portConfig = _configuration["Port"];
        if (string.IsNullOrEmpty(portConfig) || !int.TryParse(portConfig, out int port))
            throw new InvalidOperationException("Port config value not set");

        string? appProtocol = _configuration["ApplicationProtocol"];
        if (string.IsNullOrEmpty(appProtocol))
            throw new InvalidOperationException("Application protocol config value not set");

        _logger.LogInformation("Starting server on port {Result}", port);

        using SoeSocketHandler socketHandler = new
        (
            _services.GetRequiredService<ILogger<SoeSocketHandler>>(),
            new SocketHandlerParams
            {
                DefaultSessionParams = new SessionParameters
                {
                    ApplicationProtocol = appProtocol,
                    IsCompressionEnabled = true
                },
                AppCreationCallback = () => _services.GetRequiredService<IApplicationProtocolHandler>()
            }
        );
        socketHandler.Bind(new IPEndPoint(IPAddress.Loopback, port));

        await socketHandler.RunAsync(ct);
    }
}
