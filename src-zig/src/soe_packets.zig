const BinaryReader = @import("utils/BinaryReader.zig");
const BinaryWriter = @import("utils/BinaryWriter.zig");
const binary_primitives = @import("utils/binary_primitives.zig");
const SoeOpCode = @import("soe_protocol.zig").SoeOpCode;

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
            .sequence = binary_primitives.readU16BE(buffer),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8) void {
        binary_primitives.writeU16BE(buffer, self.sequence);
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
            .sequence = binary_primitives.readU16BE(buffer),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8) void {
        binary_primitives.writeU16BE(buffer, self.sequence);
    }
};

/// Enumerates the possible session termination codes.
pub const DisconnectReason = enum(u16) {
    /// No reason can be given for the disconnect.
    none = 0,
    /// An ICMP error occured, forcing the disconnect.
    icmp_error = 1,
    /// The other party has let the session become inactive.
    timeout = 2,
    /// An internal use code, used to indicate that the other party has sent a disconnect.
    other_side_terminated = 3,
    /// Indicates that the session manager has been disposed of.
    /// Generally occurs when the server/client is shutting down.
    manager_deleted = 4,
    /// An internal use code, indicating a session request attempt has failed.
    connect_fail = 5,
    /// The application is terminating the session.
    application = 6,
    /// An internal use code, indicating that the session must disconnect
    /// as the other party is unreachable.
    unreachable_connection = 7,
    /// Indicates that the session has been closed because a data sequence
    /// was not acknowledged quickly enough.
    unacknowledged_timeout = 8,
    /// Indicates that a session request has failed (often due to the connecting
    /// party attempting a reconnection too quickly), and a new attempt should be
    /// made after a short delay.
    new_connection_attempt = 9,
    /// Indicates that the application did not accept a session request.
    connection_refused = 10,
    /// Indicates that the proper session negotiation flow has not been observed.
    connect_error = 11,
    /// Indicates that a session request has probably been looped back to the sender,
    /// and it should not continue with the connection attempt.
    connecting_to_self = 12,
    /// Indicates that reliable data is being sent too fast to be processed.
    reliable_overflow = 13,
    /// Indicates that the session manager has been orphaned by the application.
    application_released = 14,
    /// Indicates that a corrupt packet was received.
    corrupt_packet = 15,
    /// Indicates that the requested SOE protocol version or application protocol is invalid.
    protocol_mismatch = 16,
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
            .session_id = binary_primitives.readU32BE(buffer),
            .reason = @enumFromInt(binary_primitives.readU16BE(buffer[4..])),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8) void {
        binary_primitives.writeU32BE(buffer, self.session_id);
        binary_primitives.writeU16BE(buffer[4..], @intFromEnum(self.reason));
    }
};

