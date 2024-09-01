const binary_primitives = @import("binary_primitives.zig");
const std = @import("std");

/// The byte sequence used to indicate that a reliable data packet is carrying multi-data.
pub const MULTI_DATA_INDICATOR = [2]u8{ 0x00, 0x19 };

/// Gets the 'true' reliable sequence for an incoming short packet sequence.
/// `packet_sequence`: The short sequence number on the reliable data packet.
/// `current_sequence`: The last calculated true reliable sequence.
/// `max_queued_reliable_data_packets`: The maximum number of reliable data packets
/// that may be queued for dispatch/receive at any one time.
pub fn getTrueIncomingSequence(
    packet_sequence: u16,
    current_sequence: i64,
    max_queued_reliable_data_packets: i16,
) i64 {
    // Note: this method makes the assumption that the amount of queued reliable data
    // can never be more than slightly less than the max value of a u16

    // Zero out the lower two bytes of our last known true sequence,
    // and insert the packet sequence into that space
    var sequence: i64 = packet_sequence | (current_sequence & (std.math.maxInt(i64) ^ std.math.maxInt(u16)));

    // If the sequence we obtain is larger than our possible window, we must have wrapped back
    // to the last 'block' (of short packet sequences), and hence need to decrement the true
    // sequence by an entire block
    if (sequence > current_sequence + max_queued_reliable_data_packets) {
        sequence -= std.math.maxInt(u16) + 1;
    }

    // Check vice-versa for having wrapped forward to the next block
    if (sequence < current_sequence - max_queued_reliable_data_packets) {
        sequence += std.math.maxInt(u16) + 1;
    }

    return sequence;
}

/// Checks whether a buffer starts with the `MULTI_DATA_INDICATOR`.
pub fn hasMultiData(buffer: []const u8) bool {
    return buffer.len > MULTI_DATA_INDICATOR.len and std.mem.startsWith(u8, buffer, &MULTI_DATA_INDICATOR);
}

/// Writes the `MULTI_DATA_INDICATOR` to a buffer.
pub fn writeMultiDataIndicator(buffer: []u8) void {
    buffer[0] = MULTI_DATA_INDICATOR[0];
    buffer[1] = MULTI_DATA_INDICATOR[1];
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

test getTrueIncomingSequence {
    try std.testing.expectEqual(3, getTrueIncomingSequence(3, 0, 10));

    // Check the boundary
    try std.testing.expectEqual(std.math.maxInt(u16), getTrueIncomingSequence(
        std.math.maxInt(u16),
        std.math.maxInt(u16) - 1,
        10,
    ));

    // Test wrap-forward. Packet seq of 2 here, despite expecting curr seq + 3, as once wrapping back around,
    // a packet seq of 0 is actually equal to max of last window + 1
    try std.testing.expectEqual(std.math.maxInt(u16) + 3, getTrueIncomingSequence(
        2,
        std.math.maxInt(u16),
        10,
    ));

    // Test wrap-backward
    try std.testing.expectEqual(std.math.maxInt(u16), getTrueIncomingSequence(
        std.math.maxInt(u16),
        std.math.maxInt(u16) + 1,
        10,
    ));
}

test hasMultiData {
    const data = MULTI_DATA_INDICATOR ++ [_]u8{ 0x00, 0x00 };
    try std.testing.expect(hasMultiData(&data));
}

test writeMultiDataIndicator {
    var data = std.mem.zeroes([4]u8);
    writeMultiDataIndicator(&data);
    try std.testing.expectEqualSlices(u8, MULTI_DATA_INDICATOR ++ &[_]u8{ 0x00, 0x00 }, &data);
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
