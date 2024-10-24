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
_recv_buffer: []u8,
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
        ._recv_buffer = try allocator.alloc(u8, session_params.remote_udp_length),
        ._connections = std.AutoHashMap(network.EndPoint, SoeSessionHandler).init(allocator),
    };
}

pub fn deinit(self: *SoeSocketHandler) void {
    // Free the socket and related buffers
    self._socket.close();
    self._allocator.free(self._recv_buffer);
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
    const result = try self._socket.receiveFrom(self._recv_buffer);

    if (result.numberOfBytes > 0) {
        var conn: ?SoeSessionHandler = self._connections.get(result.sender);

        if (conn == null) {
            // TODO: Check for remap request
            conn = self.spawnSessionHandler(result.sender);
        }

        try conn.?.handlePacket(self._recv_buffer[0..result.numberOfBytes]);
    }

    for (self._connections.valueIterator().items) |conn| {
        conn.runTick();
    }
}

/// Sends data to the remote endpoint of a session.
pub fn sendSessionData(self: *SoeSocketHandler, session: SoeSessionHandler, data: []const u8) network.Socket.SendError!usize {
    return self._socket.sendTo(session.remote, data);
}

/// Terminates and removes a session from the connection list.
pub fn terminateSession(self: *SoeSocketHandler, session: SoeSessionHandler) void {
    session.terminateSession(.application, true, false);
    self._connections.remove(session.remote);
}

fn spawnSessionHandler(self: *SoeSocketHandler, remote: network.EndPoint) SoeSessionHandler {
    // TODO: We need to ensure we have a unique session params and app params per session handler

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
