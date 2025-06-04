using Microsoft.Extensions.Logging;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol;

/// <summary>
/// Manages a UDP socket and any SOE connections on that socket.
/// </summary>
public class SoeSocketHandler : IDisposable
{
    private readonly ILogger<SoeSocketHandler> _logger;
    private readonly SessionParameters _defaultSessionParams;
    private readonly Func<IApplicationProtocolHandler> _createApplication;
    private readonly Dictionary<SocketAddress, SoeProtocolHandler> _sessions = [];
    private readonly NativeSpanPool _pool;
    private readonly Socket _socket;
    private readonly byte[] _receiveBuffer;

    public SoeSocketHandler
    (
        ILogger<SoeSocketHandler> logger,
        SessionParameters defaultSessionParams,
        Func<IApplicationProtocolHandler> createApplication
    )
    {
        _logger = logger;
        _defaultSessionParams = defaultSessionParams;
        _createApplication = createApplication;

        int maxDataLen = (int)Math.Max(defaultSessionParams.UdpLength, defaultSessionParams.RemoteUdpLength);
        _pool = new NativeSpanPool(maxDataLen, 5192); // TODO: This should be configurable. User should probably supply the pool

        int maxSocketLen = maxDataLen * 64;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            Blocking = false,
            ReceiveBufferSize = maxSocketLen,
            SendBufferSize = maxSocketLen
        };
        _receiveBuffer = GC.AllocateArray<byte>((int)defaultSessionParams.UdpLength * 32);
    }

    /// <summary>
    /// Binds this socket handler to an endpoint, readying it to act as a server.
    /// </summary>
    /// <param name="endpoint">The endpoint to listen on.</param>
    public void Bind(IPEndPoint endpoint)
        => _socket.Bind(endpoint);

    /// <summary>
    /// Creates a session and connects it to the given remote address.
    /// </summary>
    /// <param name="remote">The address of the remote to connect to.</param>
    /// <returns></returns>
    public SoeProtocolHandler Connect(IPEndPoint remote)
    {
        SoeProtocolHandler session = CreateSession(remote.Serialize(), SessionMode.Client);
        session.SendSessionRequest();
        return session;
    }

    /// <summary>
    /// Asynchronously runs the <see cref="SoeSocketHandler"/>. This method will not return until cancelled.
    /// </summary>
    /// <param name="ct"></param>
    public async Task RunAsync(CancellationToken ct)
    {
        await Task.Yield();
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(1));

        while (!ct.IsCancellationRequested)
        {
            bool runNextTick = RunTick(ct);

            // No need to cancel, it's a 1ms timer, and we don't want to handle OperationCanceledException
            if (!runNextTick)
                await timer.WaitForNextTickAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs a tick of operations.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to cancel this operation.</param>
    /// <returns><c>True</c> if another tick must be run as soon as possible.</returns>
    public bool RunTick(CancellationToken ct)
    {
        bool runNextTick = ProcessOneFromSocket();

        List<SoeProtocolHandler> toRemove = new(16);
        foreach (SoeProtocolHandler session in _sessions.Values)
        {
            if (session.TerminationReason is not DisconnectReason.None || session.State is SessionState.Terminated)
            {
                toRemove.Add(session);
                continue;
            }

            bool needsMoreTime = false;
            try
            {
                if (!session.RunTick(out needsMoreTime, ct))
                    toRemove.Add(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run a tick of a protocol handler");
            }

            if (needsMoreTime)
                runNextTick = true;
        }

        foreach (SoeProtocolHandler session in toRemove)
        {
            try
            {
                DestroySession(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to destroy a session");
            }
        }

        return runNextTick;
    }

    private bool ProcessOneFromSocket()
    {
        if (_socket.Available is 0)
            return false;

        SocketAddress remoteAddress = new(AddressFamily.InterNetwork);
        int receivedLen = _socket.ReceiveFrom(_receiveBuffer, SocketFlags.None, remoteAddress);
        if (receivedLen is 0)
            return false;

        if (!_sessions.TryGetValue(remoteAddress, out SoeProtocolHandler? session))
        {
            // TODO: Check for remap request
            session = CreateSession(remoteAddress, SessionMode.Server);
        }

        NativeSpan span = _pool.Rent();
        span.CopyDataInto(_receiveBuffer.AsSpan(0, receivedLen));
        session.EnqueuePacket(span);

        return true;
    }

    private SoeProtocolHandler CreateSession(SocketAddress address, SessionMode mode)
    {
        SoeProtocolHandler handler = new
        (
            address,
            mode,
            _defaultSessionParams.Clone(),
            _pool,
            new SocketNetworkWriter(address, _socket),
            _createApplication()
        );

        _sessions.Add(address, handler);

        return handler;
    }

    private void DestroySession(SoeProtocolHandler session)
    {
        if (session.TerminationReason is not DisconnectReason.None)
            _logger.LogDebug("Destroying pre-terminated session, which had reason {Reason}", session.TerminationReason);

        session.TerminateSession();
        _sessions.Remove(session.Remote);
        session.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposeManaged)
    {
        if (!disposeManaged)
            return;

        _socket.Dispose();

        foreach (SoeProtocolHandler session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
    }
}
