using BinaryPrimitiveHelpers;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sanctuary.SoeProtocol.Services;

/// <summary>
/// Contains logic to convert application data into reliable data packets.
/// </summary>
public sealed class ReliableDataOutputChannel : IDisposable
{
    /// <summary>
    /// Gets the maximum amount of time to wait for an acknowledgement
    /// </summary>
    public const int ACK_WAIT_MILLISECONDS = 500; // TODO: High ping could mess this up. Needs to be dynamic

    private readonly SoeProtocolHandler _handler;
    private readonly SessionParameters _sessionParams;
    private readonly ApplicationParameters _applicationParams;
    private readonly NativeSpanPool _spanPool;
    /// Holds packets that are currently within the dispatch window.
    private readonly StashedOutputPacket[] _dispatchStash;
    /// Holds backed-up data that can't fit into the _dispatchStash, including the true sequence of the data
    private readonly Queue<(long, StashedOutputPacket)> _dispatchQueue;
    private readonly SemaphoreSlim _packetOutputQueueLock;

    // Data-related
    private Rc4KeyState? _cipherState;
    private int _maxDataLength;

    /// The sequence number that the remote has most recently acknowledged.
    private long _windowStartSequence;
    /// The sequence number that we need to output from.
    private long _currentSequence;
    /// The total number of sequences that have been output.
    private long _totalSequence;

    /// The current multi-buffer
    private NativeSpan _multiBuffer;
    /// The current offset into the multi-buffer at which data should be written.
    private int _multiBufferOffset;
    /// The number of items that have been written to the current multi-buffer.
    private int _multiBufferItemCount;
    /// The offset into the multi-buffer at which the first item has been written.
    private int _multiBufferFirstItemOffset;

    /// <summary>
    /// Gets the data output statistics.
    /// </summary>
    public DataOutputStats OutputStats { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReliableDataOutputChannel"/> class.
    /// </summary>
    /// <param name="handler">The parent handler.</param>
    /// <param name="spanPool">The native span pool to use.</param>
    /// <param name="maxDataLength">The maximum length of data that may be sent by the output channel.</param>
    public ReliableDataOutputChannel
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

        _dispatchStash = new StashedOutputPacket[_sessionParams.MaxQueuedOutgoingReliableDataPackets];
        for (int i = 0; i < _dispatchStash.Length; i++)
            _dispatchStash[i] = new StashedOutputPacket();
        _dispatchQueue = new Queue<(long, StashedOutputPacket)>();

        _packetOutputQueueLock = new SemaphoreSlim(1);

        _cipherState = _applicationParams.EncryptionKeyState?.Copy();
        SetMaxDataLength(maxDataLength);
        SetupNewMultiBuffer();
    }

    /// <summary>
    /// Enqueues data to be sent on the reliable channel.
    /// </summary>
    /// <param name="data">The data.</param>
    public void EnqueueData(ReadOnlySpan<byte> data)
        => EnqueueDataInternal(data, false);

