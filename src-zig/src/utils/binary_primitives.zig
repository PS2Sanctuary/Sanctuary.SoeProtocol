const std = @import("std");

/// Reads an unsigned 16-bit integer in big endian form.
pub fn readU16BE(source: []const u8) u16 {
    var value: u16 = 0;
    value |= @as(u16, source[0]) << 8;
    value |= source[1];
    return value;
}

/// Reads an unsigned 24-bit integer in big endian form.
pub fn readU24BE(source: []const u8) u24 {
    var value: u24 = 0;
    value |= @as(u24, source[0]) << 16;
    value |= @as(u16, source[1]) << 8;
    value |= source[2];
    return value;
}

/// Reads an unsigned 32-bit integer in big endian form.
pub fn readU32BE(source: []const u8) u32 {
    var value: u32 = 0;
    value |= @as(u32, source[0]) << 24;
    value |= @as(u32, source[1]) << 16;
    value |= @as(u16, source[2]) << 8;
    value |= source[3];
    return value;
}

/// Writes an unsigned 16-bit integer in big endian form.
pub fn writeU16BE(dest: []u8, value: u16) void {
    dest[0] = @truncate(value >> 8);
    dest[1] = @truncate(value);
}

/// Writes an unsigned 24-bit integer in big endian form.
pub fn writeU24BE(dest: []u8, value: u24) void {
    dest[0] = @truncate(value >> 16);
    dest[1] = @truncate(value >> 8);
    dest[2] = @truncate(value);
}

/// Writes an unsigned 32-bit integer in big endian form.
pub fn writeU32BE(dest: []u8, value: u32) void {
    dest[0] = @truncate(value >> 24);
    dest[1] = @truncate(value >> 16);
    dest[2] = @truncate(value >> 8);
    dest[3] = @truncate(value);
}

test readU16BE {
    const data = [_]u8{ 0xFF, 0xFF };
    try std.testing.expectEqual(std.math.maxInt(u16), readU16BE(&data));
}

test readU24BE {
    const data = [_]u8{ 0x01, 0x00, 0x63 };
    try std.testing.expectEqual(std.math.maxInt(u16) + 100, readU24BE(&data));
}

test readU32BE {
    const data = [_]u8{ 0xFF, 0xFF, 0xFF, 0xFF };
    try std.testing.expectEqual(std.math.maxInt(u32), readU32BE(&data));
}

test writeU16BE {
    var data: [2]u8 = undefined;
    writeU16BE(&data, std.math.maxInt(u16));
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFF, 0xFF }, &data);
}

test writeU24BE {
    var data: [3]u8 = undefined;
    writeU24BE(&data, std.math.maxInt(u24) - 1);
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFF, 0xFF, 0xFE }, &data);
}

test writeU32BE {
    var data: [4]u8 = undefined;
    writeU32BE(&data, std.math.maxInt(u32));
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFF, 0xFF, 0xFF, 0xFF }, &data);
}
