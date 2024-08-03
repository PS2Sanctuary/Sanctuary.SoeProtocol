const std = @import("std");
const rc4 = @import("./utils/Rc4State.zig");
const testing = std.testing;

pub fn main() void {
    var rc4State = rc4.Rc4State.init(&[_]u8{ 0, 1, 2, 3, 4 });
    // for (rc4State._data) |i| {
    //     std.debug.print("0x{x} ", .{i});
    // }

    var data = [_]u8{ 0, 1, 2, 3, 4 };
    rc4State.transform(&data, &data);
    for (data) |i| {
        std.debug.print("0x{x} ", .{i});
    }
}

export fn add(a: i32, b: i32) i32 {
    return a + b;
}

test "basic add functionality" {
    try testing.expect(add(3, 7) == 10);
}
