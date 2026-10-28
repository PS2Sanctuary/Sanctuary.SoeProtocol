using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ThroughputBenchmark.Metrics;

namespace ThroughputBenchmark;

/// <summary>
/// Orchestrates the multi-stage throughput and latency benchmark on the server.
/// </summary>
public sealed class BenchmarkServerApplication : IApplicationProtocolHandler
{
    private readonly ILogger<BenchmarkServerApplication> _logger;
    private readonly CommandLineOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly BenchmarkMetricsTracker _metricsTracker = new();
    private readonly List<StageResult> _results = [];
    private readonly CancellationTokenSource _cts = new();

    private ISessionHandler? _sessionHandler;
    private SemaphoreSlim? _window;
    private TaskCompletionSource<bool>? _stageCompletionSource;
    private TaskCompletionSource<bool>? _warmupCompletionSource;
    private int _currentStageIndex = -1;
    private int _warmupReceivedCount;

    /// <inheritdoc />
    public ApplicationParameters SessionParams { get; } = new(null);

    /// <summary>
    /// Initializes a new instance of the <see cref="BenchmarkServerApplication"/> class.
    /// </summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="options">Parsed CLI options.</param>
    /// <param name="lifetime">The host application lifetime.</param>
    public BenchmarkServerApplication
    (
        ILogger<BenchmarkServerApplication> logger,
        CommandLineOptions options,
        IHostApplicationLifetime lifetime
    )
    {
        _logger = logger;
        _options = options;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public void Initialise(ISessionHandler sessionHandler)
    {
        _sessionHandler = sessionHandler;
    }

    /// <inheritdoc />
    public void OnSessionOpened()
    {
        Console.WriteLine("[Server] Session opened. Initializing benchmark run...");
        Console.Out.Flush();
        _logger.LogInformation("Server session established with client. Initializing benchmark run...");
        Task.Run(RunBenchmarkAsync);
    }

    private async Task RunBenchmarkAsync()
    {
        try
        {
            // Allow connection and socket pipeline to settle
            await Task.Delay(200, _cts.Token);

            if (_options.WarmupCount > 0)
            {
                _currentStageIndex = 0;
                _logger.LogInformation("Executing warmup phase ({Count:N0} packets)...", _options.WarmupCount);
                _warmupReceivedCount = 0;
                _warmupCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                int warmupSize = Math.Max(_options.MinPacketSize, BenchmarkPacket.HeaderSize);
                byte[] warmupBuffer = new byte[warmupSize];
                _window = new SemaphoreSlim(64, 64);

                for (int i = 0; i < _options.WarmupCount; i++)
                {
                    await _window.WaitAsync(_cts.Token);
                    BenchmarkPacket pkt = new(BenchmarkMessageType.Data, 0, i, Stopwatch.GetTimestamp());
                    pkt.WriteTo(warmupBuffer.AsSpan(0, BenchmarkPacket.HeaderSize));
                    _sessionHandler!.EnqueueData(warmupBuffer);
                }

                using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(15));
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource
                (
                    _cts.Token,
                    timeoutCts.Token
                );
                await _warmupCompletionSource.Task.WaitAsync(linkedCts.Token);

                _logger.LogInformation("Warmup phase complete. Commencing benchmark stages...");
                await Task.Delay(200, _cts.Token);
            }

            List<int> sizes = GeneratePayloadSizes(_options.MinPacketSize, _options.MaxPacketSize);
            _logger.LogInformation
            (
                "Running benchmark across {Count} payload size stages ({Min} to {Max}, {Packets:N0} packets/stage)...",
                sizes.Count,
                BenchmarkReporter.FormatBytes(_options.MinPacketSize),
                BenchmarkReporter.FormatBytes(_options.MaxPacketSize),
                _options.PacketCount
            );

            for (int stageIndex = 0; stageIndex < sizes.Count; stageIndex++)
            {
                if (_cts.IsCancellationRequested)
                    break;

                int size = sizes[stageIndex];
                _currentStageIndex = stageIndex + 1;
                int packetCount = _options.PacketCount;

                _metricsTracker.StartStage(size, packetCount);
                _stageCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Compute adaptive window size to keep in-flight reliable fragments well within queue capacity
                int fragmentsPerPacket = Math.Max(1, (int)Math.Ceiling(size / 480.0));
                int maxInFlight = Math.Clamp(64 / fragmentsPerPacket, 1, 64);
                _window = new SemaphoreSlim(maxInFlight, maxInFlight);

                byte[] payload = new byte[size];
                Random.Shared.NextBytes(payload);

                for (int seq = 0; seq < packetCount; seq++)
                {
                    await _window.WaitAsync(_cts.Token);

                    BenchmarkPacket pkt = new
                    (
                        BenchmarkMessageType.Data,
                        _currentStageIndex,
                        seq,
                        Stopwatch.GetTimestamp()
                    );
                    pkt.WriteTo(payload.AsSpan(0, BenchmarkPacket.HeaderSize));
                    _sessionHandler!.EnqueueData(payload);
                }

                // Wait for all echoed packets for this stage
                await _stageCompletionSource.Task.WaitAsync(_cts.Token);

                StageResult result = _metricsTracker.CompleteStage();
                _results.Add(result);
                BenchmarkReporter.PrintStageResult(result);

                // Small pause between stages to let sockets flush cleanly
                await Task.Delay(100, _cts.Token);
            }

            BenchmarkReporter.PrintSummary(_results);
            _logger.LogInformation("All benchmark stages completed successfully.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Benchmark execution was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during benchmark execution.");
        }
        finally
        {
            _sessionHandler?.TerminateSession();
            _lifetime.StopApplication();
        }
    }

    /// <inheritdoc />
    public void HandleAppData(ReadOnlySpan<byte> data)
    {
        if (!BenchmarkPacket.TryRead(data, out BenchmarkPacket packet))
            return;

        if (packet.Type != BenchmarkMessageType.Data)
            return;

        if (packet.Stage != _currentStageIndex)
            return;

        try
        {
            _window?.Release();
        }
        catch (SemaphoreFullException)
        {
        }

        if (_currentStageIndex == 0)
        {
            int received = Interlocked.Increment(ref _warmupReceivedCount);
            if (received >= _options.WarmupCount)
                _warmupCompletionSource?.TrySetResult(true);
            return;
        }

        int totalReceived = _metricsTracker.RecordPacket(packet.Sequence, packet.Timestamp);
        if (totalReceived >= _metricsTracker.ExpectedCount)
        {
            _stageCompletionSource?.TrySetResult(true);
        }
    }

    /// <inheritdoc />
    public void OnSessionClosed(DisconnectReason disconnectReason)
    {
        _logger.LogInformation("Server session closed ({Reason}).", disconnectReason);
        _cts.Cancel();
        _stageCompletionSource?.TrySetCanceled();
        _warmupCompletionSource?.TrySetCanceled();
        _lifetime.StopApplication();
    }

    private static List<int> GeneratePayloadSizes(int min, int max)
    {
        List<int> sizes = [];
        int current = min;
        while (current <= max)
        {
            sizes.Add(current);
            if (current == max)
                break;

            long next = (long)current * 2;
            if (next > max)
            {
                if (sizes[^1] != max)
                    sizes.Add(max);
                break;
            }

            current = (int)next;
        }

        return sizes;
    }
}
