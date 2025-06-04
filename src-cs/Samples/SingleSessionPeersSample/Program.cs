using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    public static void Main(string[] args)
    {
        if (args.Length > 0 && int.TryParse(args[0], out int port))
            Port = port;

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddTransient<PingApplication>();
        builder.Services.AddHostedService<ClientWorker>();
        builder.Services.AddHostedService<ServerWorker>();

        builder.Build().Run();
    }
}
