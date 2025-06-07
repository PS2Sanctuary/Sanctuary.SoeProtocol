using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Objects.Packets;

/// <summary>
/// Represents a packet used to acknowledge a data sequence.
/// </summary>
/// <param name="Sequence">The sequence number.</param>
public readonly record struct Acknowledge(ushort Sequence)
{
    /// <summary>
    /// Gets the buffer size required to serialize an
    /// <see cref="Acknowledge"/> packet.
    /// </summary>
    public const int SIZE = sizeof(ushort); // Sequence

    /// <summary>
    /// Deserializes an <see cref="Acknowledge"/> packet from a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <returns>The deserialized packet.</returns>
    public static Acknowledge Deserialize(ReadOnlySpan<byte> buffer)
    {
        ushort sequence = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        return new Acknowledge(sequence);
    }

    /// <summary>
    /// Serializes this <see cref="Acknowledge"/> packet to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public void Serialize(Span<byte> buffer)
        => BinaryPrimitives.WriteUInt16BigEndian(buffer, Sequence);
}
