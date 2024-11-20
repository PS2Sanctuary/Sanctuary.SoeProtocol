using Sanctuary.SoeProtocol.Abstractions.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol.Tests.Mocks;

/// <summary>
/// Represents a mocked <see cref="INetworkInterface"/>.
/// </summary>
public sealed class MockNetworkInterface : INetworkInterface, IDisposable
{
    /// <summary>
    /// Gets a channel to pass any data that may be waited on
    /// using <see cref="ReceiveAsync"/>.
    /// </summary>
    public Channel<byte[]> ReceiveChannel { get; }

    /// <summary>
    /// Gets a queue of any data passed to <see cref="Send"/>
    /// or <see cref="SendAsync"/>.
    /// </summary>
    public Queue<byte[]> SentData { get; }

    public int Available => SentData.Count > 0
        ? SentData.Peek().Length
        : 0;

    public MockNetworkInterface()
    {
        ReceiveChannel = Channel.CreateUnbounded<byte[]>();
        SentData = new Queue<byte[]>();
    }

    /// <inheritdoc />
    public void Bind(EndPoint localEndPoint) {}

    /// <inheritdoc />
    public void Connect(EndPoint remoteEndPoint) {}

    /// <inheritdoc />
    public async ValueTask<int> ReceiveAsync(Memory<byte> receiveTo, CancellationToken ct = default)
    {
        byte[] value = await ReceiveChannel.Reader.ReadAsync(ct);
        value.CopyTo(receiveTo);
        return value.Length;
    }

    /// <inheritdoc />
    public int Send(ReadOnlySpan<byte> data)
    {
        SentData.Enqueue(data.ToArray());
        return data.Length;
    }

    /// <inheritdoc />
    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        SentData.Enqueue(data.ToArray());
        return new ValueTask<int>(data.Length);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SentData.Clear();
        ReceiveChannel.Writer.Complete();
    }
}
