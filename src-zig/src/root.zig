const ApplicationParams = @import("./soe_protocol.zig").ApplicationParams;
const pooling = @import("./pooling.zig");
const soe_protocol = @import("./soe_protocol.zig");
const SoeSessionHandler = @import("./SoeSessionHandler.zig");
const SoeSocketHandler = @import("./SoeSocketHandler.zig");
const std = @import("std");
const zlib = @cImport({
    @cInclude("zlib.h");
});

pub fn main() !void {
    var gpa = std.heap.GeneralPurposeAllocator(.{}){};
    const allocator = gpa.allocator();

    var app_data_handler = AppDataHandler{};
    var session_params: soe_protocol.SessionParams = .{
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
    var data_pool = pooling.PooledDataManager.init(
        allocator,
        512,
        5192,
    );
    defer data_pool.deinit();
    var handler: SoeSocketHandler = try SoeSocketHandler.init(
        allocator,
        &session_params,
        &app_params,
        data_pool,
    );
    defer handler.deinit();

    const bind_addr = try std.net.Address.parseIp("127.0.0.1", 0);
    try handler.bind(bind_addr);
    const address = try std.net.Address.parseIp("127.0.0.1", 12345);
    _ = try handler.connect(address);

    while (true) {
        const needs_new_tick = try handler.runTick();

        if (!needs_new_tick) {
            std.Thread.sleep(1 * std.time.ns_per_ms);
        }
    }

    if (gpa.deinit() == .leak) {
        @panic("WARNING: Memory leaks detected");
    }
}

const AppDataHandler = struct {
    count: i32 = 0,

    pub fn onSessionOpened(self: *anyopaque, session: *const SoeSessionHandler) void {
        _ = self;
        std.debug.print("Session Opened!\n", .{});
        session.sendHeartbeat() catch |err| {
            std.debug.print("failed to send heartbeat with error {}", .{err});
        };
    }

    pub fn onSessionClosed(
        self: *anyopaque,
        session: *const SoeSessionHandler,
        reason: soe_protocol.DisconnectReason,
    ) void {
        _ = self;
        const data_input_stats = session.getDataInputStats();
        std.debug.print("Session closed with reason {}!\n", .{reason});
        std.debug.print("- Total received data: {d}\n", .{data_input_stats.total_received_data});
        std.debug.print("- Acknowledge packets sent: {d}\n", .{data_input_stats.acknowledge_count});
        std.debug.print("- Duplicate data received: {d}\n", .{data_input_stats.duplicate_count});
        std.debug.print("- Out-of-order data received: {d}\n", .{data_input_stats.out_of_order_count});
        std.debug.print("- Total received bytes: {d}\n", .{data_input_stats.total_received_bytes});
    }

    pub fn receiveData(ptr: *anyopaque, session: *const SoeSessionHandler, data: []const u8) void {
        const self: *AppDataHandler = @ptrCast(@alignCast(ptr));
        _ = session;
        std.debug.print("[{d}] Received data {s}\n", .{ self.count, data });
        self.count += 1;
    }
};
