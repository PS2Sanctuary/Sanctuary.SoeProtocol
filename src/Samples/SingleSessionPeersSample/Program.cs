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
    /// Gets or sets the port to run on.
    /// </summary>
    public static int Port { get; set; } = 12345;

    /// <summary>
    /// The entry point of the program.
    /// </summary>
    /// <param name="args">The launch arguments.</param>
    public static async Task Main(string[] args)
    {
        if (args.Length > 0 && int.TryParse(args[0], out int port))
            Port = port;

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
