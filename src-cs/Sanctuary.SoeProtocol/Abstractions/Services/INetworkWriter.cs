using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol.Abstractions.Services;

/// <summary>
/// Represents a network IO abstraction for
/// writing data to a remote endpoint.
/// </summary>
public interface INetworkWriter
{
    /// <summary>
    /// Sends the given bytes on the network stream.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <returns>The number of bytes written to the network stream.</returns>
    int Send(ReadOnlySpan<byte> data);

    /// <summary>
    /// Sends the given bytes on the network stream.
    /// </summary>
    /// <param name="data">The data to send</param>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    /// <returns>The number of bytes written to the network stream.</returns>
    ValueTask<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
}
