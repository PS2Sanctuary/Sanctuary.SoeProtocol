/// A sequential writer of primitives to binary data
const BinaryWriter = @import("BinaryWriter.zig");

const BinaryPrimitives = @import("BinaryPrimitives.zig");
const std = @import("std");

/// The underlying slice of binary data.
slice: []u8,
/// The offset into the slice that the reader is at.
offset: usize = 0,

pub fn init(slice: []u8) BinaryWriter {
    return BinaryWriter{ .slice = slice };
}

/// Advances the offset of the writer by the given amount.
pub fn advance(self: *BinaryWriter, amount: usize) void {
    self.offset += amount;
}

/// Writes a byte value
pub fn writeU8(self: *BinaryWriter, value: u8) void {
    self.slice[self.offset] = value;
    self.offset += @sizeOf(u8);
}

/// Writes a boolean value. False is treated as a zero, and true as a one.
pub fn writeBool(self: *BinaryWriter, value: bool) void {
    self.writeU8(switch (value) {
        false => 0,
        true => 1,
    });
}

/// Writes an unsigned 16-bit integer in big endian form.
pub fn writeU16BE(self: *BinaryWriter, value: u16) void {
    BinaryPrimitives.writeU16BE(self.slice[self.offset..], value);
    self.offset += 2;
}

/// Writes an unsigned 24-bit integer in big endian form.
pub fn writeU24BE(self: *BinaryWriter, value: u24) void {
    BinaryPrimitives.writeU24BE(self.slice[self.offset..], value);
    self.offset += 3;
}

/// Writes an unsigned 32-bit integer in big endian form.
pub fn writeU32BE(self: *BinaryWriter, value: u32) void {
    BinaryPrimitives.writeU32BE(self.slice[self.offset..], value);
    self.offset += 4;
}

/// Writes a null-terminated string.
pub fn writeStringNullTerminated(self: *BinaryWriter, value: [:0]const u8) void {
    @memcpy(
        self.slice[self.offset .. self.offset + value.len + 1],
        value[0 .. value.len + 1],
    );
    self.offset += value.len + 1;
}

test writeU8 {
    var data: [2]u8 = undefined;
    var writer = BinaryWriter.init(&data);
    writer.writeU8(0x1);
    writer.writeU8(0xFF);

    try std.testing.expectEqual(0x1, data[0]);
    try std.testing.expectEqual(0xFF, data[1]);
}

test writeBool {
    var data: [2]u8 = undefined;
    var writer = BinaryWriter.init(&data);
    writer.writeBool(false);
    writer.writeBool(true);

    try std.testing.expectEqual(0, data[0]);
    try std.testing.expectEqual(1, data[1]);
}

test writeU16BE {
    var data: [4]u8 = undefined;
    var writer = BinaryWriter.init(&data);
    writer.writeU16BE(std.math.maxInt(u8));
    writer.writeU16BE(std.math.maxInt(u16));

    try std.testing.expectEqualSlices(u8, &[_]u8{ 0x00, 0xFF }, data[0..2]);
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFF, 0xFF }, data[2..4]);
}

test writeU24BE {
    var data: [6]u8 = undefined;
    var writer = BinaryWriter.init(&data);
    writer.writeU24BE(std.math.maxInt(u8));
    writer.writeU24BE(std.math.maxInt(u24) - 1);

    try std.testing.expectEqualSlices(u8, &[_]u8{ 0x00, 0x00, 0xFF }, data[0..3]);
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFF, 0xFF, 0xFE }, data[3..6]);
}

test writeU32BE {
    var data: [8]u8 = undefined;
    var writer = BinaryWriter.init(&data);
    writer.writeU32BE(std.math.maxInt(u16) + 1);
    writer.writeU32BE(std.math.maxInt(u32));

    try std.testing.expectEqualSlices(u8, &[_]u8{ 0x00, 0x01, 0x00, 0x00 }, data[0..4]);
    try std.testing.expectEqualSlices(u8, &[_]u8{ 0xFF, 0xFF, 0xFF, 0xFF }, data[4..8]);
}

test writeStringNullTerminated {
    var data: [12]u8 = undefined;
    var writer = BinaryWriter.init(&data);
    writer.writeStringNullTerminated("test");
    writer.writeStringNullTerminated("String");

    // Coerce string to slice with sentinel included
    try std.testing.expectEqualSlices(u8, "test\x00String"[0..12], &data);
}
