using Sanctuary.SoeProtocol.Abstractions.Services;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol.Services;

/// <summary>
/// Provides an implementation of the <see cref="INetworkInterface"/>
/// that sends/receives UDP data using a <see cref="Socket"/>.
/// </summary>
public sealed class UdpSocketNetworkInterface : INetworkInterface, IDisposable
{
    private readonly Socket _socket;
    private readonly bool _connectOnReceive;
    private readonly bool _disposeSocket;
    private readonly SemaphoreSlim _sendSemaphore;

    private EndPoint? _remoteEndPoint;

    /// <inheritdoc />
    public int Available => _socket.Available;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpSocketNetworkInterface"/> class.
    /// </summary>
    /// <param name="maxDataLength">The maximum length of data that can be sent.</param>
    /// <param name="connectOnReceive"><c>True</c> to connect the socket upon receiving remote data.</param>
    public UdpSocketNetworkInterface(int maxDataLength, bool connectOnReceive = false)
        : this(new Socket(SocketType.Dgram, ProtocolType.Udp), maxDataLength, connectOnReceive)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpSocketNetworkInterface"/> class.
    /// </summary>
    /// <param name="socket">The underlying socket to use.</param>
    /// <param name="maxDataLength">The maximum length of data that may be sent.</param>
    /// <param name="connectOnReceive"><c>True</c> to connect the socket upon receiving remote data.</param>
    /// <param name="disposeSocket">
    /// <c>True</c> to dispose of the <paramref name="socket"/> when <see cref="Dispose"/> is called.
    /// </param>
    public UdpSocketNetworkInterface
    (
        Socket socket,
        int maxDataLength,
        bool connectOnReceive = false,
        bool disposeSocket = true
    )
    {
        if (socket.SocketType is not SocketType.Dgram)
            throw new ArgumentException("Must be a datagram socket", nameof(socket));

        if (socket.ProtocolType is not ProtocolType.Udp)
            throw new ArgumentException("Socket must use the UDP protocol", nameof(socket));

        _socket = socket;
        _connectOnReceive = connectOnReceive;
        _disposeSocket = disposeSocket;
        _sendSemaphore = new SemaphoreSlim(1);

        _socket.SendBufferSize = maxDataLength;
        _socket.ReceiveBufferSize = maxDataLength;
    }

    /// <inheritdoc />
    public int Send(ReadOnlySpan<byte> data)
    {
        if (_remoteEndPoint is null)
            throw new InvalidOperationException("The remote endpoint has not been set. Either bind or connect the interface");

        _sendSemaphore.Wait();
        int sent = _socket.SendTo(data, _remoteEndPoint);

        _sendSemaphore.Release();
        return sent;
    }

    /// <inheritdoc />
    public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_remoteEndPoint is null)
            throw new InvalidOperationException("The remote endpoint has not been set. Either bind or connect the interface");

        await _sendSemaphore.WaitAsync(ct).ConfigureAwait(false);
        int sent = await _socket.SendToAsync
        (
            data,
            SocketFlags.None,
            _remoteEndPoint,
            ct
        ).ConfigureAwait(false);

        _sendSemaphore.Release();
        return sent;
    }

    /// <inheritdoc />
    public async ValueTask<int> ReceiveAsync(Memory<byte> receiveTo, CancellationToken ct = default)
    {
        if (_remoteEndPoint is null)
            throw new InvalidOperationException("The remote endpoint has not been set. Either bind or connect the interface");

        if (!_socket.IsBound)
            throw new InvalidOperationException("Must bind the interface before attempting to receive");

        SocketReceiveFromResult result =  await _socket.ReceiveFromAsync
        (
            receiveTo,
            SocketFlags.None,
            _remoteEndPoint,
            ct
        ).ConfigureAwait(false);

        if (_connectOnReceive && !_socket.Connected)
            Connect(result.RemoteEndPoint);

        return result.ReceivedBytes;
    }

    /// <inheritdoc />
    public void Bind(EndPoint localEndPoint)
    {
        _socket.Bind(localEndPoint);
        _remoteEndPoint = localEndPoint;
    }

    /// <inheritdoc />
    public void Connect(EndPoint remoteEndPoint)
    {
        _socket.Connect(remoteEndPoint);
        _remoteEndPoint = remoteEndPoint;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeSocket)
            _socket.Dispose();

        _sendSemaphore.Dispose();
    }
}
