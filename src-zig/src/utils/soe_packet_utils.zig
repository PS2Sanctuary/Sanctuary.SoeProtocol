const binary_primitives = @import("binary_primitives.zig");
const BinaryWriter = @import("BinaryWriter.zig");
const crc32 = @import("crc32.zig");
const SessionParams = @import("../soe_protocol.zig").SessionParams;
const SoeOpCode = @import("../soe_protocol.zig").SoeOpCode;
const soe_packets = @import("../soe_packets.zig");
const std = @import("std");

/// Enumerates the possible errors when validating an SOE packet.
const SoePacketValidationError = error{
    /// The packet is too short, for its type
    TooShort,
    /// The packet failed CRC validation.
    CrcMismatch,
    /// The packet had an unknown OP code.
    InvalidOpCode,
};

/// Reads an `SoeOpCode` value from a buffer.
pub fn readSoeOpCode(buffer: []const u8) error{InvalidOpCode}!SoeOpCode {
    if (buffer.len < @sizeOf(SoeOpCode)) {
        return SoePacketValidationError.InvalidOpCode;
    }

    return std.meta.intToEnum(
        SoeOpCode,
        binary_primitives.readU16BE(buffer),
    ) catch {
        return SoePacketValidationError.InvalidOpCode;
    };
}

/// Determines whether the given OP code represents a packet that is used
/// outside the context of a session.
pub fn isContextlessPacket(op_code: SoeOpCode) bool {
    return switch (op_code) {
        .session_request, .session_response, .unknown_sender, .remap_connection => true,
        else => false,
    };
}

/// Appends a CRC check value to the given `writer`. The entirety of the `writer`'s
/// buffer is used to calculate the check value.
pub fn appendCrc(writer: *BinaryWriter, crc_seed: u32, crc_length: u8) void {
    if (crc_length == 0) {
        return;
    }

    const crc_value = crc32.hash(writer.getConsumed(), crc_seed);
    switch (crc_length) {
        1 => writer.writeU8(@truncate(crc_value)),
        2 => writer.writeU16BE(@truncate(crc_value)),
        3 => writer.writeU24BE(@truncate(crc_value)),
        4 => writer.writeU32BE(crc_value),
        else => @panic("Invalid CRC length"),
    }
}

/// Validates that a buffer 'most likely' contains an SOE protocol packet.
pub fn validatePacket(packet_data: []const u8, session_params: SessionParams) SoePacketValidationError!SoeOpCode {
    // Firstly, check that we can successfully read the OP code. This will check both the length,
    // and that the op code value is actually present in our set
    const op_code = try readSoeOpCode(packet_data);

    // Now check that the packet meets minimum length requirements
    const min_length = getPacketMinimumLength(
        op_code,
        session_params.is_compression_enabled,
        session_params.crc_length,
    );
    if (min_length > packet_data.len) {
        return SoePacketValidationError.TooShort;
    }

    // If the packet is contextless, or no CRC check is in place, there won't be a CRC check value!
    if (isContextlessPacket(op_code) or session_params.crc_length == 0) {
        return op_code;
    }

    const actual_crc = crc32.hash(
        packet_data[0 .. packet_data.len - session_params.crc_length],
        session_params.crc_seed,
    );

    const crc_match: bool = switch (session_params.crc_length) {
        1 => @as(u8, @truncate(actual_crc)) == packet_data[packet_data.len - 1],
        2 => @as(u16, @truncate(actual_crc)) == binary_primitives.readU16BE(packet_data[packet_data.len - 2 ..]),
        3 => @as(u24, @truncate(actual_crc)) == binary_primitives.readU24BE(packet_data[packet_data.len - 3 ..]),
        4 => actual_crc == binary_primitives.readU32BE(packet_data[packet_data.len - 4 ..]),
        else => @panic("Invalid CRC length. Must be between 0 and 4 inclusive"),
    };

    return if (crc_match) op_code else SoePacketValidationError.CrcMismatch;
}

