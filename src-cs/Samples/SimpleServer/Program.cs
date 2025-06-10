using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sanctuary.SoeProtocol.Abstractions;

namespace SimpleServer;

public class Program
{
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddTransient<IApplicationProtocolHandler, LoginApplication>();
        builder.Services.AddHostedService<SoeSessionWorker>();

        IHost host = builder.Build();
        host.Run();
    }
}
