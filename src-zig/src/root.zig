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

    // Decompress until deflate stream ends
    while (true) {
        // TODO: Read in the first chunk to the `in` buffer, setting the amount read
        strm.avail_in = fread(in, 1, CHUNK, source);
        if (ferror(source)) {
            zlib.deflateEnd(&strm);
            std.debug.panic("input read failed. Zlib error number: {d}", zlib.Z_ERRNO);
        }
        if (strm.avail_in == 0) {
            break;
        }
        strm.next_in = in;

        // run inflate() on input until output buffer not full
        while (true) {
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
            // TODO: write output
            if (fwrite(out, 1, have, dest) != have || ferror(dest)) {
                zlib.inflateEnd(&strm);
                std.debug.panic("output write failed. Zlib error number: {d}", zlib.Z_ERRNO);
            }

            if (strm.avail_out != 0) {
                break;
            }
        }
        std.debug.assert(strm.avail_in == 0); // All input should have been used

        if (ret == zlib.Z_STREAM_END) {
            break;
        }
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
