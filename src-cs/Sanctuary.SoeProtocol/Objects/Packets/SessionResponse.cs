using BinaryPrimitiveHelpers;
using System;

namespace Sanctuary.SoeProtocol.Objects.Packets;

/// <summary>
/// Represents a packet used to confirm a session request.
/// </summary>
/// <param name="SessionId">The ID of the session to confirm.</param>
/// <param name="CrcSeed">A randomly generated seed used to calculate the CRC-32 check value on relevant packets.</param>
/// <param name="CrcLength">The number of bytes that should be used to store the CRC-32 check value on relevant packets.</param>
/// <param name="IsCompressionEnabled">A value indicating whether relevant packets may be compressed.</param>
/// <param name="UnknownValue1">Unknown. Always observed to be <c>0</c>.</param>
/// <param name="UdpLength">The maximum length of a UDP packet that the sender can receive.</param>
/// <param name="SoeProtocolVersion">The version of the SOE protocol that is in use.</param>
public readonly record struct SessionResponse
(
    uint SessionId,
    uint CrcSeed,
    byte CrcLength,
    bool IsCompressionEnabled,
    byte UnknownValue1,
    uint UdpLength,
    uint SoeProtocolVersion
)
{
    /// <summary>
    /// Gets the buffer size required to serialize a
    /// <see cref="SessionResponse"/> packet.
    /// </summary>
    public const int Size = sizeof(SoeOpCode)
        + sizeof(uint) // SessionId
        + sizeof(uint) // CrcSeed
        + sizeof(byte) // CrcLength
        + sizeof(bool) // IsCompressionEnabled
        + sizeof(byte) // UnknownValue1
        + sizeof(uint) // UdpLength
        + sizeof(uint); // SoeProtocolVersion

    /// <summary>
    /// Deserializes a <see cref="SessionResponse"/> packet from a buffer.
    /// This method does not expect the OP code in the buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="hasOpCode">Indicates whether the buffer contains an OP code.</param>
    /// <returns>The deserialized packet.</returns>
    public static SessionResponse Deserialize(ReadOnlySpan<byte> buffer, bool hasOpCode)
    {
        BinaryReader reader = new(buffer);

        if (hasOpCode)
            reader.Seek(sizeof(SoeOpCode));

        uint sessionId = reader.ReadUInt32BE();
        uint crcSeed = reader.ReadUInt32BE();
        byte crcLength = reader.ReadByte();
        bool isCompressionEnabled = reader.ReadBool();
        byte unknownValue1 = reader.ReadByte();
        uint udpLength = reader.ReadUInt32BE();
        uint soeProtocolVersion = reader.ReadUInt32BE();

        return new SessionResponse
        (
            sessionId,
            crcSeed,
            crcLength,
            isCompressionEnabled,
            unknownValue1,
            udpLength,
            soeProtocolVersion
        );
    }

    /// <summary>
    /// Serializes this <see cref="SessionResponse"/> packet to a buffer.
    /// This method writes the OP code to the buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public void Serialize(Span<byte> buffer)
    {
        BinaryWriter writer = new(buffer);

        writer.WriteUInt16BE((ushort)SoeOpCode.SessionResponse);
        writer.WriteUInt32BE(SessionId);
        writer.WriteUInt32BE(CrcSeed);
        writer.WriteByte(CrcLength);
        writer.WriteBool(IsCompressionEnabled);
        writer.WriteByte(UnknownValue1);
        writer.WriteUInt32BE(UdpLength);
        writer.WriteUInt32BE(SoeProtocolVersion);
    }
}
