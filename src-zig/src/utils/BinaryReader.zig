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

/// Reads a null-terminated string, returning a slice over the underlying buffer.
pub fn readStringNullTerminated(self: *BinaryReader) BinaryReaderError![:0]const u8 {
    // Get the index of the first null-terminator (sentinel) past our offset
    const sentinel = [_]u8{0};
    const index = std.mem.indexOf(u8, self.slice[self.offset..], &sentinel);

    // Error out if std.mem.indexOf() could not find the sentinel value
    if (index == null) {
        return BinaryReaderError.StringNotTerminated;
    }

    const sentinel_offset = self.offset + index.?;
    // Take a slice between our offsets, and imply the presence of a sentinel
    const value: [:0]const u8 = self.slice[self.offset..sentinel_offset :0];

    self.offset = sentinel_offset + 1;
    return value;
}

/// Reads a null-terminated string, returning an allocated copy.
pub fn readStringNullTerminatedWithAlloc(
    self: *BinaryReader,
    allocator: std.mem.Allocator,
) error{ StringNotTerminated, OutOfMemory }![:0]const u8 {
    // Get the index of the first null-terminator (sentinel) past our offset
    const sentinel = [_]u8{0};
    const index = std.mem.indexOf(u8, self.slice[self.offset..], &sentinel);

    // Error out if std.mem.indexOf() could not find the sentinel value
    if (index == null) {
        return BinaryReaderError.StringNotTerminated;
    }

    // Allocate a sentinel slice for the string.
    const sentinel_offset = self.offset + index.?;
    const value = try allocator.allocSentinel(u8, sentinel_offset - self.offset, 0);

    // Coerce the allocated value to a non-sentinel slice, and copy the string over, sentinel included
    @memcpy(value[0 .. value.len + 1], self.slice[self.offset .. sentinel_offset + 1]);

    self.offset = sentinel_offset + 1;
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
    const data = "test\x00String";
    // Coerce to slice with sentinel included
    var reader = BinaryReader.init(data[0 .. data.len + 1]);
    try std.testing.expectEqualStrings("test", try reader.readStringNullTerminated());
    try std.testing.expectEqualStrings("String", try reader.readStringNullTerminated());

    // We should error out on data that doesn't have a null-terminator
    const data2 = [_]u8{ 'A', 'B' };
    var reader2 = BinaryReader.init(&data2);
    try std.testing.expectError(BinaryReaderError.StringNotTerminated, reader2.readStringNullTerminated());
}

test readStringNullTerminatedWithAlloc {
    const data = "test\x00String";
    // Coerce to slice with sentinel included
    var reader = BinaryReader.init(data[0 .. data.len + 1]);

    const result = try reader.readStringNullTerminatedWithAlloc(std.testing.allocator);
    defer std.testing.allocator.free(result);
    try std.testing.expectEqualStrings("test", result);

    const result2 = try reader.readStringNullTerminatedWithAlloc(std.testing.allocator);
    defer std.testing.allocator.free(result2);
    try std.testing.expectEqualStrings("String", result2);

    // We should error out on data that doesn't have a null-terminator
    const data2 = [_]u8{ 'A', 'B' };
    var reader2 = BinaryReader.init(&data2);
    try std.testing.expectError(BinaryReaderError.StringNotTerminated, reader2.readStringNullTerminated());
}
