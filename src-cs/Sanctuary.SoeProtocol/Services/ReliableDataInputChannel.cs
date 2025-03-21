﻿using Sanctuary.SoeProtocol.Abstractions;
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

    /// <summary>
    /// Gets the maximum length of time that data may go un-acknowledged.
    /// </summary>
    public static readonly TimeSpan MAX_ACK_DELAY = TimeSpan.FromMilliseconds(2);

    private readonly ISoeConnection _handler;
    private readonly SessionParameters _sessionParams;
    private readonly ApplicationParameters _applicationParams;
    private readonly NativeSpanPool _spanPool;
    private readonly DataHandler _dataHandler;

    private readonly SlidingWindowArray<StashedData> _dataBacklog;
    private readonly byte[] _ackAllBuffer;

    private Rc4KeyState? _cipherState;
    private long _windowStartSequence;

    // Fragment stitching variables
    private int _expectedDataLength;
    private int _runningDataLength;
    private byte[]? _currentBuffer;

    // Ack variables
    private long _lastAcknowledgedSequence;
    private long _lastAckAllAt;

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
        ISoeConnection handler,
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

        _dataBacklog = new SlidingWindowArray<StashedData>(_sessionParams.MaxQueuedIncomingReliableDataPackets);
        _ackAllBuffer = GC.AllocateArray<byte>(AcknowledgeAll.Size, true);

        _cipherState = _applicationParams.EncryptionKeyState?.Copy();
        _windowStartSequence = 0;

        _lastAcknowledgedSequence = -1;
        _lastAckAllAt = Stopwatch.GetTimestamp();

        for (int i = 0; i < _dataBacklog.Length; i++)
            _dataBacklog[i] = new StashedData();

        InputStats = new DataInputStats();
    }

    /// <summary>
    /// Runs a tick of the <see cref="ReliableDataInputChannel"/> operations.
    /// </summary>
    public void RunTick()
        => SendAckIfRequired();

    /// <summary>
    /// Handles a <see cref="SoeOpCode.ReliableData"/> packet.
    /// </summary>
    /// <param name="data">The reliable data.</param>
    public void HandleReliableData(Span<byte> data)
    {
        if (!PreprocessData(ref data, false))
            return;

        ProcessData(data);
        ConsumeStashedDataFragments();
        SendAckIfRequired();
    }

    /// <summary>
    /// Handles a <see cref="SoeOpCode.ReliableDataFragment"/> packet.
    /// </summary>
    /// <param name="data">The reliable data fragment.</param>
    public void HandleReliableDataFragment(Span<byte> data)
    {
        if (!PreprocessData(ref data, true))
            return;

        // At this point we know this fragment can be written directly to the buffer
        // as it is next in the sequence.
        WriteImmediateFragmentToBuffer(data);

        // Attempt to process the current buffer now, as the stashed fragments may belong to a new buffer
        // ConsumeStashedDataFragments will attempt to process the current buffer as it releases stashes
        TryProcessCurrentBuffer();
        ConsumeStashedDataFragments();
        SendAckIfRequired();
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
        {
            InputStats.DuplicateCount++;
            return false;
        }

        // Remove the sequence bytes
        data = data[sizeof(ushort)..];

        if (sequence == _windowStartSequence)
            return true;

        // We've received this data ahead of schedule, so ack it individually
        SendAck(packetSequence);
        InputStats.OutOfOrderCount++;

        StashedData? stash = TryGetStash(sequence);
        if (stash is null)
            return false;

        NativeSpan span = _spanPool.Rent();
        span.CopyDataInto(data);
        stash.Init(sequence, isFragment, span);

        return false;
    }

    // Helper method
    private StashedData? TryGetStash(long sequence)
    {
        StashedData stash = _dataBacklog[sequence - _windowStartSequence];

        // We may have already received this sequence. Confirm not active
        if (!stash.IsActive)
            return stash;

        // Sanity check. Theoretically we should never exceed the window
        if (stash.Sequence != sequence)
        {
            throw new InvalidOperationException
            (
                $"Invalid state: Attempting to replace active stash {stash.Sequence} with {sequence}"
            );
        }

        // We've already got actively stashed data with this sequence. No need to replace
        return null;
    }

    private void SendAckIfRequired()
    {
        long toAcknowledge = _windowStartSequence - 1;
        if (_lastAcknowledgedSequence >= toAcknowledge)
            return;

        // Send ack if:
        // we've not sent an ack for a little while AND we actually need to acknowledge a sequence
        // OR we've exceeded the ack window (we're receiving a lot of data)
        // OR we're acknowledging all data
        bool needAck = (Stopwatch.GetElapsedTime(_lastAckAllAt) > MAX_ACK_DELAY
                && toAcknowledge > _lastAcknowledgedSequence)
            || toAcknowledge >= _lastAcknowledgedSequence + _sessionParams.DataAckWindow / 2
            || _sessionParams.AcknowledgeAllData;
        if (!needAck)
            return;

        SendAckAll((ushort)toAcknowledge);
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
        sequence = GetTrueIncomingSequence(packetSequence);

        bool isValid = sequence >= _windowStartSequence
            && sequence < _windowStartSequence + _sessionParams.MaxQueuedIncomingReliableDataPackets;
        if (isValid)
            return true;

        // We're receiving data we've already fully processed, so inform the remote about this.
        // However, because data is usually received in clumps, ensure we don't send acks too quickly
        if (Stopwatch.GetElapsedTime(_lastAckAllAt) < MAX_ACK_DELAY)
            SendAckAll((ushort)(_windowStartSequence - 1));

        return false;
    }

    [MemberNotNull(nameof(_currentBuffer))]
    private void WriteImmediateFragmentToBuffer(ReadOnlySpan<byte> data)
    {
        if (_currentBuffer is null)
        {
            BeginNewBuffer(data);
        }
        else
        {
            data.CopyTo(_currentBuffer.AsSpan(_runningDataLength));
            _runningDataLength += data.Length;
        }
    }

    /// <summary>
    /// Sets a new storage buffer up. Expects a master fragment
    /// to be used as the <paramref name="data"/>
    /// </summary>
    /// <param name="data">The post-sequence data to create the new buffer with.</param>
    [MemberNotNull(nameof(_currentBuffer))]
    private void BeginNewBuffer(ReadOnlySpan<byte> data)
    {
        _expectedDataLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
        _currentBuffer = ArrayPool<byte>.Shared.Rent(_expectedDataLength);

        data = data[sizeof(uint)..];
        data.CopyTo(_currentBuffer);
        _runningDataLength = data.Length;
    }

    private void ConsumeStashedDataFragments(bool slideWindowFirst = true)
    {
        if (slideWindowFirst)
            IncrementWindow();

        // Copy over any stashed fragments that can be written
        // directly to the buffer; i.e. they equal the current
        // window start sequence and we have space left
        while (_dataBacklog.Current.IsActive && _dataBacklog.Current.Sequence <= _windowStartSequence)
        {
            StashedData curr = _dataBacklog.Current;

            // We should never reach a state where we have skipped past stashed fragments.
            // We perform this check (in tandem with the <= comparison above) for sanity.
            if (curr.Sequence < _windowStartSequence)
            {
                throw new Exception
                (
                    "Invalid state: stashed fragment did not match window start sequence " +
                    $"({curr.Sequence}/{_windowStartSequence})"
                );
            }

            if (!_dataBacklog.Current.IsFragment)
            {
                ProcessData(curr.Span.UsedSpan);
            }
            else
            {
                WriteImmediateFragmentToBuffer(curr.Span.UsedSpan);
                TryProcessCurrentBuffer();
            }

            curr.Clear(_spanPool);

            IncrementWindow();
        }
    }

    private void TryProcessCurrentBuffer()
    {
        if (_currentBuffer is null || _runningDataLength < _expectedDataLength)
            return;

        ProcessData(_currentBuffer.AsSpan(0, _runningDataLength));
        ArrayPool<byte>.Shared.Return(_currentBuffer);
        _currentBuffer = null;
        _runningDataLength = -1;
        _expectedDataLength = 0;
    }

    private void ProcessData(Span<byte> data)
    {
        if (DataUtils.CheckForMultiData(data))
        {
            int offset = 2;
            while (offset < data.Length)
            {
                int length = (int)DataUtils.ReadVariableLength(data, ref offset);

                Span<byte> dataSlice = data.Slice(offset, length);
                DecryptAndCallDataHandler(dataSlice);
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

    private void SendAckAll(ushort sequence)
    {
        AcknowledgeAll ack = new(sequence);
        ack.Serialize(_ackAllBuffer);
        _handler.SendContextualPacket(SoeOpCode.AcknowledgeAll, _ackAllBuffer);
        InputStats.AcknowledgeCount++;

        _lastAcknowledgedSequence = sequence;
        _lastAckAllAt = Stopwatch.GetTimestamp();
    }

    private void SendAck(ushort sequence)
    {
        Acknowledge ack = new(sequence);
        Span<byte> buffer = stackalloc byte[Acknowledge.Size];
        ack.Serialize(buffer);
        _handler.SendContextualPacket(SoeOpCode.Acknowledge, buffer);
        InputStats.AcknowledgeCount++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IncrementWindow()
    {
        _windowStartSequence++;
        _dataBacklog.Slide();
    }

    private long GetTrueIncomingSequence(ushort packetSequence)
        => DataUtils.GetTrueIncomingSequence
        (
            packetSequence,
            _windowStartSequence,
            _sessionParams.MaxQueuedIncomingReliableDataPackets
        );

    /// <inheritdoc />
    public void Dispose()
    {
        for (int i = 0; i < _dataBacklog.Length; i++)
        {
            if (_dataBacklog[i].IsActive)
                _dataBacklog[i].Clear(_spanPool);
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
        [MemberNotNullWhen(true, nameof(Span))]
        public bool IsActive { get; private set; }
        public NativeSpan? Span { get; private set; }
        public long Sequence { get; private set; }
        public bool IsFragment { get; private set; }

        public void Init(long sequence, bool isFragment, NativeSpan span)
        {
            if (IsActive)
                throw new InvalidOperationException("Already active");

            Sequence = sequence;
            IsFragment = isFragment;
            Span = span;
            IsActive = true;
        }

        public void Clear(NativeSpanPool spanPool)
        {
            if (!IsActive)
                throw new InvalidOperationException("Not active");

            IsActive = false;
            spanPool.Return(Span);
            Span = null;
        }
    }
}
