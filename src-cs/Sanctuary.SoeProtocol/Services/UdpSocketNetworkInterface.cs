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
    private readonly bool _disposeSocket;

    private bool _connectOnReceive;
    private SocketAddress? _remoteEndPoint;

    /// <inheritdoc />
    public int Available => _socket.Available;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpSocketNetworkInterface"/> class.
    /// </summary>
    /// <param name="maxDataLength">The maximum length of data that can be sent.</param>
    /// <param name="connectOnReceive"><c>True</c> to connect the socket upon receiving remote data.</param>
    public UdpSocketNetworkInterface(int maxDataLength, bool connectOnReceive = false)
        : this
        (
            new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP),
            maxDataLength,
            connectOnReceive
        )
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
        if (socket.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            throw new ArgumentException("Must be an inter-network socket");

        if (socket.SocketType is not SocketType.Dgram)
            throw new ArgumentException("Must be a datagram socket", nameof(socket));

        if (socket.ProtocolType is not (ProtocolType.IP or ProtocolType.Udp))
            throw new ArgumentException("Socket must use the IP or UDP protocol", nameof(socket));

        _socket = socket;
        _connectOnReceive = connectOnReceive;
        _disposeSocket = disposeSocket;

        _socket.SendBufferSize = maxDataLength;
        _socket.ReceiveBufferSize = maxDataLength;
        //_socket.Blocking = false;
    }

    /// <inheritdoc />
    public int Send(ReadOnlySpan<byte> data)
    {
        if (_remoteEndPoint is null)
            throw new InvalidOperationException("The remote endpoint has not been set. Either bind or connect the interface");

        int sent = _socket.SendTo(data, SocketFlags.None, _remoteEndPoint);

        return sent;
    }

    /// <inheritdoc />
    public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_remoteEndPoint is null)
            throw new InvalidOperationException("The remote endpoint has not been set. Either bind or connect the interface");

        int sent = await _socket.SendToAsync
        (
            data,
            SocketFlags.None,
            _remoteEndPoint,
            ct
        ).ConfigureAwait(false);

        return sent;
    }

    /// <inheritdoc />
    public async ValueTask<int> ReceiveAsync(Memory<byte> receiveTo, CancellationToken ct = default)
    {
        if (_remoteEndPoint is null)
            throw new InvalidOperationException("The remote endpoint has not been set. Either bind or connect the interface");

        if (!_socket.IsBound)
            throw new InvalidOperationException("Must bind the interface before attempting to receive");

        int result = await _socket.ReceiveFromAsync
        (
            receiveTo,
            SocketFlags.None,
            _remoteEndPoint,
            ct
        ).ConfigureAwait(false);

        if (_connectOnReceive)
            Connect(_remoteEndPoint);

        return result;
    }

    /// <inheritdoc />
    public void Bind(EndPoint localEndPoint)
    {
        _socket.Bind(localEndPoint);
        _remoteEndPoint = new SocketAddress(AddressFamily.InterNetworkV6);
    }

    /// <inheritdoc />
    public void Connect(EndPoint remoteEndPoint)
    {
        _socket.Connect(remoteEndPoint);
        Connect(remoteEndPoint.Serialize());
    }

    private void Connect(SocketAddress remoteEndPoint)
    {
        _remoteEndPoint = remoteEndPoint;
        _connectOnReceive = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeSocket)
            _socket.Dispose();
    }
}
