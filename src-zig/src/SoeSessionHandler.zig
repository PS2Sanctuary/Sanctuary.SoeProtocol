const binary_primitives = @import("./utils/binary_primitives.zig");
const network = @import("network");
const pooling = @import("./pooling.zig");
const soe_packets = @import("./soe_packets.zig");
const soe_protocol = @import("./soe_protocol.zig");
const std = @import("std");

/// Manages the UDP socket and all connections.
pub const SoeSessionHandler = @This();

// External private fields
_allocator: std.mem.Allocator,
_session_params: *const soe_protocol.SessionParams,
_app_params: *const soe_protocol.ApplicationParams,
_data_pool: pooling.PooledDataManager,

// Internal private fields

// Public fields

pub fn init(
    allocator: std.mem.Allocator,
    session_params: *const soe_protocol.SessionParams,
    app_params: *const soe_protocol.ApplicationParams,
    data_pool: pooling.PooledDataManager,
) !SoeSessionHandler {
    try network.init();

    return SoeSessionHandler{
        ._allocator = allocator,
        ._session_params = session_params,
        ._app_params = app_params,
        ._data_pool = data_pool,
        ._socket = network.Socket.create(network.AddressFamily.ipv4, network.Protocol.udp),
        ._recvBuffer = try allocator.alloc(u8, session_params.udp_length),
    };
}

pub fn deinit(self: *SoeSessionHandler) void {
    self._allocator.free(self._recvBuffer);
    network.deinit();
}

// pub fn runTick(self: *SoeSessionHandler) !void {
// }

pub fn handlePacket(packet: []u8) !void {
    std.debug.print("{x}", packet);
}
