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

    try deflate("/home/carls/input.txt", "/home/carls/deflated.bin");
    try inflate("/home/carls/deflated.bin", "/home/carls/inflated.txt");

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

fn deflate(input_path: []const u8, output_path: []const u8) !void {
    const CHUNK_SIZE = 16384;

    var flush: i32 = undefined;
    var in_buffer: [CHUNK_SIZE]u8 = undefined;
    var out_buffer: [CHUNK_SIZE]u8 = undefined;
    var stream: zlib.z_stream = .{
        .zalloc = null,
        .zfree = null,
        .@"opaque" = null,
    };

    const in_file = try std.fs.openFileAbsolute(input_path, .{ .mode = .read_only });
    const out_file = try std.fs.createFileAbsolute(output_path, .{});
    const in_reader = in_file.reader();
    const out_writer = out_file.writer();

    // Initialize the deflate state.
    var z_result = zlib.deflateInit(&stream, zlib.Z_DEFAULT_COMPRESSION);
    if (z_result != zlib.Z_OK) {
        std.debug.panic(
            "Failed to initialize deflate state. Error code: {s}",
            .{zlib.zError(z_result)},
        );
    }

    while (flush != zlib.Z_FINISH) {
        stream.avail_in = @truncate(in_reader.read(&in_buffer) catch |err| {
            z_result = zlib.deflateEnd(&stream);
            std.debug.panic("Input read failed: {any}", .{err});
        });

        flush = if (stream.avail_in == 0) zlib.Z_FINISH else zlib.Z_NO_FLUSH;
        stream.next_in = &in_buffer;

        // Run deflate() on input until the output buffer has space leftover post-deflate
        stream.avail_out = 0;
        while (stream.avail_out == 0) {
            stream.avail_out = CHUNK_SIZE;
            stream.next_out = &out_buffer;

            z_result = zlib.deflate(&stream, flush); // This method doesn't have a 'bad' result
            std.debug.assert(z_result != zlib.Z_STREAM_ERROR); // Ensure the state isn't clobbered

            const deflated_len = CHUNK_SIZE - stream.avail_out;
            const amount_written = out_writer.write(out_buffer[0..deflated_len]) catch |err| {
                z_result = zlib.deflateEnd(&stream);
                std.debug.panic("Output write failed: {any}", .{err});
            };
            if (amount_written != deflated_len) {
                z_result = zlib.deflateEnd(&stream);
                std.debug.panic(
                    "Output write did not commit enough bytes ({d} out of {d} expected)",
                    .{ amount_written, deflated_len },
                );
            }
        }
        std.debug.assert(stream.avail_in == 0); // All input should have been used
    }

    std.debug.assert(z_result == zlib.Z_STREAM_END); // The stream should be complete
    z_result = zlib.deflateEnd(&stream);
}

fn inflate(input_path: []const u8, output_path: []const u8) !void {
    const CHUNK_SIZE = 16384;

    var in_buffer: [CHUNK_SIZE]u8 = undefined;
    var out_buffer: [CHUNK_SIZE]u8 = undefined;
    var stream: zlib.z_stream = .{
        .zalloc = null,
        .zfree = null,
        .@"opaque" = null,
        .avail_in = 0,
        .next_in = null,
    };

    const in_file = try std.fs.openFileAbsolute(input_path, .{ .mode = .read_only });
    const out_file = try std.fs.createFileAbsolute(output_path, .{});
    const in_reader = in_file.reader();
    const out_writer = out_file.writer();

    // Initialize the inflate state
    var z_result = zlib.inflateInit(&stream);
    if (z_result != zlib.Z_OK) {
        std.debug.panic(
            "Failed to initialize inflate state. Error code: {s}",
            .{zlib.zError(z_result)},
        );
    }

    // Decompress until inflate stream ends
    while (z_result != zlib.Z_STREAM_END) {
        stream.avail_in = @truncate(in_reader.read(&in_buffer) catch |err| {
            z_result = zlib.inflateEnd(&stream);
            std.debug.panic("Input read failed: {any}", .{err});
        });
        if (stream.avail_in == 0) {
            break;
        }
        stream.next_in = &in_buffer;

        // Run inflate() on input until the output buffer has space leftover post-inflate
        stream.avail_out = 0;
        while (stream.avail_out == 0) {
            stream.avail_out = CHUNK_SIZE;
            stream.next_out = &out_buffer;

            z_result = zlib.inflate(&stream, zlib.Z_NO_FLUSH);
            std.debug.assert(z_result != zlib.Z_STREAM_ERROR); // Ensure the state isn't clobbered

            if (z_result == zlib.Z_NEED_DICT or z_result == zlib.Z_DATA_ERROR or z_result == zlib.Z_MEM_ERROR) {
                _ = zlib.inflateEnd(&stream);
                std.debug.panic("Inflate failed with return value {s}", .{zlib.zError(z_result)});
            }

            const inflated_len = CHUNK_SIZE - stream.avail_out;
            const amount_written = out_writer.write(out_buffer[0..inflated_len]) catch |err| {
                z_result = zlib.inflateEnd(&stream);
                std.debug.panic("Output write failed: {any}", .{err});
            };
            if (amount_written != inflated_len) {
                z_result = zlib.inflateEnd(&stream);
                std.debug.panic(
                    "Output write did not commit enough bytes ({d} out of {d} expected)",
                    .{ amount_written, inflated_len },
                );
            }
        }
    }

    // Cleanup
    z_result = zlib.inflateEnd(&stream);
}
