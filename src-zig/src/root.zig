const ApplicationParams = @import("./soe_protocol.zig").ApplicationParams;
const pooling = @import("./pooling.zig");
const soe_protocol = @import("./soe_protocol.zig");
const SoeSocketHandler = @import("./SoeSocketHandler.zig");
const std = @import("std");
const zlib = @cImport({
    @cInclude("zlib.h");
});

pub fn main() !void {
    var gpa = std.heap.GeneralPurposeAllocator(.{}){};
    const allocator = gpa.allocator();

    var app_data_handler = AppDataHandler{};
    const session_params: soe_protocol.SessionParams = .{
        .application_protocol = "Ping_1",
    };
    const app_params: ApplicationParams = .{
        .initial_rc4_state = undefined,
        .is_encryption_enabled = false,
        .handler_ptr = &app_data_handler,
        .handle_app_data = AppDataHandler.receiveData,
        .on_session_closed = AppDataHandler.onSessionClosed,
        .on_session_opened = AppDataHandler.onSessionOpened,
    };
    const data_pool = pooling.PooledDataManager.init(
        allocator,
        512,
        5192,
    );
    var handler: SoeSocketHandler = try SoeSocketHandler.init(
        allocator,
        &session_params,
        &app_params,
        data_pool,
    );

    const address = try std.net.Address.resolveIp("localhost", 12345);
    _ = try handler.connect(address);

    while (true) {
        std.Thread.sleep(1 * std.time.ns_per_ms);
        try handler.runTick();
    }
}

const AppDataHandler = struct {
    pub fn onSessionOpened(self: *anyopaque) void {
        _ = self;
        std.debug.print("Session Opened!", .{});
    }

    pub fn onSessionClosed(self: *anyopaque, reason: soe_protocol.DisconnectReason) void {
        _ = self;
        std.debug.print("Session closed with reason {}!", .{reason});
    }

    pub fn receiveData(ptr: *anyopaque, data: []const u8) void {
        _ = ptr;
        std.debug.print("Received data {s}", .{data});
    }
};
