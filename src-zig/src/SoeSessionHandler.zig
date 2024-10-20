const binary_primitives = @import("./utils/binary_primitives.zig");
const network = @import("network");
const pooling = @import("./pooling.zig");
const soe_packets = @import("./soe_packets.zig");
const soe_protocol = @import("./soe_protocol.zig");
const SoeSocketHandler = @import("./SoeSocketHandler.zig");
const std = @import("std");

/// Manages an individual SOE session.
pub const SoeSessionHandler = @This();

// External private fields
_remote: network.EndPoint,
_parent: SoeSocketHandler,
_allocator: std.mem.Allocator,
_session_params: *const soe_protocol.SessionParams,
_app_params: *const soe_protocol.ApplicationParams,
_data_pool: pooling.PooledDataManager,

// Internal private fields

// Public fields

pub fn init(
    remote: network.EndPoint,
    parent: SoeSocketHandler,
    allocator: std.mem.Allocator,
    session_params: *const soe_protocol.SessionParams,
    app_params: *const soe_protocol.ApplicationParams,
    data_pool: pooling.PooledDataManager,
) !SoeSessionHandler {
    return SoeSessionHandler{
        ._remote = remote,
        ._parent = parent,
        ._allocator = allocator,
        ._session_params = session_params,
        ._app_params = app_params,
        ._data_pool = data_pool,
    };
}

pub fn deinit(self: *SoeSessionHandler) void {
    std.debug.print("{any}", .{self});
}

// pub fn runTick(self: *SoeSessionHandler) !void {
// }

pub fn handlePacket(packet: []u8) !void {
    std.debug.print("{x}", packet);
}
