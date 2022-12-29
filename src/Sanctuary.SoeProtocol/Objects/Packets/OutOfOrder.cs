using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Objects.Packets;

/// <summary>
/// Represents a packet used to indicate that out-of-order
/// data sequences have been received.
/// </summary>
/// <param name="Sequence">The mis-ordered sequence number.</param>
public readonly record struct OutOfOrder(ushort Sequence)
{
    /// <summary>
    /// Gets the buffer size required to serialize an
    /// <see cref="OutOfOrder"/> packet.
    /// </summary>
    public const int Size = sizeof(ushort); // Sequence

    /// <summary>
    /// Deserializes an <see cref="OutOfOrder"/> packet from a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <returns>The deserialized packet.</returns>
    public static OutOfOrder Deserialize(ReadOnlySpan<byte> buffer)
    {
        ushort sequence = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        return new OutOfOrder(sequence);
    }

    /// <summary>
    /// Serializes this <see cref="OutOfOrder"/> packet to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public void Serialize(Span<byte> buffer)
        => BinaryPrimitives.WriteUInt16BigEndian(buffer, Sequence);
}
