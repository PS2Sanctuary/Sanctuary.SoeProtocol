const std = @import("std");
const crc32 = @import("./utils/crc32.zig");
const testing = std.testing;

pub fn main() void {
    const data = [_]u8{ 0, 1, 2, 3, 4 };
    const seed: u32 = 32;

    const result = crc32.hash(&data, seed);
    std.debug.print("{}", .{result});
}

export fn add(a: i32, b: i32) i32 {
    return a + b;
}

test "basic add functionality" {
    try testing.expect(add(3, 7) == 10);
}
