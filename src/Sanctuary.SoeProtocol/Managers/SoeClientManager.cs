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

public sealed class SoeClientManager : IDisposable
{
    private readonly ILogger<SoeClientManager> _logger;
    private readonly IApplicationProtocolHandler _application;
    private readonly IPEndPoint _remoteEndPoint;
    private readonly NativeSpanPool _spanPool;

    private SoeProtocolHandler? _protocolHandler;
    private bool _isDisposed;

    public SoeClientManager
    (
        ILogger<SoeClientManager> logger,
        IApplicationProtocolHandler application,
        IPEndPoint connectTo
    )
    {
        _logger = logger;
        _application = application;
        _remoteEndPoint = connectTo;

        int bufferSize = (int)Math.Max(_application.SessionParams.UdpLength, _application.SessionParams.RemoteUdpLength);
        _spanPool = new NativeSpanPool(bufferSize, _application.SessionParams.MaxQueuedRawPackets);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SoeClientManager));

        await Task.Yield();

        using UdpSocketNetworkInterface networkInterface = new((int)_application.SessionParams.UdpLength);
        networkInterface.Connect(_remoteEndPoint);

        _protocolHandler = new SoeProtocolHandler
        (
            SessionMode.Client,
            _application.SessionParams,
            _spanPool,
            networkInterface,
            _application
        );

        Task receiveTask = RunReceiveLoopAsync(networkInterface, ct);
        Task handlerTask = _protocolHandler.RunAsync(ct);

        try
        {
            await Task.WhenAny(receiveTask, handlerTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SoeClientManager run failure");
        }

        // Just in case we had an error in the receive loop
        if (_protocolHandler.State is not SessionState.Terminated)
            _protocolHandler.TerminateSession();

        receiveTask.Dispose();
        handlerTask.Dispose();
    }

    private async Task RunReceiveLoopAsync(INetworkReader networkReader, CancellationToken ct)
    {
        if (_protocolHandler is null)
            throw new InvalidOperationException("Cannot run the receive loop while the protocol handler is null");

        await Task.Yield();
        byte[] receiveBuffer = GC.AllocateArray<byte>((int)_application.SessionParams.UdpLength, true);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int amount = await networkReader.ReceiveAsync(receiveBuffer, ct).ConfigureAwait(false);
                if (amount <= 0)
                    continue;

                NativeSpan span = _spanPool.Rent();
                span.CopyDataInto(receiveBuffer.AsSpan(0, amount));
                _protocolHandler.EnqueuePacket(span);
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

        _protocolHandler?.Dispose();

        if (_application is IDisposable disposable)
            disposable.Dispose();

        _isDisposed = true;
    }
}
