using Sanctuary.SoeProtocol.Objects.Packets;
using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Services;

public class ReliableDataInputChannel
{
    private readonly SoeProtocolHandler _handler;
    private readonly byte[] _ackBuffer = GC.AllocateArray<byte>(Acknowledge.Size, true);

    private ushort _windowStartSequence;

    public ReliableDataInputChannel(SoeProtocolHandler handler)
    {
        _windowStartSequence = 0;
        _handler = handler;
    }

    public void HandleReliableData(ReadOnlySpan<byte> data)
    {
        if (!CheckSequence(data, out ushort sequence))
            return;
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

    private void SendAck(ushort sequence)
    {
        Acknowledge ack = new(sequence);
        ack.Serialize(_ackBuffer);
        _handler.SendContextualPacket(SoeOpCode.Acknowledge, _ackBuffer);
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
}
