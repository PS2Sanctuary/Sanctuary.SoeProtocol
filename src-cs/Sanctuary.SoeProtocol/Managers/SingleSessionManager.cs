using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol.Managers;

/// <summary>
/// A manager for a single-remote SOE protocol session.
/// </summary>
public sealed class SingleSessionManager : IDisposable
{
    private readonly ILogger<SingleSessionManager> _logger;
    private readonly SessionParameters _sessionParams;
    private readonly IApplicationProtocolHandler _application;
    private readonly IPEndPoint _remoteEndPoint;
    private readonly NativeSpanPool _spanPool;
    private readonly SessionMode _mode;

    private SoeProtocolHandler? _protocolHandler;
    private bool _isDisposed;

    /// <summary>
    /// Gets statistics related to receiving reliable data.
    /// </summary>
    public DataInputStats? ReliableDataReceiveStats => _protocolHandler?.ReliableDataReceiveStats;

    /// <summary>
    /// Gets statistics reliated to sending reliable data.
    /// </summary>
    public DataOutputStats? ReliableDataSendStats => _protocolHandler?.ReliableDataSendStats;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleSessionManager"/> class.
    /// </summary>
    /// <param name="logger">The logging interface to use.</param>
    /// <param name="sessionParams">The session parameters to use.</param>
    /// <param name="application">The application protocol handler.</param>
    /// <param name="connectTo">The endpoint to connect to.</param>
    /// <param name="mode">The operation mode of the </param>
    public SingleSessionManager
    (
        ILogger<SingleSessionManager> logger,
        SessionParameters sessionParams,
        IApplicationProtocolHandler application,
        IPEndPoint connectTo,
        SessionMode mode
    )
    {
        _logger = logger;
        _sessionParams = sessionParams;
        _application = application;
        _remoteEndPoint = connectTo;
        _mode = mode;

        int bufferSize = (int)Math.Max(sessionParams.UdpLength, sessionParams.RemoteUdpLength);
        _spanPool = new NativeSpanPool(bufferSize, sessionParams.MaxQueuedRawPackets);
    }

    /// <summary>
    /// Runs the client. This method will not return until cancelled.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
    public async Task RunAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, typeof(SingleSessionManager));

        await Task.Yield();

        using CancellationTokenSource internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using UdpSocketNetworkInterface networkInterface = Initialize();
        using Task receiveTask = RunReceiveLoopAsync(networkInterface, internalCts.Token);
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(1));

        try
        {
            if (_mode is SessionMode.Client)
                _protocolHandler.SendSessionRequest();
            _protocolHandler.Initialize();

            while (!ct.IsCancellationRequested)
            {
                // Run a tick of the protocol handler. If this returns false, the protocol handler has terminated
                if (!_protocolHandler.RunTick(out bool needsMoreTime, internalCts.Token))
                    break;

                if (receiveTask.IsCompleted)
                    break;

                // If the protocol handler didn't need to process again immediately, have a cooldown
                if (!needsMoreTime)
                    await timer.WaitForNextTickAsync(internalCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // This is fine
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{nameof(SingleSessionManager)} run loop failure");
        }
        finally
        {
            _protocolHandler.TerminateSession();
        }

        // Guaranteed cleanup, in case of an exception in the above try-catch
        try
        {
            internalCts.Cancel();
            await receiveTask;
        }
        catch (OperationCanceledException)
        {
            // This is fine
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{nameof(SingleSessionManager)} task cleanup failure");
        }

        _logger.LogDebug
        (
            "{Mode} session {ID} closed with reason: {Reason}",
            _mode,
            _protocolHandler?.SessionId,
            _protocolHandler?.TerminationReason
        );
    }

    private async Task RunReceiveLoopAsync(UdpSocketNetworkInterface networkReader, CancellationToken ct)
    {
        if (_protocolHandler is null)
            throw new InvalidOperationException("Cannot run the receive loop while the protocol handler is null");

        await Task.Yield();
        byte[] receiveBuffer = GC.AllocateArray<byte>((int)_sessionParams.UdpLength, true);

        while (!ct.IsCancellationRequested)
        {
            int amount = await networkReader.ReceiveAsync(receiveBuffer, ct).ConfigureAwait(false);
            if (amount <= 0)
                continue;

            NativeSpan span = _spanPool.Rent();
            span.CopyDataInto(receiveBuffer.AsSpan(0, amount));

            if (!_protocolHandler.EnqueuePacket(span))
                _spanPool.Return(span);
        }
    }

    [MemberNotNull(nameof(_protocolHandler))]
    private UdpSocketNetworkInterface Initialize()
    {
        UdpSocketNetworkInterface networkInterface = new
        (
            (int)_sessionParams.UdpLength * 64,
            _mode == SessionMode.Server
        );

        switch (_mode)
        {
            case SessionMode.Client:
                networkInterface.Connect(_remoteEndPoint);
                break;
            case SessionMode.Server:
                networkInterface.Bind(_remoteEndPoint);
                break;
            default:
                throw new InvalidOperationException("Unknown mode");
        }

        _protocolHandler?.Dispose();
        _protocolHandler = new SoeProtocolHandler
        (
            _remoteEndPoint.Serialize(),
            _mode,
            _sessionParams,
            _spanPool,
            networkInterface,
            _application
        );

        return networkInterface;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _protocolHandler?.Dispose();

        _isDisposed = true;
    }
}
