using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sanctuary.SoeProtocol.Services;

/// <summary>
/// Contains logic to convert application data into reliable data packets.
/// A much-simplified version of the <see cref="ReliableDataOutputChannel"/>
/// that does not support multi-packets and is less performant, but also
/// isn't continuously spawning bugs and hard-to-debug logic.
/// </summary>
public sealed class ReliableDataOutputChannel2 : IDisposable
{
    /// <summary>
    /// Gets the maximum amount of time to wait for an acknowledgement
    /// </summary>
    public const int ACK_WAIT_MILLISECONDS = 500; // TODO: High ping could mess this up. Needs to be dynamic

    private readonly SoeProtocolHandler _handler;
    private readonly SessionParameters _sessionParams;
    private readonly ApplicationParameters _applicationParams;
    private readonly NativeSpanPool _spanPool;
    /// Holds backed-up data that can't fit into the _dispatchStash, including the true sequence of the data
    private readonly List<(long, StashedOutputPacket)> _dispatchQueue;
    private readonly SemaphoreSlim _packetOutputQueueLock;

    // Data-related
    private Rc4KeyState? _cipherState;
    private int _maxDataLength;

    /// The total number of sequences that have been output.
    private long _totalSequence;
    /// The maximum sequence number that the client knows about
    private long _maxClientSequence;

    private bool _waitingForAck;
    private long _lastAckAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReliableDataOutputChannel2"/> class.
    /// </summary>
    /// <param name="handler">The parent handler.</param>
    /// <param name="spanPool">The native span pool to use.</param>
    /// <param name="maxDataLength">The maximum length of data that may be sent by the output channel.</param>
    public ReliableDataOutputChannel2
    (
        SoeProtocolHandler handler,
        NativeSpanPool spanPool,
        int maxDataLength
    )
    {
        _handler = handler;
        _sessionParams = handler.SessionParams;
        _applicationParams = handler.ApplicationParams;
        _spanPool = spanPool;

        _dispatchQueue = new List<(long, StashedOutputPacket)>();

        _packetOutputQueueLock = new SemaphoreSlim(1);

        _cipherState = _applicationParams.EncryptionKeyState?.Copy();
        SetMaxDataLength(maxDataLength);

        _totalSequence = 0;
    }

    /// <summary>
    /// Enqueues data to be sent on the reliable channel.
    /// </summary>
    /// <param name="data">The data.</param>
    public void EnqueueData(ReadOnlySpan<byte> data)
    {
        if (data.Length is 0)
            return;

        byte[]? encryptedSpan = null;
        if (_applicationParams.IsEncryptionEnabled)
            encryptedSpan = Encrypt(data, out data);

        _packetOutputQueueLock.Wait();

        StashFragment(ref data, true, data.Length > _maxDataLength - sizeof(ushort));
        while (data.Length > 0)
            StashFragment(ref data, false, true);

        _packetOutputQueueLock.Release();

        if (encryptedSpan is not null)
            ArrayPool<byte>.Shared.Return(encryptedSpan);
    }

    /// <summary>
    /// Runs a tick of the output channel, which will send queued data.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    public void RunTick(CancellationToken ct)
    {
        if (Stopwatch.GetElapsedTime(_lastAckAt).TotalMilliseconds > ACK_WAIT_MILLISECONDS)
            _waitingForAck = false;

        if (_waitingForAck)
            return;

        for (int i = 0; i < _sessionParams.MaxQueuedOutgoingReliableDataPackets; i++)
        {
            if (i >= _dispatchQueue.Count)
                break;

            ct.ThrowIfCancellationRequested();

            StashedOutputPacket packet = _dispatchQueue[i].Item2;
            SoeOpCode opCode = packet.IsFragment ? SoeOpCode.ReliableDataFragment : SoeOpCode.ReliableData;
            _handler.SendContextualPacket(opCode, packet.DataSpan!.UsedSpan);
        }

        _waitingForAck = true;
    }

