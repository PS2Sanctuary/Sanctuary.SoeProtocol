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