    /// <summary>
    /// Runs a tick of the output channel, which will send queued data.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    public void RunTick(CancellationToken ct)
    {
        int stashIndex;
        _packetOutputQueueLock.Wait(ct);

        // Attempt to load packets from the dispatch queue into the dispatch stash (the current window)
        // while there is unused space in the window
        while (_dispatchQueue.TryPeek(out (long Seq, StashedOutputPacket Data) bundle))
        {
            stashIndex = GetSequenceIndexInDispatchBuffer(bundle.Seq);
            if (_dispatchStash[stashIndex].DataSpan is not null)
                break;

            _dispatchStash[stashIndex] = bundle.Data;
            _dispatchQueue.Dequeue();
        }

        // Just in case we've received a late ack, after reverting to repeat
        if (_currentSequence < _windowStartSequence)
            _currentSequence = _windowStartSequence;

        // Pull anything from the multi-buffer
        EnqueueMultiBuffer();
        _packetOutputQueueLock.Release();

        // Check how long it's been since we went the first item in the window. If it hasn't been cleared after
        // our ack window has timed out, then it hasn't been acknowledged, and we need to re-send it
        long resendingUpTo = 0;
        stashIndex = GetSequenceIndexInDispatchBuffer(_windowStartSequence);
        long earliestSent = _dispatchStash[stashIndex].SentAt;
        if (earliestSent > -1 && Stopwatch.GetElapsedTime(earliestSent).Milliseconds >= ACK_WAIT_MILLISECONDS)
        {
            _currentSequence = _windowStartSequence;
            resendingUpTo = _currentSequence;
        }

        // Send everything we haven't sent from the current window
        Debug.Assert(_currentSequence <= _totalSequence);
        long lastSequenceToSend = Math.Min
        (
            _totalSequence,
            _currentSequence + _sessionParams.MaxQueuedOutgoingReliableDataPackets
        );

        while (_currentSequence < lastSequenceToSend)
        {
            ct.ThrowIfCancellationRequested();

            stashIndex = GetSequenceIndexInDispatchBuffer(_currentSequence);
            StashedOutputPacket stashedPacket = _dispatchStash[stashIndex];
            if (stashedPacket.DataSpan is null)
                continue; // Packets ahead of _currentSequence may have been individually acked & cleared

            SoeOpCode opCode = stashedPacket.IsFragment ? SoeOpCode.ReliableDataFragment : SoeOpCode.ReliableData;
            stashedPacket.SentAt = Stopwatch.GetTimestamp();
            _handler.SendContextualPacket(opCode, stashedPacket.DataSpan.UsedSpan);

            OutputStats.TotalSentReliablePackets++;
            if (_currentSequence < resendingUpTo)
                OutputStats.TotalResentReliablePackets++;

            _currentSequence++;
        }
    }

    /// <summary>
    /// Notifies the channel of an acknowledgement packet.
    /// </summary>
    /// <param name="ack">The acknowledgement.</param>
    public void NotifyOfAcknowledge(Acknowledge ack)
    {
        long seq = GetTrueIncomingSequence(ack.Sequence);
        OutputStats.IncomingAcknowledgeCount++;
        ProcessAck(seq);
    }

