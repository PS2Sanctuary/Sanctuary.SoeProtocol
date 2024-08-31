using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sanctuary.SoeProtocol.Util;
using System;
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
        Test();
        return;

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

    public static void Test()
    {
        byte[] data = new byte[8];
        BinaryWriter writer = new(data);

        writer.WriteUInt32BE(ushort.MaxValue + 1);
        writer.WriteUInt32BE(uint.MaxValue);
        foreach (byte element in data)
            Console.Write("0x{0:X2}, ", element);
    }
}
