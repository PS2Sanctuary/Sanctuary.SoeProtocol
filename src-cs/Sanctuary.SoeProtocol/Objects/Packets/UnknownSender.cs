using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Objects.Packets;

/// <summary>
/// Represents a packet used to indicate that the receiving party
/// does not have a session associated with the sender's address.
/// </summary>
public readonly record struct UnknownSender
{
    /// <summary>
    /// Gets the buffer size required to serialize a
    /// <see cref="UnknownSender"/> packet.
    /// </summary>
    public const int Size = sizeof(SoeOpCode);

    /// <summary>
    /// Serializes this <see cref="UnknownSender"/> packet to a buffer.
    /// This method writes the OP code to the buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public static void Serialize(Span<byte> buffer)
        => BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)SoeOpCode.UnknownSender);
}
