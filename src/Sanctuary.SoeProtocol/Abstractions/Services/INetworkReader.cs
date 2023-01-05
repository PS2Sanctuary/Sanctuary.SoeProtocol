using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol.Abstractions.Services;

/// <summary>
/// Represents a network IO abstraction for
/// reading data from a remote endpoint.
/// </summary>
public interface INetworkReader
{
    /// <summary>
    /// Gets the amount of data that has been received from the
    /// network interface and is available to be read.
    /// </summary>
    int Available { get; }

    /// <summary>
    /// Receives data from the network stream.
    /// </summary>
    /// <param name="receiveTo">The buffer to receive the data to.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    /// <returns>The number of bytes read from the network stream.</returns>
    ValueTask<int> ReceiveAsync(Memory<byte> receiveTo, CancellationToken ct = default);

    /// <summary>
    /// Binds this <see cref="INetworkReader"/> to a local endpoint.
    /// </summary>
    /// <param name="localEndPoint">The endpoint to bind to.</param>
    void Bind(EndPoint localEndPoint);
}
