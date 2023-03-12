using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Abstractions.Services;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Util;
using System;
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
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SingleSessionManager));

        await Task.Yield();

        using CancellationTokenSource internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using UdpSocketNetworkInterface networkInterface = new
        (
            (int)_sessionParams.UdpLength,
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
            _mode,
            _sessionParams,
            _spanPool,
            networkInterface,
            _application
        );

        Task receiveTask = RunReceiveLoopAsync(networkInterface, internalCts.Token);
        Task handlerTask = _protocolHandler.RunAsync(internalCts.Token);

        try
        {
            if (_mode is SessionMode.Client)
                _protocolHandler.SendSessionRequest();
            await Task.WhenAny(receiveTask, handlerTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{nameof(SingleSessionManager)} run failure");
        }

        internalCts.Cancel();

        try
        {
            await Task.WhenAll(receiveTask, handlerTask);
        }
        catch (AggregateException aex)
        {
            foreach (Exception ex in aex.InnerExceptions)
                _logger.LogError(ex, $"{nameof(SingleSessionManager)} run failure");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{nameof(SingleSessionManager)} run failure");
        }

        receiveTask.Dispose();
        handlerTask.Dispose();

        _logger.LogDebug
        (
            "{Mode} session {ID} closed with reason: {Reason}",
            _mode,
            _protocolHandler?.SessionId,
            _protocolHandler?.TerminationReason
        );
    }

    private async Task RunReceiveLoopAsync(INetworkReader networkReader, CancellationToken ct)
    {
        if (_protocolHandler is null)
            throw new InvalidOperationException("Cannot run the receive loop while the protocol handler is null");

        await Task.Yield();
        byte[] receiveBuffer = GC.AllocateArray<byte>((int)_sessionParams.UdpLength, true);

        try
        {
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
        catch (OperationCanceledException)
        {
            // This is fine
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _spanPool.Dispose();
        _protocolHandler?.Dispose();

        _isDisposed = true;
    }
}
