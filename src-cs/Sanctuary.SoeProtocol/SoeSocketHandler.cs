using Microsoft.Extensions.Logging;
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
    private readonly SocketHandlerParams _parameters;
    private readonly Dictionary<SocketAddress, SoeProtocolHandler> _sessions = [];
    private readonly NativeSpanPool _pool;
    private readonly Socket _socket;
    private readonly byte[] _receiveBuffer;

    private bool _isDisposed;
    private CancellationTokenSource? _internalCts;
    private (SocketAddress, Memory<byte>)? _alreadyReceived;

    public SoeSocketHandler
    (
        ILogger<SoeSocketHandler> logger,
        SocketHandlerParams parameters
    )
    {
        _logger = logger;
        _parameters = parameters;
        SessionParameters ssnParams = parameters.DefaultSessionParams;

        int maxDataLen = (int)Math.Max(ssnParams.UdpLength, ssnParams.RemoteUdpLength);
        _pool = new NativeSpanPool(maxDataLen, parameters.PacketPoolSize);

        int maxSocketLen = maxDataLen * 64;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            Blocking = false,
            ReceiveBufferSize = maxSocketLen,
            SendBufferSize = maxSocketLen
        };
        _receiveBuffer = GC.AllocateArray<byte>((int)ssnParams.UdpLength * 32);
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
        _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        while (!_internalCts.IsCancellationRequested)
        {
            bool runNextTick = RunTick(_internalCts.Token);
            if (runNextTick)
                continue;

            _socket.ReceiveTimeout = 1;
            SocketAddress address = new(AddressFamily.InterNetwork);
            int recvAmount = await _socket.ReceiveFromAsync(_receiveBuffer, SocketFlags.None, address, ct);
            if (recvAmount > 0)
                _alreadyReceived = (address, _receiveBuffer.AsMemory(0, recvAmount));
        }

        _internalCts.Dispose();
    }

    /// <summary>
    /// Runs a tick of operations.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to cancel this operation.</param>
    /// <returns><c>True</c> if another tick must be run as soon as possible.</returns>
    public bool RunTick(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

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
        SocketAddress remoteAddress = new(AddressFamily.InterNetwork);
        Span<byte> received;

        if (_alreadyReceived is not null)
        {
            remoteAddress = _alreadyReceived.Value.Item1;
            received = _alreadyReceived.Value.Item2.Span;
            _alreadyReceived = null;
        }
        else
        {
            if (_socket.Available is 0)
                return false;

            int receivedLen = _socket.ReceiveFrom(_receiveBuffer, SocketFlags.None, remoteAddress);
            if (receivedLen is 0)
                return false;
            received = _receiveBuffer.AsSpan(0, receivedLen);
        }

        if (!_sessions.TryGetValue(remoteAddress, out SoeProtocolHandler? session))
        {
            switch (SoePacketUtils.ReadSoeOpCode(_receiveBuffer))
            {
                case SoeOpCode.SessionRequest:
                    session = CreateSession(remoteAddress, SessionMode.Server);
                    break;
                case SoeOpCode.RemapConnection:
                    RemapConnection remap = RemapConnection.Deserialize(_receiveBuffer, true);
                    RemapSession(remoteAddress, remap);
                    return true;
                default:
                    return true;
            }
        }

        NativeSpan span = _pool.Rent();
        span.CopyDataInto(received);
        session.EnqueuePacket(span);

        return true;
    }

    private SoeProtocolHandler CreateSession(SocketAddress address, SessionMode mode)
    {
        SoeProtocolHandler handler = new
        (
            address,
            mode,
            _parameters.DefaultSessionParams.Clone(),
            _pool,
            new SocketNetworkWriter(address, _socket),
            _parameters.AppCreationCallback()
        );
        _sessions.Add(address, handler);

        handler.Initialize();

        return handler;
    }

    private void DestroySession(SoeProtocolHandler session)
    {
        if (session.TerminationReason is not DisconnectReason.None)
        {
            _logger.LogDebug
            (
                "Destroying pre-terminated {Mode} session ({Id}), which had reason {Reason}",
                session.Mode,
                session.SessionId,
                session.TerminationReason
            );
        }

        session.TerminateSession();
        _sessions.Remove(session.Remote);
        session.Dispose();

        if (_sessions.Count is 0 && _parameters.StopOnLastSessionTerminated)
            _internalCts?.Cancel();
    }

    private void RemapSession(SocketAddress address, RemapConnection remapRequest)
    {
        if (!_parameters.AllowPortRemaps)
            return;

        IPEndPoint dummy = new(IPAddress.Any, 0);
        SoeProtocolHandler? handler = null;

        foreach (SoeProtocolHandler element in _sessions.Values)
        {
            if (element.SessionId == remapRequest.SessionId && element.SessionParams.CrcSeed == remapRequest.CrcSeed)
            {
                handler = element;
                break;
            }
        }

        // If we couldn't find a matching handler then just blindly return. No need to notify the sender
        if (handler is null)
            return;

        IPEndPoint newRemote = (IPEndPoint)dummy.Create(address);
        IPEndPoint oldRemote = (IPEndPoint)dummy.Create(handler.Remote);

        // We do NOT want to handle IP address remaps - this is probably someone trying to hijack a session.
        if (!newRemote.Address.Equals(oldRemote.Address))
            return;

        // At this point only the port has changed - probably due to NAT. We're happy to remap this
        _sessions.Remove(handler.Remote);
        handler.Remote = address;
        _sessions.Add(address, handler);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposeManaged)
    {
        if (!disposeManaged || _isDisposed)
            return;

        _socket.Dispose();

        foreach (SoeProtocolHandler session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();

        _isDisposed = true;
    }
}
