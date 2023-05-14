using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// Contains utility methods for working with reliable multi-data.
/// </summary>
public static class DataUtils
{
    /// <summary>
    /// Gets the byte sequence that indicates a reliable data packet is carrying multi-data.
    /// </summary>
    public static readonly ReadOnlyMemory<byte> MULTI_DATA_INDICATOR = new byte[] { 0x00, 0x19 };

    /// <summary>
    /// Gets the true sequence from an incoming packet sequence.
    /// </summary>
    /// <param name="packetSequence">The packet sequence.</param>
    /// <param name="currentSequence">The last known (general, expected window) sequence.</param>
    /// <param name="maxQueuedReliableDataPackets">
    /// The maximum number of reliable data packets that may be queued for dispatch/receive.
    /// </param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetTrueIncomingSequence
    (
        ushort packetSequence,
        long currentSequence,
        short maxQueuedReliableDataPackets
    )
    {
        // Note; this method makes the assumption that the amount of queued reliable data
        // can never be more than slightly less than the max value of a ushort

        // Zero-out the lower two bytes of our last known sequence and
        // and insert the packet sequence in that space
        long sequence = packetSequence | (currentSequence & (long.MaxValue ^ ushort.MaxValue));

        // If the sequence we obtain is larger than our possible window, we must have wrapped back
        // to the last 'packet sequence' 'block' (ushort), and hence need to decrement the true
        // sequence by an entire block
        if (sequence > currentSequence + maxQueuedReliableDataPackets)
            sequence -= ushort.MaxValue + 1;

        // If the sequence we obtain is smaller than our possible window, we must have wrapped
        // forward to the next 'packet sequence' block, and hence need to increment the true
        // sequence by an entire block
        if (sequence < currentSequence - maxQueuedReliableDataPackets)
            sequence += ushort.MaxValue + 1;

        return sequence;
    }

    /// <summary>
    /// Checks whether a buffer starts with the <see cref="MULTI_DATA_INDICATOR"/>.
    /// </summary>
    /// <param name="buffer">The buffer to check.</param>
    /// <returns><c>True</c> if the buffer contains multi-data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CheckForMultiData(ReadOnlySpan<byte> buffer)
        => buffer.Length > 2
            && buffer[0] == MULTI_DATA_INDICATOR.Span[0]
            && buffer[1] == MULTI_DATA_INDICATOR.Span[1];

    /// <summary>
    /// Writes the <see cref="MULTI_DATA_INDICATOR"/> to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">The offset into the buffer at which to write the multi-data indicator.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    /// Gets the amount of space in a buffer that a variable-length value will consume.
    /// </summary>
    /// <param name="length">The length value.</param>
    /// <returns>The required buffer size.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetVariableLengthSize(int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must not be negative");

        return GetVariableLengthSize((uint)length);
    }

    /// <summary>
    /// Gets the amount of space in a buffer that a variable-length integer will consume.
    /// </summary>
    /// <param name="length">The length value.</param>
    /// <returns>The required buffer size.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetVariableLengthSize(uint length)
        => length switch
        {
            < byte.MaxValue => sizeof(byte),
            < ushort.MaxValue => sizeof(ushort) + 1,
            _ => sizeof(uint) + 3
        };

    /// <summary>
    /// Writes a variable-length value to a buffer.
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
