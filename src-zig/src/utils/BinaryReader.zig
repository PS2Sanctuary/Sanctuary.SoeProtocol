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
pub fn readByte(self: *BinaryReader) u8 {
    self.offset += 1;
    return self.slice[self.offset - 1];
}

/// Reads a boolean value. Zero is treated as false, One as true, and all other values as errors.
pub fn readBool(self: *BinaryReader) BinaryReaderError!bool {
    return switch (self.readByte()) {
        0 => false,
        1 => true,
        else => BinaryReaderError.NonBooleanValue,
    };
}

/// Reads an unsigned 24-bit integer in big endian form.
pub fn readUInt24BE(self: *BinaryReader) u24 {
    var value: u24 = 0;
    value |= @as(u24, self.readByte()) << 16;
    value |= @as(u16, self.readByte()) << 8;
    value |= self.readByte();
    return value;
}

/// Reads an unsigned 32-bit integer in big endian form.
pub fn readUInt32BE(self: *BinaryReader) u32 {
    var value: u32 = 0;
    value |= @as(u32, self.readByte()) << 24;
    value |= @as(u32, self.readByte()) << 16;
    value |= @as(u16, self.readByte()) << 8;
    value |= self.readByte();
    return value;
}

// pub fn readStringNullTerminated(self: *BinaryReader) BinaryReaderError![:0]const u8 {
//     // Slice up to a null-terminator (sentinel). This slice will not include the sentinel
//     const initial_slice: []const u8 = std.mem.sliceTo(self.slice[self.offset..], 0);
//     // Get the actual offset of the sentinel in our slice
//     const final_offset = self.offset + initial_slice.len;

//     // We need to check that a sentinel was actually found, as std.mem.sliceTo() will return
//     // the entire slice if it can't find the sentinel value
//     if (self.slice[final_offset] != 0) {
//         return BinaryReaderError.StringNotTerminated;
//     }

//     // Slice our buffer and cast to a sentinel-slice
//     const value: [:0]const u8 = self.slice[self.offset..final_offset :0];
//     self.offset = final_offset;

//     return value;
// }

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

test "Test BinaryReader.readByte()" {
    const data = [_]u8{ 0x1, 0xFF };
    var reader = BinaryReader.init(&data);

    try std.testing.expectEqual(0x1, reader.readByte());
    try std.testing.expectEqual(0xFF, reader.readByte());
}

test "Test BinaryReader.readBool()" {
    const data = [_]u8{ 0x0, 0x1, 0xFF };
    var reader = BinaryReader.init(&data);

    try std.testing.expectEqual(false, reader.readBool());
    try std.testing.expectEqual(true, reader.readBool());
    try std.testing.expectError(BinaryReaderError.NonBooleanValue, reader.readBool());
}

test "Test BinaryReader.readUInt24BE()" {
    const data = [_]u8{ 0x01, 0x00, 0x00, 0x01, 0x00, 0x63 };
    var reader = BinaryReader.init(&data);

    try std.testing.expectEqual(std.math.maxInt(u16) + 1, reader.readUInt24BE());
    try std.testing.expectEqual(std.math.maxInt(u16) + 100, reader.readUInt24BE());
}

test "Test BinaryReader.readUInt32BE()" {
    const data = [_]u8{ 0x00, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
    var reader = BinaryReader.init(&data);

    try std.testing.expectEqual(std.math.maxInt(u16) + 1, reader.readUInt32BE());
    try std.testing.expectEqual(std.math.maxInt(u32), reader.readUInt32BE());
}

test "Test BinaryReader.readStringNullTerminated()" {
    const data = "testString";
    // Coerce to slice with sentinel included
    var reader = BinaryReader.init(data[0 .. data.len + 1]);
    try std.testing.expectEqualStrings("testString", try reader.readStringNullTerminated());

    // We should error out on data that doesn't have a null-terminator
    const data2 = [_]u8{ 'A', 'B' };
    var reader2 = BinaryReader.init(&data2);
    try std.testing.expectError(BinaryReaderError.StringNotTerminated, reader2.readStringNullTerminated());
}