    /// <summary>
    /// Notifies the channel of an acknowledgement packet.
    /// </summary>
    /// <param name="ack">The acknowledgement.</param>
    public void NotifyOfAcknowledge(Acknowledge ack)
    {
        long seq = GetTrueIncomingSequence(ack.Sequence);
        _packetOutputQueueLock.Wait();

        int index = _dispatchQueue.FindIndex(x => x.Item1 == seq);
        _spanPool.Return(_dispatchQueue[index].Item2.DataSpan!);
        if (index != -1)
            _dispatchQueue.RemoveAt(index);

        _packetOutputQueueLock.Release();

        if (seq > _maxClientSequence)
            _maxClientSequence = seq;

        _lastAckAt = Stopwatch.GetTimestamp();
        _waitingForAck = false;
    }

    /// <summary>
    /// Notifies the channel of an acknowledgement packet.
    /// </summary>
    /// <param name="ackAll">The acknowledgement.</param>
    public void NotifyOfAcknowledgeAll(AcknowledgeAll ackAll)
    {
        long seq = GetTrueIncomingSequence(ackAll.Sequence);
        _packetOutputQueueLock.Wait();

        while (_dispatchQueue.Count > 0 && _dispatchQueue[0].Item1 <= seq)
        {
            _spanPool.Return(_dispatchQueue[0].Item2.DataSpan!);
            _dispatchQueue.RemoveAt(0);
        }

        _packetOutputQueueLock.Release();

        if (seq > _maxClientSequence)
            _maxClientSequence = seq;

        _lastAckAt = Stopwatch.GetTimestamp();
        _waitingForAck = false;
    }

    /// <summary>
    /// Sets the maximum length of data that may be output in a single packet.
    /// </summary>
    /// <remarks>
    /// This method should not be used after any data has been enqueued on the channel, to ensure that previously
    /// queued packets do not exceed the new limit.
    /// </remarks>
    /// <param name="maxDataLength">The maximum data length.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if this method is called after data has been enqueued.
    /// </exception>
    public void SetMaxDataLength(int maxDataLength)
        => _maxDataLength = maxDataLength;

    private void StashFragment(ref ReadOnlySpan<byte> data, bool isMaster, bool isFragment)
    {
        NativeSpan span = _spanPool.Rent();
        BinaryWriter writer = new(span.FullSpan);

        writer.WriteUInt16BE((ushort)_totalSequence);
        int amountToTake = Math.Min(data.Length, _maxDataLength - sizeof(ushort));

        if (isMaster && isFragment)
        {
            writer.WriteUInt32BE((uint)data.Length);
            amountToTake -= sizeof(uint);
        }

        writer.WriteBytes(data[..amountToTake]);
        span.UsedLength = writer.Offset;

        StashedOutputPacket packet = new()
        {
            IsFragment = isFragment,
            DataSpan = span
        };
        _dispatchQueue.Add((_totalSequence, packet));

        _totalSequence++;
        data = data[amountToTake..];
    }

    private byte[] Encrypt(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> output)
    {
        // Note the logic for ensuring that encrypted data which begins with a zero,
        // gets prefixed with a 0

        byte[] storage = ArrayPool<byte>.Shared.Rent(data.Length + 1);
        storage[0] = 0;

        // We can assume the key state is not null, as encryption cannot be enabled
        // by the application without setting a key state
        Rc4Cipher.Transform(data, storage.AsSpan(1), ref _cipherState!);
        output = storage[1] == 0
            ? storage
            : storage.AsSpan(1, data.Length);

        return storage;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetTrueIncomingSequence(ushort packetSequence)
        => DataUtils.GetTrueIncomingSequence
        (
            packetSequence,
            _maxClientSequence,
            _sessionParams.MaxQueuedOutgoingReliableDataPackets
        );

    /// <inheritdoc />
    public void Dispose()
    {
        foreach ((long, StashedOutputPacket) packet in _dispatchQueue)
        {
            if (packet.Item2.DataSpan is { } span)
                _spanPool.Return(span);
        }

        _dispatchQueue.Clear();
        _packetOutputQueueLock.Dispose();
    }

    private class StashedOutputPacket
    {
        public bool IsFragment { get; set; }
        public NativeSpan? DataSpan { get; set; }
    }
}
