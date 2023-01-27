using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace SingleSessionPeersSample;

/// <summary>
/// The entry class of the program.
/// </summary>
public static class Program
{
    /// <summary>
    /// The entry point of the program.
    /// </summary>
    /// <param name="args">The launch arguments.</param>
    public static async Task Main(string[] args)
    {
        IHost host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddTransient<PingApplication>();
                services.AddHostedService<ClientWorker>();
                services.AddHostedService<ServerWorker>();
            })
            .Build();

        await host.RunAsync();
    }
}
