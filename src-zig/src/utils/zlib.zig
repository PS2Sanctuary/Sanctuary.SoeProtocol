const std = @import("std");
const zlib = @cImport({
    @cInclude("zlib.h");
});

pub const ZlibError = error{
    StreamEnd,
    NeedDict,
    Errno,
    StreamError,
    DataError,
    MemError,
    BufError,
    VersionError,
    OutOfMemory,
    Unknown,
};

pub fn decompress(allocator: std.mem.Allocator, expected_len: usize, input: []const u8) !std.ArrayList(u8) {
    var output = try std.ArrayList(u8).initCapacity(
        allocator,
        expected_len,
    );
    var out_writer = output.writer();

    var out_buffer: [512]u8 = undefined;
    var stream: zlib.z_stream = .{
        .zalloc = null,
        .zfree = null,
        .@"opaque" = null,
        .avail_in = @truncate(input.len),
        .next_in = @constCast(input.ptr),
    };

    // Initialize the inflate state
    var z_result = zlib.inflateInit(&stream);
    if (z_result != zlib.Z_OK) {
        std.debug.panic(
            "Failed to initialize inflate state. Error code: {s}",
            .{zlib.zError(z_result)},
        );
    }
    defer z_result = zlib.inflateEnd(&stream);

    // Run inflate() on input until the output buffer has space leftover post-inflate
    while (stream.avail_out == 0) {
        stream.avail_out = out_buffer.len;
        stream.next_out = &out_buffer;

        z_result = zlib.inflate(&stream, zlib.Z_NO_FLUSH);
        std.debug.assert(z_result != zlib.Z_STREAM_ERROR); // Ensure the state isn't clobbered

        if (z_result == zlib.Z_NEED_DICT or z_result == zlib.Z_DATA_ERROR or z_result == zlib.Z_MEM_ERROR) {
            return zlibErrorFromInt(z_result);
        }

        const inflated_len = out_buffer.len - stream.avail_out;
        const amount_written = try out_writer.write(out_buffer[0..inflated_len]);
        std.debug.assert(amount_written == inflated_len);
    }

    std.debug.assert(z_result == zlib.Z_STREAM_END);
    return output;
}

fn zlibErrorFromInt(val: c_int) ZlibError {
    return switch (val) {
        zlib.Z_STREAM_END => error.StreamEnd,
        zlib.Z_NEED_DICT => error.NeedDict,
        zlib.Z_ERRNO => error.Errno,
        zlib.Z_STREAM_ERROR => error.StreamError,
        zlib.Z_DATA_ERROR => error.DataError,
        zlib.Z_MEM_ERROR => error.MemError,
        zlib.Z_BUF_ERROR => error.BufError,
        zlib.Z_VERSION_ERROR => error.VersionError,
        else => error.Unknown,
    };
}

test decompress {
    const deflated = [_]u8{
        0x78, 0x9C, 0x2B, 0xC9, 0xC8, 0x2C, 0x56, 0x00, 0xA2, 0x44, 0x85, 0xB4, 0xCC, 0x9C, 0x54, 0x05,
        0x23, 0x2E, 0x00, 0x35, 0x9A, 0x05, 0x52,
    };
    const expected = "this is a file 2\n";

    const output = try decompress(
        std.testing.allocator,
        expected.len,
        &deflated,
    );

    try std.testing.expectEqualSlices(u8, expected, output.items);
    output.deinit();
}