/// Represents a packet used to remap an existing session to a new port.
pub const RemapConnection = struct {
    /// The buffer size required to serialize a `RemapConnection` packet,
    /// including space for the OP code.
    pub const SIZE = @sizeOf(SoeOpCode) + @sizeOf(u32) * 2;

    /// The ID of the session that is being remapped.
    session_id: u32,
    // The CRC seed being used in the session.
    crc_seed: u32,

    pub fn deserialize(buffer: []const u8, has_op_code: bool) RemapConnection {
        var reader = BinaryReader.init(buffer);

        if (has_op_code) {
            reader.advance(@sizeOf(SoeOpCode));
        }

        return RemapConnection{
            .session_id = reader.readU32BE(),
            .crc_seed = reader.readU32BE(),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8, include_op_code: bool) void {
        var writer = BinaryWriter.init(buffer);

        if (include_op_code) {
            writer.writeU16BE(@intFromEnum(SoeOpCode.remap_connection));
        }

        writer.writeU32BE(self.session_id);
        writer.writeU32BE(self.crc_seed);
    }
};

/// Represents a packet used to request an SOE session.
pub const SessionRequest = struct {
    /// The minimum buffer size required to serialize a `SessionRequest` packet,
    /// including space for the OP code.
    pub const MIN_SIZE = @sizeOf(SoeOpCode) +
        @sizeOf(u32) + // soe_protocol_version
        @sizeOf(u32) + // session_id
        @sizeOf(u32) + // udp_length
        1; // application_protocol terminator

    /// The version of the SOE protocol that the packet sender is using.
    soe_protocol_version: u32,
    /// A randomly generated session identifier.
    session_id: u32,
    /// The maximum length of a UDP packet that the sender can receive.
    udp_length: u32,
    /// The application protocol that the sender wishes to transport.
    application_protocol: [:0]u8,

    /// Deserializes a new `SessionRequest` instance from the data in the `buffer`.
    /// Note that the created object is only valid for the lifetime of the `buffer`,
    /// as the `application_protocol` string is not allocated on the heap.
    pub fn deserialize(buffer: []const u8, has_op_code: bool) SessionRequest {
        var reader = BinaryReader.init(buffer);

        if (has_op_code) {
            reader.advance(@sizeOf(SoeOpCode));
        }

        return SessionRequest{
            .soe_protocol_version = reader.readU32BE(),
            .session_id = reader.readU32BE(),
            .udp_length = reader.readU32BE(),
            .application_protocol = reader.readStringNullTerminated(),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8, include_op_code: bool) void {
        var writer = BinaryWriter.init(buffer);

        if (include_op_code) {
            writer.writeU16BE(@intFromEnum(SoeOpCode.session_request));
        }

        writer.writeU32BE(self.soe_protocol_version);
        writer.writeU32BE(self.session_id);
        writer.writeU32BE(self.udp_length);
        writer.writeStringNullTerminated(self.application_protocol);
    }
};

/// Represents a packet used to request an SOE session.
pub const SessionResponse = struct {
    /// The buffer size required to serialize a `SessionResponse` packet,
    /// including space for the OP code.
    pub const SIZE = @sizeOf(SoeOpCode) +
        @sizeOf(u32) + // session_id
        @sizeOf(u32) + // crc_seed
        @sizeOf(u8) + // crc_length
        @sizeOf(u8) + // is_compression_enabled
        @sizeOf(u8) + // unknown_value_1
        @sizeOf(u32) + // udp_length
        @sizeOf(u32); // soe_protocol_version

    /// The ID of the session to confirm.
    session_id: u32,
    /// A randomly generated seed used to calculate the CRC-32 check value on relevant packets.
    crc_seed: u32,
    /// The number of bytes that should be used to store the CRC-32 check value on relevant packets.
    crc_length: u8,
    /// A value indicating whether relevant packets may be compressed.
    is_compression_enabled: bool,
    /// Unknown. Always observed to be <c>0</c>.
    unknown_value_1: u8,
    /// The maximum length of a UDP packet that the sender can receive.
    udp_length: u32,
    /// The version of the SOE protocol that the packet sender is using.
    soe_protocol_version: u32,

    pub fn deserialize(buffer: []const u8, has_op_code: bool) SessionResponse {
        var reader = BinaryReader.init(buffer);

        if (has_op_code) {
            reader.advance(@sizeOf(SoeOpCode));
        }

        return SessionResponse{
            .session_id = reader.readU32BE(),
            .crc_seed = reader.readU32BE(),
            .crc_length = reader.readU8(),
            .is_compression_enabled = reader.readBool(),
            .unknown_value_1 = reader.readU8(),
            .udp_length = reader.readU32BE(),
            .soe_protocol_version = reader.readU32BE(),
        };
    }

    pub fn serialize(self: @This(), buffer: []u8, include_op_code: bool) void {
        var writer = BinaryWriter.init(buffer);

        if (include_op_code) {
            writer.writeU16BE(@intFromEnum(SoeOpCode.session_response));
        }

        writer.writeU32BE(self.session_id);
        writer.writeU32BE(self.crc_seed);
        writer.writeU8(self.crc_length);
        writer.writeBool(self.is_compression_enabled);
        writer.writeU8(self.unknown_value_1);
        writer.writeU32BE(self.udp_length);
        writer.writeU32BE(self.soe_protocol_version);
    }
};

/// Represents a packet used to indicate that the receiving party does not have
/// a session associated with the sender's address.
pub const UnknownSender = struct {
    /// The buffer size required to serialize an `UnknownSender` packet,
    /// including space for the OP code.
    pub const SIZE = @sizeOf(SoeOpCode);

    pub fn serialize(buffer: []u8) void {
        binary_primitives.writeU16BE(buffer, @intFromEnum(SoeOpCode.unknown_sender));
    }
};
