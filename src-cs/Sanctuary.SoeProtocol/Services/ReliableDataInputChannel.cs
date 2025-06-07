using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sanctuary.SoeProtocol.Services;

/// <summary>
/// Contains logic to handle reliable data packets and extract the proxied application data.
/// </summary>
public sealed class ReliableDataInputChannel : IDisposable
{
    /// <summary>
    /// A delegate that will be called when application data is received.
    /// </summary>
    public delegate void DataHandler(ReadOnlySpan<byte> data);

    private readonly SoeProtocolHandler _handler;
    private readonly SessionParameters _sessionParams;
    private readonly ApplicationParameters _applicationParams;
    private readonly NativeSpanPool _spanPool;
    private readonly DataHandler _dataHandler;
    private readonly StashedData[] _stash;

    private Rc4KeyState? _cipherState;
    /// The next reliable data sequence that we expect to receive.
    private long _windowStartSequence;
    private AcknowledgeAll? _bufferedAckAll;
    /// Stores fragments that compose the current piece of reliable data.
    private byte[]? _currentBuffer;
    /// The current length of the data that has been received into the <see cref="_currentBuffer"/>.
    private int _runningDataLength;
    /// The expected length of the data that should be received into the <see cref="_currentBuffer"/>.
    private int _expectedDataLength;
    /// The last reliable data sequence that we acknowledged.
    private long _lastAckAllSequence;
    private long _lastAckAllTime;

