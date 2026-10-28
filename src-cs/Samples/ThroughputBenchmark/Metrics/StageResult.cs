using System;

namespace ThroughputBenchmark.Metrics;

/// <summary>
/// Contains the aggregated performance metrics for a single benchmark stage.
/// </summary>
/// <param name="PacketSize">Size of the packet payload in bytes.</param>
/// <param name="PacketCount">Total number of packets transmitted and received.</param>
/// <param name="Elapsed">Total elapsed time for the stage.</param>
/// <param name="PacketsPerSecond">Achieved packet rate in packets/sec.</param>
/// <param name="ThroughputMegabytesPerSec">Payload throughput in megabytes per second (MB/s, 1024^2 bytes).</param>
/// <param name="ThroughputGigabitsPerSec">Payload throughput in gigabits per second (Gbps, 10^9 bits).</param>
/// <param name="MinLatencyMs">Minimum round-trip latency in milliseconds.</param>
/// <param name="AvgLatencyMs">Average round-trip latency in milliseconds.</param>
/// <param name="P50LatencyMs">50th percentile (median) round-trip latency in milliseconds.</param>
/// <param name="P90LatencyMs">90th percentile round-trip latency in milliseconds.</param>
/// <param name="P95LatencyMs">95th percentile round-trip latency in milliseconds.</param>
/// <param name="P99LatencyMs">99th percentile round-trip latency in milliseconds.</param>
/// <param name="MaxLatencyMs">Maximum round-trip latency in milliseconds.</param>
public sealed record StageResult
(
    int PacketSize,
    int PacketCount,
    TimeSpan Elapsed,
    double PacketsPerSecond,
    double ThroughputMegabytesPerSec,
    double ThroughputGigabitsPerSec,
    double MinLatencyMs,
    double AvgLatencyMs,
    double P50LatencyMs,
    double P90LatencyMs,
    double P95LatencyMs,
    double P99LatencyMs,
    double MaxLatencyMs
);
