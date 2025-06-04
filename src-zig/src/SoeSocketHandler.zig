const binary_primitives = @import("./utils/binary_primitives.zig");
const pooling = @import("./pooling.zig");
const soe_packets = @import("./soe_packets.zig");
const soe_packet_utils = @import("./utils/soe_packet_utils.zig");
const soe_protocol = @import("./soe_protocol.zig");
const SoeSessionHandler = @import("./SoeSessionHandler.zig");
const std = @import("std");
const udp_socket = @import("utils/udp_socket.zig");

/// Manages the UDP socket and all connections.
pub const SoeSocketHandler = @This();

// External private fields
_allocator: std.mem.Allocator,
_session_params: *soe_protocol.SessionParams,
_app_params: *const soe_protocol.ApplicationParams,
_data_pool: *pooling.PooledDataManager,

// Internal private fields
_socket: udp_socket.UdpSocket,
_recv_buffer: []u8,
_connections: std.AutoHashMap(std.posix.sockaddr, *SoeSessionHandler),

// Public fields

pub fn init(
    allocator: std.mem.Allocator,
    session_params: *soe_protocol.SessionParams,
    app_params: *const soe_protocol.ApplicationParams,
    data_pool: *pooling.PooledDataManager,
) !SoeSocketHandler {
    const max_socket_len: i32 = @as(i32, @intCast(@max(session_params.udp_length, session_params.remote_udp_length))) * 64;

    return SoeSocketHandler{
        ._allocator = allocator,
        ._session_params = session_params,
        ._app_params = app_params,
        ._data_pool = data_pool,
        ._socket = try udp_socket.UdpSocket.init(max_socket_len),
        ._recv_buffer = try allocator.alloc(u8, @intCast(session_params.udp_length * 32)),
        ._connections = std.AutoHashMap(std.posix.sockaddr, *SoeSessionHandler).init(allocator),
    };
}

pub fn deinit(self: *SoeSocketHandler) void {
    // Free the socket and related buffers
    self._socket.deinit();
    self._allocator.free(self._recv_buffer);

    // Free all session connections
    var iterator = self._connections.valueIterator();
    while (iterator.next()) |element| {
        element.*.deinit();
    }
    self._connections.deinit();
}

/// Binds this socket handler to an endpoint, readying it to act as a server.
pub fn bind(self: *SoeSocketHandler, endpoint: std.net.Address) !void {
    try self._socket.bind(endpoint);
}

pub fn connect(self: *SoeSocketHandler, remote: std.net.Address) !*SoeSessionHandler {
    var handler = try self.createSessionHandler(remote, .client);
    try handler.sendSessionRequest();
    return handler;
}

/// Runs a tick of operations for the `SoeSocketHandler`.
/// Returns `true` if another tick must be run as soon as possible.
pub fn runTick(self: *SoeSocketHandler) !bool {
    const processed_one = try self.processOneFromSocket();

    var iterator = self._connections.valueIterator();
    while (iterator.next()) |entry| {
        var conn = entry.*;

        if (conn.termination_reason != .none) {
            _ = self._connections.remove(conn.remote.any);
        } else {
            conn.runTick() catch |err| {
                std.debug.print("Failed to run tick of session handler with error {any}", .{err});
                self.destroySession(conn);
            };
        }
    }

    return processed_one;
}

/// Sends data to the remote endpoint of a session.
pub fn sendSessionData(self: *const SoeSocketHandler, session: *const SoeSessionHandler, data: []const u8) !usize {
    return try self._socket.sendTo(session.remote, data);
}

fn processOneFromSocket(self: *SoeSocketHandler) !bool {
    const result = self._socket.receiveFrom(self._recv_buffer) catch |err| {
        if (err == std.posix.RecvFromError.WouldBlock) {
            return false;
        } else {
            return err;
        }
    };

    if (result.received_len <= 0) {
        return false;
    }

    var conn: ?*SoeSessionHandler = self._connections.get(result.sender.any);

    if (conn == null) {
        const op_code = try soe_packet_utils.readSoeOpCode(self._recv_buffer);
        switch (op_code) {
            .session_request => conn = try self.createSessionHandler(result.sender, .server),
            .remap_connection => return false,
            _ => return false,
        }
    }

    conn.?.handlePacket(self._recv_buffer[0..result.received_len]) catch |err| {
        std.debug.print("Failed to run tick of session handler with error {any}", .{err});
        self.destroySession(conn.?);
    };

    return true;
}

fn createSessionHandler(
    self: *SoeSocketHandler,
    remote: std.net.Address,
    mode: SoeSessionHandler.SessionMode,
) !*SoeSessionHandler {
    // TODO: We need to ensure we have a unique session params and app params per session handler
    // One way we could do this is by cloning the parameter objects and passing them to the session handler
    // by value. Consumers must then manipulate the param objects from the session handler itself

    const handler = try SoeSessionHandler.init(
        mode,
        remote,
        self,
        self._allocator,
        self._session_params,
        self._app_params,
        self._data_pool,
    );

    try self._connections.put(remote.any, handler);

    return handler;
}

fn destroySession(self: *SoeSocketHandler, session: *SoeSessionHandler) void {
    session.terminateSession(.application_released, true, false) catch {
        // This is fine. We're getting rid of it anyway
    };

    _ = self._connections.remove(session.remote.any);
    session.deinit();
}