    /// <summary>
    /// Gets the input statistics.
    /// </summary>
    public DataInputStats InputStats { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReliableDataInputChannel"/>.
    /// </summary>
    /// <param name="handler">The protocol handler that owns this channel.</param>
    /// <param name="sessionParams">The session parameters.</param>
    /// <param name="appParams">The application parameters.</param>
    /// <param name="spanPool">A pool that may be used to stash out-of-order fragments.</param>
    /// <param name="dataHandler">The handler for processed data.</param>
    public ReliableDataInputChannel
    (
        SoeProtocolHandler handler,
        SessionParameters sessionParams,
        ApplicationParameters appParams,
        NativeSpanPool spanPool,
        DataHandler dataHandler
    )
    {
        _handler = handler;
        _sessionParams = sessionParams;
        _applicationParams = appParams;
        _spanPool = spanPool;
        _dataHandler = dataHandler;

        _cipherState = _applicationParams.EncryptionKeyState?.Copy();

        // Pre-fill the stash
        _stash = new StashedData[_sessionParams.MaxQueuedIncomingReliableDataPackets];
        for (int i = 0; i < _stash.Length; i++)
            _stash[i] = new StashedData();

        _lastAckAllSequence = -1;
        _lastAckAllTime = Stopwatch.GetTimestamp();

        InputStats = new DataInputStats();
    }

    /// <summary>
    /// Runs a tick of the <see cref="ReliableDataInputChannel"/> operations. This includes acknowledging processed
    /// data packets.
    /// </summary>
    public void RunTick()
    {
        if (_bufferedAckAll is not null)
        {
            SendAckAll(_bufferedAckAll.Value);
            _bufferedAckAll = null;
        }

        long toAck = _windowStartSequence - 1;

        // No need to perform an ack all if we're acking everything individually, or we've already acked up to the
        // current window start sequence
        if (_sessionParams.AcknowledgeAllData || toAck <= _lastAckAllSequence)
            return;

        // Ack if:
        // - at least MAX_ACK_DELAY_NS have passed since the last ack time and
        // - our seq to ack is greater than the last ack seq + half of the ack window
        bool needAck = Stopwatch.GetElapsedTime(_lastAckAllTime) > _sessionParams.MaximumAcknowledgeDelay
            || toAck >= _lastAckAllSequence + _sessionParams.DataAckWindow / 2;

        if (needAck)
            SendAckAll(new AcknowledgeAll((ushort)toAck));
    }

    /// <summary>
    /// Handles a <see cref="SoeOpCode.ReliableData"/> packet.
    /// </summary>
    /// <param name="data">The reliable data.</param>
    public void HandleReliableData(Span<byte> data)
    {
        if (!PreprocessData(ref data, false))
            return;

        ProcessData(data);
        // We've now processed another packet, so we can increment the window
        _windowStartSequence += 1;

        ConsumeStashedDataFragments();
    }

    /// <summary>
    /// Handles a <see cref="SoeOpCode.ReliableDataFragment"/> packet.
    /// </summary>
    /// <param name="data">The reliable data fragment.</param>
    public void HandleReliableDataFragment(Span<byte> data)
    {
        if (!PreprocessData(ref data, true))
            return;

        // At this point we know this fragment can be written directly to the buffer as it is next in the sequence.
        WriteImmediateFragmentToBuffer(data);
        // We've now processed another packet, so we can increment the window
        _windowStartSequence += 1;

        // Attempt to process the current buffer now, as the stashed fragments may belong to a new buffer
        // ConsumeStashedDataFragments will attempt to process the current buffer as it releases stashes
        TryProcessCurrentBuffer();
        ConsumeStashedDataFragments();
    }

    private void SendAckAll(AcknowledgeAll ackAll)
    {
        Span<byte> buffer = stackalloc byte[AcknowledgeAll.SIZE];
        ackAll.Serialize(buffer);
        _handler.SendContextualPacket(SoeOpCode.AcknowledgeAll, buffer);
        InputStats.AcknowledgeCount++;

        _lastAckAllSequence = ackAll.Sequence;
        _lastAckAllTime = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Pre-processes reliable data, and stashes it if required.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="isFragment">Indicates whether this data is a reliable fragment.</param>
    /// <returns><c>True</c> if the processed data should be used, otherwise <c>false</c>.</returns>
    private bool PreprocessData(ref Span<byte> data, bool isFragment)
    {
        InputStats.TotalReceived++;

        if (!IsValidReliableData(data, out long sequence, out ushort packetSequence))
            return false;

        bool ahead = sequence != _windowStartSequence;

        // Ack this data if we are in ack-all mode, or it is ahead of our expectations
        if (_sessionParams.AcknowledgeAllData || ahead)
        {
            Span<byte> buffer = stackalloc byte[Acknowledge.SIZE];
            new Acknowledge(packetSequence).Serialize(buffer);
            _handler.SendContextualPacket(SoeOpCode.Acknowledge, buffer);
        }

        // Remove the sequence bytes
        data = data[sizeof(ushort)..];

        // We can process this immediately.
        if (!ahead)
            return true;

        // We've received this data out-of-order, so stash it
        InputStats.OutOfOrderCount++;
        long stashSpot = sequence % _sessionParams.MaxQueuedIncomingReliableDataPackets;

        // Grab our stash item. We may have already stashed this packet ahead of time, so check for that
        StashedData stashItem = _stash[stashSpot];
        if (stashItem.Span is not null)
        {
            InputStats.DuplicateCount++;
            return false;
        }

        NativeSpan dataSpan = _spanPool.Rent();
        dataSpan.CopyDataInto(data);

        // Update our stash item
        stashItem.IsFragment = isFragment;
        stashItem.Span = dataSpan;
        _stash[stashSpot] = stashItem;
        return false;
    }

    /// <summary>
    /// Checks whether the given reliable data is valid for processing, by ensuring
    /// that it is within the current window, and we haven't already processed it.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="sequence">The true sequence.</param>
    /// <param name="packetSequence">The embedded packet sequence.</param>
    /// <returns>Whether we should process this data.</returns>
    private bool IsValidReliableData(ReadOnlySpan<byte> data, out long sequence, out ushort packetSequence)
    {
        packetSequence = BinaryPrimitives.ReadUInt16BigEndian(data);
        sequence = DataUtils.GetTrueIncomingSequence
        (
            packetSequence,
            _windowStartSequence,
            _sessionParams.MaxQueuedIncomingReliableDataPackets
        );

        // If this is too far ahead of our window, just drop it
        if (sequence > _windowStartSequence + _sessionParams.MaxQueuedIncomingReliableDataPackets)
            return false;

        // Great, we're inside the window
        if (sequence >= _windowStartSequence)
            return true;

        // We're receiving data we've already fully processed, so inform the remote about this.
        // However, because data is usually received in clumps, ensure we don't send acks too quickly
        if (Stopwatch.GetElapsedTime(_lastAckAllTime) < _sessionParams.MaximumAcknowledgeDelay) // TODO: Could this cause issues because we miss acking the most recent sequence?
            SendAckAll(new AcknowledgeAll((ushort)(_windowStartSequence - 1)));
        InputStats.DuplicateCount++;

        return false;
    }

    /// <summary>
    /// Writes a fragment to the <see cref="_currentBuffer"/>. If this is not allocated, the fragment in the
    /// <paramref name="data"/> will be assumed to be a master fragment (i.e. has the length of the full data packet)
    /// and a new <see cref="_currentBuffer"/> will be allocated to this len.
    /// </summary>
    /// <param name="data"></param>
    [MemberNotNull(nameof(_currentBuffer))]
    private void WriteImmediateFragmentToBuffer(ReadOnlySpan<byte> data)
    {
        if (_currentBuffer is not null)
        {
            data.CopyTo(_currentBuffer.AsSpan(_runningDataLength));
            _runningDataLength += data.Length;
        }
        else
        {
            // Otherwise, create a new buffer by assuming this is a master fragment and reading
            // the length
            _expectedDataLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
            _currentBuffer = ArrayPool<byte>.Shared.Rent(_expectedDataLength);
            data = data[sizeof(uint)..];
            data.CopyTo(_currentBuffer);
            _runningDataLength = data.Length;
        }
    }

    private void TryProcessCurrentBuffer()
    {
        if (_currentBuffer is null || _runningDataLength < _expectedDataLength)
            return;

        // Process the buffer, free it, and reset fields
        ProcessData(_currentBuffer);
        ArrayPool<byte>.Shared.Return(_currentBuffer);
        _currentBuffer = null;
        _runningDataLength = 0;
        _expectedDataLength = 0;
    }

    private void ConsumeStashedDataFragments()
    {
        // Grab the stash index of our current window start sequence
        long stashSpot = _windowStartSequence % _sessionParams.MaxQueuedIncomingReliableDataPackets;
        StashedData stashedItem = _stash[stashSpot];

        // Iterate through the stash until we reach an empty slot
        while (stashedItem.Span is { } pooledData)
        {
            if (stashedItem.IsFragment)
            {
                ProcessData(pooledData.UsedSpan);
            }
            else
            {
                WriteImmediateFragmentToBuffer(pooledData.UsedSpan);
                TryProcessCurrentBuffer();
            }

            // Release our stash reference
            _spanPool.Return(pooledData);
            stashedItem.Span = null;

            // Increment the window
            _windowStartSequence++;
            stashSpot = _windowStartSequence % _sessionParams.MaxQueuedIncomingReliableDataPackets;
            stashedItem = _stash[stashSpot];
        }
    }

    private void ProcessData(Span<byte> data)
    {
        if (DataUtils.CheckForMultiData(data))
        {
            int offset = 2;
            while (offset < data.Length)
            {
                int length = (int)DataUtils.ReadVariableLength(data, ref offset);
                DecryptAndCallDataHandler(data.Slice(offset, length));
                offset += length;
            }
        }
        else
        {
            DecryptAndCallDataHandler(data);
        }
    }

    private void DecryptAndCallDataHandler(Span<byte> data)
    {
        if (_applicationParams.IsEncryptionEnabled)
        {
            // A single 0x00 byte may be used to prefix encrypted data. We must ignore it
            if (data.Length > 1 && data[0] == 0)
                data = data[1..];

            // We can assume the key state is not null, as encryption cannot be enabled
            // by the application without setting a key state
            Rc4Cipher.Transform(data, data, ref _cipherState!);
        }

        InputStats.TotalReceivedBytes += data.Length;
        _dataHandler(data);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (StashedData element in _stash)
        {
            if (element.Span is { } active)
            {
                _spanPool.Return(active);
                element.Span = null;
            }
        }

        if (_currentBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_currentBuffer);
            _currentBuffer = null;
        }
    }

    [SkipLocalsInit]
    private class StashedData
    {
        public NativeSpan? Span;
        public bool IsFragment;
    }
}
