using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Util;

public static class MultiPacketUtils
{
    /// <summary>
    /// Reads a MultiData variable-length integer.
    /// </summary>
    /// <param name="data">The buffer to read the value from.</param>
    /// <param name="offset">
    /// The offset into the buffer at which the variable length value begins.
    /// Will be incremented by the amount of bytes consumed by the value.
    /// </param>
    /// <returns>The value.</returns>
    public static uint ReadVariableLength(ReadOnlySpan<byte> data, ref int offset)
    {
        uint value;

        if (data[offset] < byte.MaxValue)
        {
            value = data[offset++];
        }
        else if (data[offset] == byte.MaxValue && data[offset + 1] == 0)
        {
            // We only offset by one, as the implied 0x00 in front of all
            // core OP codes given big endian, allows us to use that
            // as an indicator for a length value of 0xFF
            value = data[offset++];
        }
        else if (data[offset + 1] == byte.MaxValue && data[offset + 2] == byte.MaxValue)
        {
            offset += 3;
            value = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            offset += sizeof(uint);
        }
        else
        {
            offset += 1;
            value = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            offset += sizeof(ushort);
        }

        return value;
    }

    /// <summary>
    /// Gets the amount of space in a buffer that a variable-length integer
    /// will consume.
    /// </summary>
    /// <param name="length">The length value.</param>
    /// <returns>The required buffer size.</returns>
    public static int GetVariableLengthSize(int length)
        => length switch
        {
            <= byte.MaxValue => sizeof(byte),
            < ushort.MaxValue => sizeof(ushort) + 1,
            _ => sizeof(uint) + 3
        };

    /// <summary>
    /// Writes a MultiPacket variable-length integer to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to write to.</param>
    /// <param name="length">The length value to write.</param>
    /// <param name="offset">
    /// The offset into the buffer at which to write the value.
    /// The offset will be incremented by the amount of bytes consumed by the value.
    /// </param>
    public static void WriteVariableLength(Span<byte> buffer, uint length, ref int offset)
    {
        if (length <= byte.MaxValue)
        {
            // We rely on core OP codes all starting with 0x00 (given big endian)
            // to signal that a length of 0xFF is not a ushort value.
            buffer[offset++] = (byte)length;
        }
        else if (length < ushort.MaxValue)
        {
            buffer[offset++] = byte.MaxValue;
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)length);
            offset += sizeof(ushort);
        }
        else
        {
            buffer[offset++] = byte.MaxValue;
            buffer[offset++] = byte.MaxValue;
            buffer[offset++] = byte.MaxValue;
            BinaryPrimitives.WriteUInt32BigEndian(buffer[offset..], length);
            offset += sizeof(uint);
        }
    }
}
