using System;
using System.Diagnostics;
using System.Threading;

namespace ThroughputBenchmark.Metrics;

/// <summary>
/// Measures packet latencies and aggregates throughput metrics for benchmark stages.
/// </summary>
public sealed class BenchmarkMetricsTracker
{
    private int _packetSize;
    private int _receivedCount;
    private long _startTimestamp;
    private double[]? _latencies;

    /// <summary>
    /// Gets the number of packets received so far in the current stage.
    /// </summary>
    public int ReceivedCount => Volatile.Read(ref _receivedCount);

    /// <summary>
    /// Gets the expected packet count for the current stage.
    /// </summary>
    public int ExpectedCount { get; private set; }

    /// <summary>
    /// Initializes tracking for a new benchmark stage.
    /// </summary>
    /// <param name="packetSize">Size of payload in bytes.</param>
    /// <param name="expectedCount">Expected number of packets.</param>
    public void StartStage(int packetSize, int expectedCount)
    {
        _packetSize = packetSize;
        ExpectedCount = expectedCount;
        _receivedCount = 0;
        _latencies = new double[expectedCount];
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Records an incoming echoed packet and its round-trip latency.
    /// </summary>
    /// <param name="sequence">Packet sequence number.</param>
    /// <param name="sentTimestamp">Timestamp when packet was transmitted.</param>
    /// <returns>The total number of packets recorded so far.</returns>
    public int RecordPacket(int sequence, long sentTimestamp)
    {
        long currentTimestamp = Stopwatch.GetTimestamp();
        double latencyMs = Stopwatch.GetElapsedTime(sentTimestamp, currentTimestamp).TotalMilliseconds;

        if (_latencies != null && sequence >= 0 && sequence < _latencies.Length)
            _latencies[sequence] = latencyMs;

        return Interlocked.Increment(ref _receivedCount);
    }

    /// <summary>
    /// Completes the benchmark stage and produces the final <see cref="StageResult"/>.
    /// </summary>
    /// <returns>The computed metrics for the stage.</returns>
    public StageResult CompleteStage()
    {
        long endTimestamp = Stopwatch.GetTimestamp();
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_startTimestamp, endTimestamp);

        if (_latencies is null || _latencies.Length is 0)
        {
            return new StageResult
            (
                _packetSize,
                0,
                elapsed,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0
            );
        }

        double[] latenciesCopy = (double[])_latencies.Clone();
        Array.Sort(latenciesCopy);

        double min = latenciesCopy[0];
        double max = latenciesCopy[^1];
        double sum = 0;
        foreach (double t in latenciesCopy)
            sum += t;

        double avg = sum / latenciesCopy.Length;
        double p50 = GetPercentile(latenciesCopy, 0.50);
        double p90 = GetPercentile(latenciesCopy, 0.90);
        double p95 = GetPercentile(latenciesCopy, 0.95);
        double p99 = GetPercentile(latenciesCopy, 0.99);

        double totalBytes = (double)_packetSize * ExpectedCount;
        double seconds = Math.Max(elapsed.TotalSeconds, 0.000001);
        double pps = ExpectedCount / seconds;
        double mbps = totalBytes / (1024.0 * 1024.0) / seconds;
        double gbps = totalBytes * 8.0 / 1_000_000_000.0 / seconds;

        return new StageResult
        (
            _packetSize,
            ExpectedCount,
            elapsed,
            pps,
            mbps,
            gbps,
            min,
            avg,
            p50,
            p90,
            p95,
            p99,
            max
        );
    }

    private static double GetPercentile(double[] sortedArray, double percentile)
    {
        if (sortedArray.Length == 0)
            return 0;

        int index = (int)Math.Round((sortedArray.Length - 1) * percentile);
        index = Math.Clamp(index, 0, sortedArray.Length - 1);
        return sortedArray[index];
    }
}
