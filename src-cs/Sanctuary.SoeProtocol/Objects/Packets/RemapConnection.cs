using BinaryPrimitiveHelpers;
using System;

namespace Sanctuary.SoeProtocol.Objects.Packets;

/// <summary>
/// Represents a packet used to remap an existing session to a new port.
/// </summary>
/// <param name="SessionId">The ID of the session to remap.</param>
/// <param name="CrcSeed">The CRC seed being used in the session.</param>
public readonly record struct RemapConnection(uint SessionId, uint CrcSeed)
{
    /// <summary>
    /// Gets the buffer size required to serialize a
    /// <see cref="RemapConnection"/> packet.
    /// </summary>
    public const int Size = sizeof(SoeOpCode)
        + sizeof(uint) // SessionId
        + sizeof(uint); // CrcSeed

    /// <summary>
    /// Deserializes a <see cref="RemapConnection"/> packet from a buffer.
    /// This method does not expect the OP code in the buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="hasOpCode">Indicates whether the buffer contains an OP code.</param>
    /// <returns>The deserialized packet.</returns>
    public static RemapConnection Deserialize(ReadOnlySpan<byte> buffer, bool hasOpCode)
    {
        BinaryReader reader = new(buffer);

        if (hasOpCode)
            reader.Seek(sizeof(SoeOpCode));

        uint sessionId = reader.ReadUInt32BE();
        uint crcSeed = reader.ReadUInt32BE();

        return new RemapConnection
        (
            sessionId,
            crcSeed
        );
    }

    /// <summary>
    /// Serializes this <see cref="RemapConnection"/> packet to a buffer.
    /// This method writes the OP code to the buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public void Serialize(Span<byte> buffer)
    {
        BinaryWriter writer = new(buffer);

        writer.WriteUInt16BE((ushort)SoeOpCode.RemapConnection);
        writer.WriteUInt32BE(SessionId);
        writer.WriteUInt32BE(CrcSeed);
    }
}
