using Sanctuary.SoeProtocol.Abstractions.Services;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol.Services;

/// <summary>
/// Provides an implementation of the <see cref="INetworkWriter"/> interface, which simply wraps a socket
/// and stores a remote address to which data will be sent.
/// </summary>
public class SocketNetworkWriter : INetworkWriter
{
    private readonly SocketAddress _remote;
    private readonly Socket _socket;

    /// <summary>
    /// Initializes a new instance of the <see cref="SocketNetworkWriter"/>.
    /// </summary>
    /// <param name="remote">The remote address to which data will be sent.</param>
    /// <param name="socket">The underlying socket to send data on.</param>
    public SocketNetworkWriter(SocketAddress remote, Socket socket)
    {
        _remote = remote;
        _socket = socket;
    }

    /// <inheritdoc />
    public int Send(ReadOnlySpan<byte> data)
        => _socket.SendTo(data, SocketFlags.None, _remote);

    /// <inheritdoc />
    public async ValueTask<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        => await _socket.SendToAsync(data, SocketFlags.None, _remote, ct);
}