    /// <summary>
    /// Notifies the channel of an acknowledgement packet.
    /// </summary>
    /// <param name="ackAll">The acknowledgement.</param>
    public void NotifyOfAcknowledgeAll(AcknowledgeAll ackAll)
    {
        long seq = GetTrueIncomingSequence(ackAll.Sequence);
        OutputStats.IncomingAcknowledgeCount++;

        for (long i = _windowStartSequence; i <= seq; i++)
            ProcessAck(i);
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
    {
        if (_currentSequence > 0)
            throw new InvalidOperationException("The maximum length may not be changed after data has been enqueued");

        _maxDataLength = maxDataLength;
    }

    private void ProcessAck(long sequence)
    {
        int index = GetSequenceIndexInDispatchBuffer(sequence);

        StashedOutputPacket packet = _dispatchStash[index];
        if (packet.DataSpan is not null)
        {
            _spanPool.Return(packet.DataSpan);
            packet.DataSpan = null;
            OutputStats.ActualAcknowledgeCount++;
        }

        // Walk the window forward to our _currentSequence, until we find a packet that hasn't been acked
        while (_windowStartSequence < _currentSequence)
        {
            index = GetSequenceIndexInDispatchBuffer(_windowStartSequence);
            if (_dispatchStash[index].DataSpan is not null)
                break;

            _windowStartSequence++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetSequenceIndexInDispatchBuffer(long sequence)
        => (int)sequence % _sessionParams.MaxQueuedOutgoingReliableDataPackets;

    private void EnqueueDataInternal(ReadOnlySpan<byte> data, bool isRecursing)
    {
        byte[]? encryptedSpan = null;
        if (!isRecursing)
            _packetOutputQueueLock.Wait();

        if (!isRecursing && _applicationParams.IsEncryptionEnabled)
            encryptedSpan = Encrypt(data, out data);

        int multiLength = DataUtils.GetVariableLengthSize(data.Length) + data.Length;
        if (_maxDataLength - _multiBufferOffset >= multiLength) // We can fit in the current multi-buffer
        {
            DataUtils.WriteVariableLength(_multiBuffer.FullSpan, (uint)data.Length, ref _multiBufferOffset);
            data.CopyTo(_multiBuffer.FullSpan[_multiBufferOffset..]);

            if (_multiBufferFirstItemOffset is -1)
                _multiBufferFirstItemOffset = _multiBufferOffset;
            _multiBufferOffset += data.Length;
            _multiBufferItemCount++;
        }
        else
        {
            // We must enqueue the current multi-buffer, in order to maintain order
            EnqueueMultiBuffer();

            // Now that we've cleared the multi-buffer, can we fit?
            if (_maxDataLength - _multiBufferOffset >= multiLength)
            {
                EnqueueDataInternal(data, true);
            }
            else
            {
                StashFragment(ref data, true);
                while (data.Length > 0)
                    StashFragment(ref data, false);
            }
        }

        if (encryptedSpan is not null)
            ArrayPool<byte>.Shared.Return(encryptedSpan);

        if (!isRecursing)
            _packetOutputQueueLock.Release();
    }

    private void StashFragment(ref ReadOnlySpan<byte> data, bool isMaster)
    {
        NativeSpan span = _spanPool.Rent();
        BinaryWriter writer = new(span.FullSpan);

        writer.WriteUInt16BE((ushort)_totalSequence);
        int amountToTake = Math.Min(data.Length, _maxDataLength - sizeof(ushort));

        if (isMaster)
        {
            writer.WriteUInt32BE((uint)data.Length);
            amountToTake -= sizeof(uint);
        }

        writer.WriteBytes(data[..amountToTake]);
        span.UsedLength = writer.Offset;

        AddToDispatch(span, true);
        data = data[amountToTake..];
    }

    /// <summary>
    /// Queues the current multi-buffer as a full data packet.
    /// </summary>
    private void EnqueueMultiBuffer()
    {
        switch (_multiBufferItemCount)
        {
            case 0:
                return;
            case 1: // Just send a non-multi data packet in this case
                // Overwrite the multi-data indicator with the sequence
                _multiBuffer.StartOffset = _multiBufferFirstItemOffset - sizeof(ushort);
                _multiBuffer.UsedLength = _multiBufferOffset - _multiBuffer.StartOffset;
                BinaryPrimitives.WriteUInt16BigEndian
                (
                    _multiBuffer.UsedSpan,
                    (ushort)_totalSequence
                );
                break;
            default:
                // Write the current sequence to the head of the buffer
                BinaryPrimitives.WriteUInt16BigEndian
                (
                    _multiBuffer.FullSpan,
                    (ushort)_totalSequence
                );
                _multiBuffer.UsedLength = _multiBufferOffset;
                break;
        }

        AddToDispatch(_multiBuffer, false);
        SetupNewMultiBuffer();
    }

    private void AddToDispatch(NativeSpan buffer, bool isFragment)
    {
        int dispatchIndex = GetSequenceIndexInDispatchBuffer(_totalSequence);
        StashedOutputPacket packet = _dispatchStash[dispatchIndex];

        // Check if the stash contains backed-up data
        if (packet.DataSpan is not null)
        {
            packet = new StashedOutputPacket
            {
                IsFragment = isFragment,
                DataSpan = buffer
            };
            _dispatchQueue.Enqueue((_totalSequence, packet));
        }
        else
        {
            packet.IsFragment = isFragment;
            packet.DataSpan = buffer;
            packet.SentAt = -1;
        }

        _totalSequence++;
    }

    [MemberNotNull(nameof(_multiBuffer))]
    private void SetupNewMultiBuffer()
    {
        _multiBuffer = _spanPool.Rent();
        _multiBufferItemCount = 0;
        _multiBufferFirstItemOffset = -1;
        _multiBufferOffset = sizeof(ushort); // Space for sequence
        DataUtils.WriteMultiDataIndicator(_multiBuffer.FullSpan, ref _multiBufferOffset);
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
            _windowStartSequence,
            _sessionParams.MaxQueuedOutgoingReliableDataPackets
        );

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (StashedOutputPacket packet in _dispatchStash)
        {
            if (packet.DataSpan is null)
                continue;

            _spanPool.Return(packet.DataSpan);
            packet.DataSpan = null;
        }

        _spanPool.Return(_multiBuffer);
        _packetOutputQueueLock.Dispose();
    }

    private class StashedOutputPacket
    {
        public bool IsFragment { get; set; }
        public NativeSpan? DataSpan { get; set; }
        public long SentAt { get; set; } = -1;
    }
}
