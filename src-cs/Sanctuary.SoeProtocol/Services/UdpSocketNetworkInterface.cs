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

    private bool _connectOnReceive;
    private SocketAddress? _remoteEndPoint;

    /// <inheritdoc />
    public int Available => _socket.Available;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpSocketNetworkInterface"/> class.
    /// </summary>
    /// <param name="maxDataLength">The maximum length of data that may be sent.</param>
    /// <param name="connectOnReceive"><c>True</c> to connect the socket upon receiving remote data.</param>
    public UdpSocketNetworkInterface
    (
        int maxDataLength,
        bool connectOnReceive = false
    )
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP);
        _connectOnReceive = connectOnReceive;

        _socket.SendBufferSize = maxDataLength;
        _socket.ReceiveBufferSize = maxDataLength;
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
        => _socket.Dispose();
}
