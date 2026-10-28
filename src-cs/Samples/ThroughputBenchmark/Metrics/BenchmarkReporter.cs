using System;
using System.Collections.Generic;

namespace ThroughputBenchmark.Metrics;

/// <summary>
/// Renders console logs and ASCII summary tables for benchmark results.
/// </summary>
public static class BenchmarkReporter
{
    /// <summary>
    /// Formats a byte size into a human-readable string (e.g. 64 B, 1.00 KB).
    /// </summary>
    /// <param name="bytes">Byte count.</param>
    /// <returns>Formatted byte string.</returns>
    public static string FormatBytes(int bytes)
        => bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F2} MB",
            >= 1024 => $"{bytes / 1024.0:F0} KB",
            _ => $"{bytes} B"
        };

    /// <summary>
    /// Prints the single-stage completed metrics line.
    /// </summary>
    /// <param name="result">The stage result.</param>
    public static void PrintStageResult(StageResult result)
    {
        string sizeStr = FormatBytes(result.PacketSize);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"[{sizeStr,6}] ");
        Console.ResetColor();

        Console.WriteLine
        (
            $"{result.PacketCount,6:N0} pkts in {result.Elapsed.TotalSeconds,5:F2}s | "
                + $"{result.PacketsPerSecond,8:N0} pkt/s | "
                + $"{result.ThroughputMegabytesPerSec,7:F2} MB/s ({result.ThroughputGigabitsPerSec,5:F3} Gbps) | "
                + $"Latency (ms): min={result.MinLatencyMs:F2} avg={result.AvgLatencyMs:F2} "
                + $"p50={result.P50LatencyMs:F2} p95={result.P95LatencyMs:F2} p99={result.P99LatencyMs:F2} max={result.MaxLatencyMs:F2}"
        );
        Console.Out.Flush();
    }

    /// <summary>
    /// Prints a comprehensive summary table for all executed stages.
    /// </summary>
    /// <param name="results">List of all stage results.</param>
    public static void PrintSummary(IReadOnlyList<StageResult> results)
    {
        if (results.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine(new string('=', 116));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{"SOE PROTOCOL THROUGHPUT BENCHMARK RESULTS",75}");
        Console.ResetColor();
        Console.WriteLine(new string('=', 116));

        Console.WriteLine
        (
            $" {"Payload",-9} | {"Packets",-8} | {"Time (s)",-8} | {"Packets/s",-10} | {"MB/s",-9} | {"Gbps",-8} | "
                + $"{"Avg (ms)",-8} | {"P50 (ms)",-8} | {"P95 (ms)",-8} | {"P99 (ms)",-8} | {"Max (ms)",-8}"
        );
        Console.WriteLine(new string('-', 116));

        foreach (StageResult res in results)
        {
            string sizeStr = FormatBytes(res.PacketSize);
            Console.WriteLine
            (
                $" {sizeStr,-9} | {res.PacketCount,8:N0} | {res.Elapsed.TotalSeconds,8:F3} | {res.PacketsPerSecond,10:N0} | "
                    + $"{res.ThroughputMegabytesPerSec,9:F2} | {res.ThroughputGigabitsPerSec,8:F3} | {res.AvgLatencyMs,8:F2} | "
                    + $"{res.P50LatencyMs,8:F2} | {res.P95LatencyMs,8:F2} | {res.P99LatencyMs,8:F2} | {res.MaxLatencyMs,8:F2}"
            );
        }

        Console.WriteLine(new string('=', 116));
        Console.WriteLine();
        Console.Out.Flush();
    }
}
