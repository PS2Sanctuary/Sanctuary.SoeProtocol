using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Objects.Packets;

/// <summary>
/// Represents a packet used to keep a session alive, when no
/// data has been receiving by either party for some time.
/// </summary>
public readonly record struct Heartbeat
{
    /// <summary>
    /// Gets the buffer size required to serialize a
    /// <see cref="Heartbeat"/> packet.
    /// </summary>
    public const int Size = sizeof(SoeOpCode);

    /// <summary>
    /// Serializes this <see cref="Heartbeat"/> packet to a buffer.
    /// This method writes the OP code to the buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public static void Serialize(Span<byte> buffer)
        => BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)SoeOpCode.Heartbeat);
}