/// Calculates the minimum length that a packet may be, given its OP code.
pub fn getPacketMinimumLength(op_code: SoeOpCode, is_compression_enabled: bool, crc_length: u8) usize {
    const contextual_padding = @sizeOf(SoeOpCode) + @as(u16, @intFromBool(is_compression_enabled)) + crc_length;

    return switch (op_code) {
        .session_request => soe_packets.SessionRequest.MIN_SIZE,
        .session_response => soe_packets.SessionResponse.SIZE,
        .multi_packet => contextual_padding + 2, // Min length of data-length bytes + first byte of data
        .disconnect => contextual_padding + soe_packets.Disconnect.SIZE,
        .heartbeat => contextual_padding,
        .reliable_data, .reliable_data_fragment => contextual_padding + @sizeOf(u16) + 1, // Sequence + first byte of data
        .acknowledge => contextual_padding + soe_packets.Acknowledge.SIZE,
        .acknowledge_all => contextual_padding + soe_packets.AcknowledgeAll.SIZE,
        .unknown_sender => soe_packets.UnknownSender.SIZE,
        .remap_connection => soe_packets.RemapConnection.SIZE,
    };
}

pub fn readVariableLength(source: []const u8, offset: *usize) u32 {
    var value: u32 = 0;

    if (source[offset.*] < 0xFF) {
        value = source[offset.*];
        offset.* += 1;
    } else if (source[offset.* + 1] == 0xFF and source[offset.* + 2] == 0xFF) {
        value = binary_primitives.readU32BE(source[offset.* + 3 ..]);
        offset.* += 7;
    } else {
        value = binary_primitives.readU16BE(source[offset.* + 1 ..]);
        offset.* += 3;
    }

    return value;
}

pub fn getVariableLengthSize(length: u32) comptime_int {
    if (length < 0xFF) {
        return 1;
    } else if (length < 0xFFFF) {
        return 3;
    } else {
        return 7;
    }
}

pub fn writeVariableLength(dest: []u8, value: u32, offset: *usize) void {
    if (value < 0xFF) {
        dest[offset.*] = @truncate(value);
        offset.* += 1;
    } else if (value < 0xFFFF) {
        dest[offset.*] = 0xFF;
        binary_primitives.writeU16BE(dest[offset.* + 1 ..], @truncate(value));
        offset.* += 3;
    } else {
        dest[offset.*] = 0xFF;
        dest[offset.* + 1] = 0xFF;
        dest[offset.* + 2] = 0xFF;
        binary_primitives.writeU32BE(dest[offset.* + 3 ..], value);
        offset.* += 7;
    }
}

test readSoeOpCode {
    const buffer = [_]u8{ 0x00, 0x01 };
    try std.testing.expectEqual(SoeOpCode.session_request, readSoeOpCode(&buffer));
}

test isContextlessPacket {
    try std.testing.expect(isContextlessPacket(SoeOpCode.session_response));
    try std.testing.expectEqual(false, isContextlessPacket(SoeOpCode.disconnect));
}

test appendCrc {
    const crc_seed = 5;

    for (0..5) |crc_length| {
        // Create a new buffer to store our data + crc value
        var buffer = try std.testing.allocator.alloc(u8, 4 + crc_length);
        defer std.testing.allocator.free(buffer);

        // Write a random initial value to the buffer
        var writer = BinaryWriter.init(buffer);
        writer.writeU32BE(454653524);

        // Hash the value we wrote and get a buffer of it to compare against
        const expected_crc = crc32.hash(buffer[0..4], crc_seed);
        var expected_buffer: [4]u8 = undefined;
        binary_primitives.writeU32BE(&expected_buffer, expected_crc);

        // Now run the method under test, to append a CRC value to the writer
        appendCrc(&writer, crc_seed, @truncate(crc_length));

        // Confirm that the value part of the buffer, plus the expected CRC value
        // (taken from the end as per big-endian) match
        const check_value = try std.mem.concat(
            std.testing.allocator,
            u8,
            &.{ buffer[0..4], expected_buffer[4 - crc_length ..] },
        );
        defer std.testing.allocator.free(check_value);

        try std.testing.expectEqualSlices(u8, check_value, buffer);
    }
}

