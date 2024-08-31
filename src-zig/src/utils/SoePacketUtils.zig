const BinaryPrimitives = @import("BinaryPrimitives.zig");
const BinaryWriter = @import("BinaryWriter.zig");
const Crc32 = @import("Crc32.zig");
const SoeOpCode = @import("../SoeProtocol.zig").SoeOpCode;
const std = @import("std");

/// Reads an `SoeOpCode` value from a buffer.
pub fn readSoeOpCode(buffer: []const u8) SoeOpCode {
    return @enumFromInt(BinaryPrimitives.readU16BE(buffer));
}

/// Determines whether the given OP code represents a packet that is used
/// outside the context of a session.
pub fn isContextlessPacket(op_code: SoeOpCode) bool {
    return switch (op_code) {
        .session_request, .session_response, .unknown_sender, .remap_connection => true,
        else => false,
    };
}

pub fn appendCrc(writer: *BinaryWriter, crc_seed: u32, crc_length: u8) void {
    if (crc_length == 0) {
        return;
    }

    const crc_value = Crc32.hash(writer.getConsumed(), crc_seed);
    switch (crc_length) {
        1 => writer.writeU8(@truncate(crc_value)),
        2 => writer.writeU16BE(@truncate(crc_value)),
        3 => writer.writeU24BE(@truncate(crc_value)),
        4 => writer.writeU32BE(crc_value),
        else => @panic("Invalid CRC length"),
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
        const expected_crc = Crc32.hash(buffer[0..4], crc_seed);
        var expected_buffer: [4]u8 = undefined;
        BinaryPrimitives.writeU32BE(&expected_buffer, expected_crc);

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
