using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Sanctuary.SoeProtocol.Services;

public sealed class ReliableDataInputChannel : IDisposable
{
    public delegate void DataHandler(ReadOnlySpan<byte> data);

    private readonly SoeProtocolHandler _handler;
    private readonly NativeSpanPool _spanPool;
    private readonly DataHandler _dataHandler;

    private readonly SlidingWindowArray<StashedData> _dataBacklog;
    private readonly byte[] _ackBuffer;

    private Rc4KeyState _cipherState;
    private ushort _windowStartSequence;

    // Fragment stitching variables
    private int _expectedDataLength;
    private int _runningDataLength;
    private byte[]? _currentBuffer;

    public ReliableDataInputChannel
    (
        SoeProtocolHandler handler,
        NativeSpanPool spanPool,
        Rc4KeyState cipherState,
        DataHandler dataHandler
    )
    {
        _handler = handler;
        _spanPool = spanPool;
        _dataHandler = dataHandler;

        _dataBacklog = new SlidingWindowArray<StashedData>(_handler.SessionParams.MaxQueuedRawPackets);
        _ackBuffer = GC.AllocateArray<byte>(Acknowledge.Size, true);

        _cipherState = cipherState;
        _windowStartSequence = 0;
    }

    public void HandleReliableData(Span<byte> data)
    {
        if (!CheckSequence(data, out ushort sequence))
            return;

        data = data[sizeof(ushort)..];

        if (sequence != _windowStartSequence)
        {
            NativeSpan span = _spanPool.Rent();
            span.CopyDataInto(data);
            _dataBacklog[sequence - _windowStartSequence] = new StashedData(sequence, false, span);

            return;
        }

        ProcessData(data);
        ConsumeStashedDataFragments();

        SendAck((ushort)(_windowStartSequence - 1)); // TODO: Acks not necessarily on every data block
    }

    public void HandleReliableDataFragment(ReadOnlySpan<byte> data)
    {
        if (!CheckSequence(data, out ushort sequence))
            return;

        data = data[sizeof(ushort)..];

        if (sequence != _windowStartSequence)
        {
            NativeSpan span = _spanPool.Rent();
            span.CopyDataInto(data);
            _dataBacklog[sequence - _windowStartSequence] = new StashedData(sequence, true, span);

            return;
        }

        // At this point we know this fragment can be written directly to the buffer
        // as it is next in the sequence.
        WriteImmediateFragmentToBuffer(data);
        TryProcessCurrentBuffer();
        ConsumeStashedDataFragments();

        SendAck((ushort)(_windowStartSequence - 1)); // TODO: Acks not necessarily on every data block
    }

    private bool CheckSequence(ReadOnlySpan<byte> data, out ushort sequence)
    {
        sequence = BinaryPrimitives.ReadUInt16BigEndian(data);

        if (IsSequenceGreater(sequence))
            return true;

        SendAck((ushort)(_windowStartSequence - 1));
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
        // directly to the buffer; i.e they equal the current
        // window start sequence and we have space left
        while (IsSequenceSmaller(_dataBacklog.Current.Sequence) || _dataBacklog.Current.Sequence == _windowStartSequence)
        {
            StashedData curr = _dataBacklog.Current;

            // We should never reach a state where we have skipped
            // past stashed fragments
            if (IsSequenceSmaller(curr.Sequence))
                throw new Exception("Invalid state: a stashed fragment was skipped");

            if (!_dataBacklog.Current.IsFragment)
            {
                ProcessData(curr.Span.UsedSpan);
            }
            else
            {
                WriteImmediateFragmentToBuffer(curr.Span.UsedSpan);
                TryProcessCurrentBuffer();
            }

            _spanPool.Return(curr.Span);
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
        if (MultiDataUtils.CheckForMultiData(data))
        {
            int offset = 2;
            while (offset < data.Length)
            {
                int length = (int)MultiDataUtils.ReadVariableLength(data, ref offset);

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
        if (_handler.SessionParams.IsEncryptionEnabled)
        {
            // A single 0x00 byte may be used to prefix encrypted data. We must ignore it
            if (data.Length > 1 && data[0] == 0)
                data = data[1..];

            Rc4Cipher.Transform(data, data, ref _cipherState);
        }

        _dataHandler(data);
    }

    private void SendAck(ushort sequence)
    {
        Acknowledge ack = new(sequence);
        ack.Serialize(_ackBuffer);
        _handler.SendContextualPacket(SoeOpCode.Acknowledge, _ackBuffer);
    }

    private void IncrementWindow()
    {
        _windowStartSequence++;
        _dataBacklog.Slide();
    }

    /// <summary>
    /// Determines if a wrap-around sequence number is greater
    /// than the current window start sequence.
    /// </summary>
    /// <param name="incomingSequence">The incoming sequence number.</param>
    /// <returns><c>True</c> if the incoming sequence is greater than the window start.</returns>
    private bool IsSequenceGreater(ushort incomingSequence)
        => incomingSequence > _windowStartSequence
            || _windowStartSequence - incomingSequence > 10000;

    /// <summary>
    /// Determines if a wrap-around sequence number is smaller
    /// than the current window start sequence.
    /// </summary>
    /// <param name="incomingSequence">The incoming sequence number.</param>
    /// <returns><c>True</c> if the incoming sequence is smaller than the window start.</returns>
    private bool IsSequenceSmaller(ushort incomingSequence)
        => incomingSequence < _windowStartSequence
            || incomingSequence - _windowStartSequence > 10000;

    /// <inheritdoc />
    public void Dispose()
    {
        _cipherState.Dispose();

        for (int i = 0; i < _dataBacklog.Length; i++)
            _spanPool.Return(_dataBacklog[i].Span);

        if (_currentBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_currentBuffer);
            _currentBuffer = null;
        }
    }

    private readonly record struct StashedData(ushort Sequence, bool IsFragment, NativeSpan Span);
}
