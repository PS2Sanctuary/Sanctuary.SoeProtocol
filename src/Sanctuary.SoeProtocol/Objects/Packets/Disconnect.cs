using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Objects.Packets;

/// <summary>
/// Enumerates the possible session termination codes.
/// </summary>
public enum DisconnectReason : ushort
{
    /// <summary>
    /// No reason can be given for the disconnect.
    /// </summary>
    None = 0,

    /// <summary>
    /// An ICMP error occured, forcing the disconnect.
    /// </summary>
    IcmpError = 1,

    /// <summary>
    /// The other party has let the session become inactive.
    /// </summary>
    Timeout = 2,

    /// <summary>
    /// An internal use code, used to indicate that the other party
    /// has sent a disconnect.
    /// </summary>
    OtherSideTerminated = 3,

    /// <summary>
    /// Indicates that the session manager has been disposed of.
    /// Generally occurs when the server/client is shutting down.
    /// </summary>
    ManagerDeleted = 4,

    /// <summary>
    /// Indicates that a connection attempt has failed internally.
    /// </summary>
    ConnectFail = 5,

    /// <summary>
    /// The application is terminating the session.
    /// </summary>
    Application = 6,

    /// <summary>
    /// An internal use code, indicating that the session must disconnect
    /// as the other party is unreachable.
    /// </summary>
    UnreachableConnection = 7,

    /// <summary>
    /// Indicates that the session has been closed because a data sequence
    /// was not acknowledged quickly enough.
    /// </summary>
    UnacknowledgedTimeout = 8,

    /// <summary>
    /// Indicates that a session request has failed (often due to the connecting
    /// party attempting a reconnection too quickly), and a new attempt should be
    /// made after a short delay.
    /// </summary>
    NewConnectionAttempt = 9,

    /// <summary>
    /// Indicates that the application did not accept a session request.
    /// </summary>
    ConnectionRefused = 10,

    /// <summary>
    /// Indicates that the proper session negotiation flow has not been observed.
    /// </summary>
    ConnectError = 11,

    /// <summary>
    /// Indicates that a session request has probably been looped back to the sender,
    /// and it should not continue with the connection attempt.
    /// </summary>
    ConnectingToSelf = 12,

    /// <summary>
    /// Indicates that reliable data is being sent too fast to be processed.
    /// </summary>
    ReliableOverflow = 13,

    /// <summary>
    /// Indicates that the session manager has been orphaned by the application.
    /// </summary>
    ApplicationReleased = 14,

    /// <summary>
    /// Indicates that a corrupt packet was received.
    /// </summary>
    CorruptPacket = 15,

    /// <summary>
    /// Indicates that the requested SOE protocol version or
    /// application protocol is invalid.
    /// </summary>
    ProtocolMismatch = 16
}

/// <summary>
/// Represents a packet used to terminate a session.
/// </summary>
/// <param name="SessionId">The ID of the session that is being terminated.</param>
/// <param name="Reason">The reason for the termination.</param>
public readonly record struct Disconnect
(
    uint SessionId,
    DisconnectReason Reason
)
{
    /// <summary>
    /// Gets the buffer size required to serialize an
    /// <see cref="Disconnect"/> packet.
    /// </summary>
    public const int Size = sizeof(uint) // SessionId
        + sizeof(DisconnectReason); // Reason

    /// <summary>
    /// Deserializes an <see cref="Disconnect"/> packet from a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <returns>The deserialized packet.</returns>
    public static Disconnect Deserialize(ReadOnlySpan<byte> buffer)
    {
        uint sessionId = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        DisconnectReason reason = (DisconnectReason)BinaryPrimitives.ReadUInt16BigEndian(buffer[sizeof(uint)..]);

        return new Disconnect(sessionId, reason);
    }

    /// <summary>
    /// Serializes this <see cref="Disconnect"/> packet to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public void Serialize(Span<byte> buffer)
    {
        BinaryPrimitives.WriteUInt32BigEndian(buffer, SessionId);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[sizeof(uint)..], (ushort)Reason);
    }
}
