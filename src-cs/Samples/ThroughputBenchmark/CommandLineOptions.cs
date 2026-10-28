using System;
using System.Net;

namespace ThroughputBenchmark;

/// <summary>
/// Represents parsed command line options for the benchmark.
/// </summary>
public sealed class CommandLineOptions
{
    /// <summary>
    /// Gets a value indicating whether server mode is enabled.
    /// </summary>
    public bool IsServer { get; init; }

    /// <summary>
    /// Gets a value indicating whether client mode is enabled.
    /// </summary>
    public bool IsClient { get; init; }

    /// <summary>
    /// Gets the target IP address.
    /// </summary>
    public IPAddress Address { get; init; } = IPAddress.Loopback;

    /// <summary>
    /// Gets the target port.
    /// </summary>
    public int Port { get; init; } = 12345;

    /// <summary>
    /// Gets the number of packets per size stage.
    /// </summary>
    public int PacketCount { get; init; } = 10_000;

    /// <summary>
    /// Gets the minimum packet size in bytes.
    /// </summary>
    public int MinPacketSize { get; init; } = 64;

    /// <summary>
    /// Gets the maximum packet size in bytes.
    /// </summary>
    public int MaxPacketSize { get; init; } = 65_536;

    /// <summary>
    /// Gets the number of warmup packets.
    /// </summary>
    public int WarmupCount { get; init; } = 500;

    /// <summary>
    /// Gets a value indicating whether help is requested.
    /// </summary>
    public bool ShowHelp { get; init; }

    /// <summary>
    /// Parses command line arguments.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="options">The parsed options if successful.</param>
    /// <param name="error">The error message if parsing failed.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
    public static bool TryParse(string[] args, out CommandLineOptions? options, out string? error)
    {
        bool isServer = false;
        bool isClient = false;
        IPAddress address = IPAddress.Loopback;
        int port = 12345;
        int packetCount = 10_000;
        int minPacketSize = 64;
        int maxPacketSize = 65_536;
        int warmupCount = 500;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "--help" or "-h" or "-?")
            {
                showHelp = true;
                break;
            }

            if (arg is "--server" or "-s")
            {
                isServer = true;
                continue;
            }

            if (arg is "--client" or "-c")
            {
                isClient = true;
                continue;
            }

            if (arg is "--port" or "-p")
            {
                if (++i >= args.Length || !int.TryParse(args[i], out port) || port is <= 0 or > 65535)
                {
                    options = null;
                    error = "Invalid port value specified. Must be between 1 and 65535.";
                    return false;
                }
                continue;
            }

            if (arg is "--address" or "-a")
            {
                if (++i >= args.Length || !IPAddress.TryParse(args[i], out IPAddress? parsedAddress))
                {
                    options = null;
                    error = "Invalid IP address specified.";
                    return false;
                }
                address = parsedAddress;
                continue;
            }

            if (arg is "--packet-count" or "-n")
            {
                if (++i >= args.Length || !int.TryParse(args[i], out packetCount) || packetCount <= 0)
                {
                    options = null;
                    error = "Invalid packet count. Must be a positive integer.";
                    return false;
                }
                continue;
            }

            if (arg is "--min-size")
            {
                if (++i >= args.Length || !int.TryParse(args[i], out minPacketSize) || minPacketSize < 16)
                {
                    options = null;
                    error = "Invalid min-size. Must be at least 16 bytes.";
                    return false;
                }
                continue;
            }

            if (arg is "--max-size")
            {
                if (++i >= args.Length || !int.TryParse(args[i], out maxPacketSize) || maxPacketSize < 16)
                {
                    options = null;
                    error = "Invalid max-size. Must be at least 16 bytes.";
                    return false;
                }
                continue;
            }

            if (arg is "--warmup" or "-w")
            {
                if (++i >= args.Length || !int.TryParse(args[i], out warmupCount) || warmupCount < 0)
                {
                    options = null;
                    error = "Invalid warmup count. Must be non-negative.";
                    return false;
                }
                continue;
            }

            options = null;
            error = $"Unrecognized option: {arg}";
            return false;
        }

        if (showHelp)
        {
            options = new CommandLineOptions { ShowHelp = true };
            error = null;
            return true;
        }

        if (isServer == isClient)
        {
            options = null;
            error = "Must specify either --server (-s) or --client (-c).";
            return false;
        }

        if (minPacketSize > maxPacketSize)
        {
            options = null;
            error = $"Min packet size ({minPacketSize}) cannot exceed max packet size ({maxPacketSize}).";
            return false;
        }

        options = new CommandLineOptions
        {
            IsServer = isServer,
            IsClient = isClient,
            Address = address,
            Port = port,
            PacketCount = packetCount,
            MinPacketSize = minPacketSize,
            MaxPacketSize = maxPacketSize,
            WarmupCount = warmupCount
        };
        error = null;
        return true;
    }

    /// <summary>
    /// Displays command usage and help information.
    /// </summary>
    public static void PrintHelp()
    {
        Console.WriteLine("SOE Protocol Throughput Benchmark");
        Console.WriteLine("Usage: dotnet run --project Samples/ThroughputBenchmark -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --server, -s              Run as benchmark server");
        Console.WriteLine("  --client, -c              Run as benchmark client");
        Console.WriteLine("  --address, -a <ip>        Target IP address (default: 127.0.0.1)");
        Console.WriteLine("  --port, -p <port>         Target UDP port (default: 12345)");
        Console.WriteLine("  --packet-count, -n <num>  Number of packets per stage (default: 10000)");
        Console.WriteLine("  --min-size <bytes>        Minimum payload size (default: 64, min: 16)");
        Console.WriteLine("  --max-size <bytes>        Maximum payload size (default: 65536, min: 16)");
        Console.WriteLine("  --warmup, -w <num>        Warmup packet count (default: 500)");
        Console.WriteLine("  --help, -h                Show this help message");
    }
}
