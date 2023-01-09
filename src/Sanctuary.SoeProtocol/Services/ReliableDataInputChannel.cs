using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Services;

public sealed class ReliableDataInputChannel : IDisposable
{
    private readonly SoeProtocolHandler _handler;
    private readonly NativeSpanPool _spanPool;

    private readonly SlidingWindowArray<StashedData> _dataBacklog;
    private readonly byte[] _ackBuffer;

    private Rc4KeyState _cipherState;
    private ushort _windowStartSequence;

    public ReliableDataInputChannel
    (
        SoeProtocolHandler handler,
        NativeSpanPool spanPool,
        Rc4KeyState cipherState
    )
    {
        _handler = handler;
        _spanPool = spanPool;

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
        IncrementWindow();

        // TODO: This needs to factor in fragments. Need a dedicated method, akin to the DataSequencer from Sanctuary.Core
        while (_dataBacklog.Current.Sequence == _windowStartSequence && _dataBacklog.Current.IsFragment)
        {
            ProcessData(_dataBacklog.Current.Span.UsedSpan);
            _spanPool.Return(_dataBacklog.Current.Span);
            IncrementWindow();
        }

        SendAck((ushort)(_windowStartSequence - 1)); // TODO: Acks not necessarily on every data block
    }

    public void HandleReliableDataFragment(ReadOnlySpan<byte> data)
    {
        if (!CheckSequence(data, out ushort sequence))
            return;
    }

    private bool CheckSequence(ReadOnlySpan<byte> data, out ushort sequence)
    {
        sequence = BinaryPrimitives.ReadUInt16BigEndian(data);

        if (IsSequenceGreater(sequence))
            return true;

        SendAck((ushort)(_windowStartSequence - 1));
        return false;
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

        // TODO: Dispatch the data
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

    private readonly record struct StashedData(ushort Sequence, bool IsFragment, NativeSpan Span);

    /// <inheritdoc />
    public void Dispose()
    {
        _cipherState.Dispose();

        for (int i = 0; i < _dataBacklog.Length; i++)
            _spanPool.Return(_dataBacklog[i].Span);
    }
}
