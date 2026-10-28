using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ThroughputBenchmark;

/// <summary>
/// Entry class for the throughput benchmark executable.
/// </summary>
public static class Program
{
    /// <summary>
    /// Entry point of the throughput benchmark.
    /// </summary>
    /// <param name="args">Launch arguments.</param>
    public static void Main(string[] args)
    {
        if (!CommandLineOptions.TryParse(args, out CommandLineOptions? options, out string? error))
        {
            if (error != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {error}");
                Console.ResetColor();
                Console.WriteLine();
            }

            CommandLineOptions.PrintHelp();
            Environment.ExitCode = 1;
            return;
        }

        if (options!.ShowHelp)
        {
            CommandLineOptions.PrintHelp();
            return;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(options);

        if (options.IsServer)
        {
            builder.Services.AddTransient<BenchmarkServerApplication>();
            builder.Services.AddHostedService<ServerWorker>();
        }
        else if (options.IsClient)
        {
            builder.Services.AddTransient<BenchmarkClientApplication>();
            builder.Services.AddHostedService<ClientWorker>();
        }

        builder.Build().Run();
    }
}
