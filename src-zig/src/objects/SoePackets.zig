const BinaryPrimitives = @import("../utils/BinaryPrimitives.zig");

// Note that contextual packets do not write their OP code. This is instead handled by
// the dispatch pipeline, as their may be additional bytes between the OP and packet data.

/// Represents a packet used to acknowledge a reliable data sequence.
pub const Acknowledge = struct {
    /// The buffer size required to serialize an `Acknowledge` packet.
    pub const SIZE = @sizeOf(u16);

    /// The reliable data sequence.
    sequence: u16,

    pub fn deserialize(buffer: []const u8) Acknowledge {
        return Acknowledge{
            .sequence = BinaryPrimitives.readU16BE(buffer),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8) void {
        BinaryPrimitives.writeU16BE(buffer, self.sequence);
    }
};

/// Represents a packet used to acknowledge all outstanding data sequences,
/// up to the given sequence.
pub const AcknowledgeAll = struct {
    /// The buffer size required to serialize an `AcknowledgeAll` packet.
    pub const SIZE = @sizeOf(u16);

    /// The reliable data sequence.
    sequence: u16,

    pub fn deserialize(buffer: []const u8) AcknowledgeAll {
        return AcknowledgeAll{
            .sequence = BinaryPrimitives.readU16BE(buffer),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8) void {
        BinaryPrimitives.writeU16BE(buffer, self.sequence);
    }
};

/// Enumerates the possible session termination codes.
pub const DisconnectReason = enum(u16) {
    /// No reason can be given for the disconnect.
    None = 0,
    /// An ICMP error occured, forcing the disconnect.
    IcmpError = 1,
    /// The other party has let the session become inactive.
    Timeout = 2,
    /// An internal use code, used to indicate that the other party has sent a disconnect.
    OtherSideTerminated = 3,
    /// Indicates that the session manager has been disposed of.
    /// Generally occurs when the server/client is shutting down.
    ManagerDeleted = 4,
    /// An internal use code, indicating a session request attempt has failed.
    ConnectFail = 5,
    /// The application is terminating the session.
    Application = 6,
    /// An internal use code, indicating that the session must disconnect
    /// as the other party is unreachable.
    UnreachableConnection = 7,
    /// Indicates that the session has been closed because a data sequence
    /// was not acknowledged quickly enough.
    UnacknowledgedTimeout = 8,
    /// Indicates that a session request has failed (often due to the connecting
    /// party attempting a reconnection too quickly), and a new attempt should be
    /// made after a short delay.
    NewConnectionAttempt = 9,
    /// Indicates that the application did not accept a session request.
    ConnectionRefused = 10,
    /// Indicates that the proper session negotiation flow has not been observed.
    ConnectError = 11,
    /// Indicates that a session request has probably been looped back to the sender,
    /// and it should not continue with the connection attempt.
    ConnectingToSelf = 12,
    /// Indicates that reliable data is being sent too fast to be processed.
    ReliableOverflow = 13,
    /// Indicates that the session manager has been orphaned by the application.
    ApplicationReleased = 14,
    /// Indicates that a corrupt packet was received.
    CorruptPacket = 15,
    /// Indicates that the requested SOE protocol version or application protocol is invalid.
    ProtocolMismatch = 16,
};

/// Represents a packet used to terminate a session.
pub const Disconnect = struct {
    /// The buffer size required to serialize a `Disconnect` packet.
    pub const SIZE = @sizeOf(u32) + @sizeOf(DisconnectReason);

    /// The ID of the session that is being terminated.
    session_id: u32,
    /// The reason for the termination.
    reason: DisconnectReason,

    pub fn deserialize(buffer: []const u8) Disconnect {
        return Disconnect{
            .session_id = BinaryPrimitives.readU32BE(buffer),
            .reason = @enumFromInt(BinaryPrimitives.readU16BE(buffer[4..])),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8) void {
        BinaryPrimitives.writeU32BE(buffer, self.session_id);
        BinaryPrimitives.writeU16BE(buffer, @intFromEnum(self.reason));
    }
};
