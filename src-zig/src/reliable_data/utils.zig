const binary_primitives = @import("../utils/binary_primitives.zig");
const std = @import("std");
const zlib = @cImport({
    @cInclude("zlib.h");
});

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
/// Returns the length of the indicator in bytes.
pub fn writeMultiDataIndicator(buffer: []u8) usize {
    buffer[0] = MULTI_DATA_INDICATOR[0];
    buffer[1] = MULTI_DATA_INDICATOR[1];
    return MULTI_DATA_INDICATOR.len;
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
    const written = writeMultiDataIndicator(&data);
    try std.testing.expectEqualSlices(
        u8,
        MULTI_DATA_INDICATOR ++ &[_]u8{ 0x00, 0x00 },
        &data,
    );
    try std.testing.expectEqual(MULTI_DATA_INDICATOR.len, written);
}
