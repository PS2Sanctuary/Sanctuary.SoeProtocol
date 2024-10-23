const ApplicationParams = @import("./soe_protocol.zig").ApplicationParams;
const pooling = @import("./pooling.zig");
const SessionParams = @import("./soe_protocol.zig").SessionParams;
const SoeSocketHandler = @import("./SoeSocketHandler.zig");
const std = @import("std");
const zlib = @cImport({
    @cInclude("zlib.h");
});

pub fn main() !void {
    std.debug.print("zlib version: {s}\n", .{zlib.ZLIB_VERSION});

    const CHUNK = 16384;
    var ret: i32 = undefined;
    var have: u8 = undefined;
    var strm: zlib.z_stream = undefined;
    var in: [CHUNK]u8 = undefined;
    var out: [CHUNK]u8 = undefined;

    // Allocate inflate state
    strm.zalloc = zlib.Z_NULL;
    strm.zfree = zlib.Z_NULL;
    strm.@"opaque" = zlib.Z_NULL;
    strm.avail_in = 0;
    strm.next_in = zlib.Z_NULL;
    ret = zlib.inflateInit(&strm);
    if (ret != zlib.Z_OK) {
        @panic("failed to initialize deflater");
    }

    const input = try std.fs.createFileAbsolute("~/deflated.bin", .{ .read = true });
    const output = try std.fs.createFileAbsolute("~/inflated.out");

    // Decompress until inflate stream ends
    while (ret != zlib.Z_STREAM_END) {
        strm.avail_in = @truncate(input.read(&in) catch {
            std.debug.print("input read failed. Zlib error number: {d}", zlib.Z_ERRNO);
            zlib.deflatedEnd(&strm);
            return;
        });
        if (strm.avail_in == 0) {
            break;
        }
        strm.next_in = in;

        // Run inflate() on input until the output buffer has space leftover post-inflate
        strm.avail_out = 0;
        while (strm.avail_out == 0) {
            strm.avail_out = CHUNK;
            strm.next_out = out;

            ret = zlib.inflate(&strm, zlib.Z_NO_FLUSH);
            std.debug.assert(ret != zlib.Z_STREAM_ERROR); // Check state not clobbered

            switch (ret) {
                zlib.Z_NEED_DICT or zlib.Z_DATA_ERROR or zlib.Z_MEM_ERROR => {
                    zlib.inflateEnd(&strm);
                    std.debug.panic("Inflate failed with return value {d}", ret);
                },
            }

            have = CHUNK - strm.avail_out;
            const amount_written = output.write(out[0..have]) catch {
                zlib.inflateEnd(&strm);
                std.debug.print("output write failed. Zlib error number: {d}", zlib.Z_ERRNO);
                return;
            };
            if (amount_written != have) {
                zlib.inflateEnd(&strm);
                std.debug.print("output write failed. Zlib error number: {d}", zlib.Z_ERRNO);
                return;
            }
        }
        std.debug.assert(strm.avail_in == 0); // All input should have been used
    }

    // Cleanup
    zlib.inflateEnd(&strm);

    return;

    // var gpa = std.heap.GeneralPurposeAllocator(.{}){};
    // const allocator = gpa.allocator();

    // const session_params: SessionParams = .{};
    // const app_params: ApplicationParams = .{
    //     .initial_rc4_state = undefined,
    //     .handle_app_data = undefined,
    //     .handler_ptr = undefined,
    //     .is_encryption_enabled = false,
    //     .on_session_closed = undefined,
    //     .on_session_opened = undefined,
    // };
    // const data_pool = pooling.PooledDataManager.init(allocator, 512, 5192);

    // const handler: SoeSocketHandler = try SoeSocketHandler.init(
    //     allocator,
    //     &session_params,
    //     &app_params,
    //     data_pool,
    // );

    // std.debug.print("{any}", .{handler._app_params.is_encryption_enabled});
}
