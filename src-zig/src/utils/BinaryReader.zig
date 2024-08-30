/// A sequential reader of primitives from binary data
const BinaryReader = @import("BinaryReader.zig");
const std = @import("std");

const BinaryReaderError = error{
    NonBooleanValue,
    StringNotTerminated,
};

/// The underlying slice of binary data.
slice: []const u8,
/// The offset into the slice that the reader is at.
offset: usize = 0,

pub fn init(slice: []const u8) BinaryReader {
    return BinaryReader{ .slice = slice };
}

/// Advances the offset of the reader by the given amount.
pub fn advance(self: *BinaryReader, amount: usize) void {
    self.offset += amount;
}

/// Reads a byte value
pub fn readU8(self: *BinaryReader) u8 {
    self.offset += 1;
    return self.slice[self.offset - 1];
}

/// Reads a boolean value. Zero is treated as false, One as true, and all other values as errors.
pub fn readBool(self: *BinaryReader) BinaryReaderError!bool {
    return switch (self.readU8()) {
        0 => false,
        1 => true,
        else => BinaryReaderError.NonBooleanValue,
    };
}

/// Reads an unsigned 24-bit integer in big endian form.
pub fn readU24BE(self: *BinaryReader) u24 {
    var value: u24 = 0;
    value |= @as(u24, self.readU8()) << 16;
    value |= @as(u16, self.readU8()) << 8;
    value |= self.readU8();
    return value;
}

/// Reads an unsigned 32-bit integer in big endian form.
pub fn readU32BE(self: *BinaryReader) u32 {
    var value: u32 = 0;
    value |= @as(u32, self.readU8()) << 24;
    value |= @as(u32, self.readU8()) << 16;
    value |= @as(u16, self.readU8()) << 8;
    value |= self.readU8();
    return value;
}

pub fn readStringNullTerminated(self: *BinaryReader) BinaryReaderError![:0]const u8 {
    // Get the index of the first null-terminator (sentinel) past our offset
    const sentinel = [_]u8{0};
    const index = std.mem.indexOf(u8, self.slice[self.offset..], &sentinel);

    // Error out if std.mem.indexOf() could not find the sentinel value
    if (index == null) {
        return BinaryReaderError.StringNotTerminated;
    }

    const finalOffset = self.offset + index.?;
    // Take a slice between our offsets, and imply the presence of a sentinel
    const value: [:0]const u8 = self.slice[self.offset..finalOffset :0];

    self.offset = finalOffset;
    return value;
}

test readU8 {
    const data = [_]u8{ 0x1, 0xFF };
    var reader = BinaryReader.init(&data);

    try std.testing.expectEqual(0x1, reader.readU8());
    try std.testing.expectEqual(0xFF, reader.readU8());
}

test readBool {
    const data = [_]u8{ 0x0, 0x1, 0xFF };
    var reader = BinaryReader.init(&data);

    try std.testing.expectEqual(false, reader.readBool());
    try std.testing.expectEqual(true, reader.readBool());
    try std.testing.expectError(BinaryReaderError.NonBooleanValue, reader.readBool());
}

test readU24BE {
    const data = [_]u8{ 0x01, 0x00, 0x00, 0x01, 0x00, 0x63 };
    var reader = BinaryReader.init(&data);

    try std.testing.expectEqual(std.math.maxInt(u16) + 1, reader.readU24BE());
    try std.testing.expectEqual(std.math.maxInt(u16) + 100, reader.readU24BE());
}

test readU32BE {
    const data = [_]u8{ 0x00, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
    var reader = BinaryReader.init(&data);

    try std.testing.expectEqual(std.math.maxInt(u16) + 1, reader.readU32BE());
    try std.testing.expectEqual(std.math.maxInt(u32), reader.readU32BE());
}

test readStringNullTerminated {
    const data = "testString";
    // Coerce to slice with sentinel included
    var reader = BinaryReader.init(data[0 .. data.len + 1]);
    try std.testing.expectEqualStrings("testString", try reader.readStringNullTerminated());

    // We should error out on data that doesn't have a null-terminator
    const data2 = [_]u8{ 'A', 'B' };
    var reader2 = BinaryReader.init(&data2);
    try std.testing.expectError(BinaryReaderError.StringNotTerminated, reader2.readStringNullTerminated());
}
