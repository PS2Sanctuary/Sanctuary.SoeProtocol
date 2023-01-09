using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// Contains utility methods for working with reliable multi-data.
/// </summary>
public static class MultiDataUtils
{
    public static readonly ReadOnlyMemory<byte> MULTI_DATA_INDICATOR = new byte[] { 0x00, 0x19 };

    /// <summary>
    /// Checks whether a buffer starts with the <see cref="MULTI_DATA_INDICATOR"/>.
    /// </summary>
    /// <param name="buffer">The buffer to check.</param>
    /// <returns><c>True</c> if the buffer contains multi-data.</returns>
    public static bool CheckForMultiData(ReadOnlySpan<byte> buffer)
        => buffer.Length > 2
            && buffer[0] == MULTI_DATA_INDICATOR.Span[0]
            && buffer[1] == MULTI_DATA_INDICATOR.Span[1];

    /// <summary>
    /// Writes the <see cref="MULTI_DATA_INDICATOR"/> to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">The offset into the buffer at which to write the multi-data indicator.</param>
    public static void WriteMultiDataIndicator(Span<byte> buffer, ref int offset)
    {
        buffer[offset] = MULTI_DATA_INDICATOR.Span[0];
        buffer[offset + 1] = MULTI_DATA_INDICATOR.Span[1];
        offset += 2;
    }

    /// <summary>
    /// Reads a variable length value from a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to read from.</param>
    /// <param name="offset">
    /// The offset into the buffer at which to read the length value.
    /// The offset will be incremented by the amount of bytes used by the length value.
    /// </param>
    /// <returns>The length value.</returns>
    public static uint ReadVariableLength(ReadOnlySpan<byte> buffer, ref int offset)
    {
        uint value;

        if (buffer[offset] < byte.MaxValue)
        {
            value = buffer[offset++];
        }
        else if (buffer[offset + 1] == byte.MaxValue && buffer[offset + 2] == byte.MaxValue)
        {
            offset += 3;
            value = BinaryPrimitives.ReadUInt32BigEndian(buffer[offset..]);
            offset += sizeof(uint);
        }
        else
        {
            offset += 1;
            value = BinaryPrimitives.ReadUInt16BigEndian(buffer[offset..]);
            offset += sizeof(ushort);
        }

        return value;
    }

    /// <summary>
    /// Gets the amount of space in a buffer that a
    /// variable-length integer will consume.
    /// </summary>
    /// <param name="length">The length value.</param>
    /// <returns>The required buffer size.</returns>
    public static int GetVariableLengthSize(int length)
        => length switch
        {
            < byte.MaxValue => sizeof(byte),
            < ushort.MaxValue => sizeof(ushort) + 1,
            _ => sizeof(uint) + 3
        };

    /// <summary>
    /// Writes a variable-length integer to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to write to.</param>
    /// <param name="length">The length value to write.</param>
    /// <param name="offset">
    /// The offset into the buffer at which to write the value.
    /// The offset will be incremented by the amount of bytes used by the length value.
    /// </param>
    public static void WriteVariableLength(Span<byte> buffer, uint length, ref int offset)
    {
        if (length < byte.MaxValue)
        {
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
