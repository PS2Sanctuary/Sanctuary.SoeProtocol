const binary_primitives = @import("./utils/binary_primitives.zig");
const network = @import("network");
const pooling = @import("./pooling.zig");
const soe_packets = @import("./soe_packets.zig");
const soe_protocol = @import("./soe_protocol.zig");
const SoeSessionHandler = @import("./SoeSessionHandler.zig");
const std = @import("std");

/// Manages the UDP socket and all connections.
pub const SoeSocketHandler = @This();

// External private fields
_allocator: std.mem.Allocator,
_session_params: *const soe_protocol.SessionParams,
_app_params: *const soe_protocol.ApplicationParams,
_data_pool: pooling.PooledDataManager,

// Internal private fields
_socket: network.Socket,
_recvBuffer: []u8,
_connections: std.AutoHashMap(network.EndPoint, SoeSessionHandler),

// Public fields

pub fn init(
    allocator: std.mem.Allocator,
    session_params: *const soe_protocol.SessionParams,
    app_params: *const soe_protocol.ApplicationParams,
    data_pool: pooling.PooledDataManager,
) !SoeSocketHandler {
    try network.init();

    return SoeSocketHandler{
        ._allocator = allocator,
        ._session_params = session_params,
        ._app_params = app_params,
        ._data_pool = data_pool,
        ._socket = try network.Socket.create(network.AddressFamily.ipv4, network.Protocol.udp),
        ._recvBuffer = try allocator.alloc(u8, session_params.udp_length),
        ._connections = std.AutoHashMap(network.EndPoint, SoeSessionHandler).init(allocator),
    };
}

pub fn deinit(self: *SoeSocketHandler) void {
    // Free the socket and related buffers
    self._allocator.free(self._recvBuffer);
    self._socket.close();
    network.deinit();

    // Free all session connections
    for (self._connections.valueIterator().items) |session| {
        session.deinit();
    }
    self._connections.deinit();
}

/// Binds this socket handler to an endpoint, readying it to act as a server.
pub fn bind(self: *SoeSocketHandler, endpoint: network.EndPoint) !void {
    self._socket.bind(endpoint);
}

pub fn connect(self: *SoeSocketHandler, remote: network.EndPoint) !void {
    self.spawnSessionHandler(remote);
}

pub fn runTick(self: *SoeSocketHandler) !void {
    const result = try self._socket.receiveFrom(self._recvBuffer);
    var conn: ?SoeSessionHandler = self._connections.get(result.sender);

    if (conn == null) {
        conn = self.spawnSessionHandler(result.sender);
    }

    try conn.?.handlePacket(self._recvBuffer[0..result.numberOfBytes]);
}

fn spawnSessionHandler(self: *SoeSocketHandler, remote: network.EndPoint) SoeSessionHandler {
    const handler = SoeSessionHandler.init(
        remote,
        self,
        self._allocator,
        self._session_params,
        self._app_params,
        self._data_pool,
    );

    self._connections.put(remote, handler);

    return handler;
}