test validatePacket {
    const session_params = SessionParams{};

    // Ensure we don't validate packets with too short of an OP code
    var packet: []const u8 = &[_]u8{@truncate(@intFromEnum(SoeOpCode.session_request))};
    try std.testing.expectError(
        SoePacketValidationError.InvalidOpCode,
        validatePacket(packet, session_params),
    );

    // Ensure we don't validate packets with an unknown OP code
    packet = &[_]u8{ 0x00, 0x00, 0x00, 0x04 };
    try std.testing.expectError(
        SoePacketValidationError.InvalidOpCode,
        validatePacket(packet, session_params),
    );
    try std.testing.expectError(
        SoePacketValidationError.InvalidOpCode,
        validatePacket(packet[2..], session_params),
    );
}

test "validatePacket_validatesContextualPacketForAllCrcLengths" {
    var session_params = SessionParams{};

    for (1..5) |crc_length| {
        // Update the crc_length of our session
        session_params.crc_length = @truncate(crc_length);
        // Set our initial crc seed
        session_params.crc_seed = 10;

        // Allocate space for a new acknowledgement packet
        const packet_data = try std.testing.allocator.alloc(
            u8,
            @sizeOf(SoeOpCode) + soe_packets.Acknowledge.SIZE + crc_length,
        );
        defer std.testing.allocator.free(packet_data);

        // Fill the ack packet with OP code, data, and CRC
        var writer = BinaryWriter.init(packet_data);
        writer.writeU16BE(@intFromEnum(SoeOpCode.acknowledge));

        const ack = soe_packets.Acknowledge{ .sequence = 10 };
        ack.serialize(writer.getRemaining());
        writer.advance(soe_packets.Acknowledge.SIZE);

        appendCrc(&writer, session_params.crc_seed, @truncate(crc_length));

        try std.testing.expectEqual(
            SoeOpCode.acknowledge,
            validatePacket(packet_data, session_params),
        );

        // Update the CRC seed, and test we now invalidate the packet
        session_params.crc_seed = 20;
        try std.testing.expectError(
            SoePacketValidationError.CrcMismatch,
            validatePacket(packet_data, session_params),
        );
    }
}

test readVariableLength {
    var offset: usize = 0;

    const one_byte_len = [_]u8{0xFE};
    try std.testing.expectEqual(0xFE, readVariableLength(&one_byte_len, &offset));
    try std.testing.expectEqual(1, offset);
    offset = 0;

    const two_byte_len = [_]u8{ 0xFF, 0x00, 0xFF };
    try std.testing.expectEqual(0xFF, readVariableLength(&two_byte_len, &offset));
    try std.testing.expectEqual(3, offset);
    offset = 0;

    const four_byte_len = [_]u8{ 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF };
    try std.testing.expectEqual(0xFFFF, readVariableLength(&four_byte_len, &offset));
    try std.testing.expectEqual(7, offset);
}

test writeVariableLength {
    var offset: usize = 0;
    var buffer = std.mem.zeroes([7]u8);

    writeVariableLength(&buffer, 0xFE, &offset);
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFE, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, &buffer);
    try std.testing.expectEqual(1, offset);
    offset = 0;

    writeVariableLength(&buffer, 0xFF, &offset);
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00 }, &buffer);
    try std.testing.expectEqual(3, offset);
    offset = 0;

    writeVariableLength(&buffer, 0xFFFF, &offset);
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF }, &buffer);
    try std.testing.expectEqual(7, offset);
}
