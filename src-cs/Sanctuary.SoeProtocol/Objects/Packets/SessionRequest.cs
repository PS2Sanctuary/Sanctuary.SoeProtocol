using BinaryPrimitiveHelpers;
using System;

namespace Sanctuary.SoeProtocol.Objects.Packets;

/// <summary>
/// Represents a packet used to request a session.
/// </summary>
/// <param name="SoeProtocolVersion">The version of the SOE protocol in use.</param>
/// <param name="SessionId">A randomly generated session identifier.</param>
/// <param name="UdpLength">The maximum length of a UDP packet that the sender can receive.</param>
/// <param name="ApplicationProtocol">The application protocol that the sender wishes to transport.</param>
public readonly record struct SessionRequest
(
    uint SoeProtocolVersion,
    uint SessionId,
    uint UdpLength,
    string ApplicationProtocol
)
{
    /// <summary>
    /// Gets the minimum size of a buffer required to serialize
    /// a <see cref="SessionRequest"/> packet.
    /// </summary>
    public const int MinSize = sizeof(SoeOpCode)
        + sizeof(uint) // SoeProtocolVersion
        + sizeof(uint) // SessionId
        + sizeof(uint) // UdpLength
        + 1; // ApplicationProtocol terminator

    /// <summary>
    /// Deserializes a <see cref="SessionRequest"/> packet from a buffer.
    /// This method does not expect an OP code in the buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="hasOpCode">Indicates whether the buffer contains an OP code.</param>
    /// <returns>The deserialized packet.</returns>
    public static SessionRequest Deserialize(ReadOnlySpan<byte> buffer, bool hasOpCode)
    {
        BinaryPrimitiveReader reader = new(buffer);

        if (hasOpCode)
            reader.Seek(sizeof(SoeOpCode));

        uint soeProtocolVersion = reader.ReadUInt32BE();
        uint sessionId = reader.ReadUInt32BE();
        uint udpLength = reader.ReadUInt32BE();
        string applicationProtocol = reader.ReadStringNullTerminated();

        return new SessionRequest(soeProtocolVersion, sessionId, udpLength, applicationProtocol);
    }

    /// <summary>
    /// Gets the buffer size required to serialize a
    /// <see cref="SessionRequest"/> packet.
    /// </summary>
    public int GetSize()
        => sizeof(SoeOpCode)
            + sizeof(uint) // SoeProtocolVersion
            + sizeof(uint) // SessionId
            + sizeof(uint) // UdpLength
            + ApplicationProtocol.Length + 1; // + 1 for null termination

    /// <summary>
    /// Serializes this <see cref="SessionRequest"/> packet to a buffer.
    /// This method writes the OP code to the buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public void Serialize(Span<byte> buffer)
    {
        BinaryPrimitiveWriter writer = new(buffer);

        writer.WriteUInt16BE((ushort)SoeOpCode.SessionRequest);
        writer.WriteUInt32BE(SoeProtocolVersion);
        writer.WriteUInt32BE(SessionId);
        writer.WriteUInt32BE(UdpLength);
        writer.WriteStringNullTerminated(ApplicationProtocol);
    }
}
