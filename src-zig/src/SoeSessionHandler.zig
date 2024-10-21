const binary_primitives = @import("./utils/binary_primitives.zig");
const BinaryWriter = @import("./utils/BinaryWriter.zig");
const network = @import("network");
const pooling = @import("./pooling.zig");
const ReliableDataInputChannel = @import("./reliable_data/ReliableDataInputChannel.zig");
const soe_packets = @import("./soe_packets.zig");
const soe_packet_utils = @import("./utils/soe_packet_utils.zig");
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
_contextual_send_buffer: []u32,
_data_input_channel: ReliableDataInputChannel,
_last_received_packet_tick: std.time.Instant,

// Public fields
mode: SessionMode,
state: SessionState,
session_id: u32,
termination_reason: soe_protocol.DisconnectReason,
terminated_by_remote: bool,

pub fn init(
    mode: SessionMode,
    remote: network.EndPoint,
    parent: SoeSocketHandler,
    allocator: std.mem.Allocator,
    session_params: *const soe_protocol.SessionParams,
    app_params: *const soe_protocol.ApplicationParams,
    data_pool: pooling.PooledDataManager,
) !SoeSessionHandler {
    return SoeSessionHandler{
        .mode = mode,
        ._remote = remote,
        ._parent = parent,
        ._allocator = allocator,
        ._session_params = session_params,
        ._app_params = app_params,
        ._data_pool = data_pool,
        ._contextual_send_buffer = allocator.alloc(u8, session_params.udp_length),
        ._data_input_channel = ReliableDataInputChannel.init(
            allocator,
            session_params,
            app_params,
            data_pool,
        ),
        ._last_received_packet_tick = std.time.Instant.now(),
        .state = SessionState.negotiating,
    };
}

pub fn deinit(self: *SoeSessionHandler) void {
    self._allocator.free(self._contextual_send_buffer);
    self._data_input_channel.deinit();
}

pub fn runTick(self: *SoeSessionHandler) !void {
    sendHeartbeatIfRequired();

    if (std.time.Instant.since(self._last_received_packet_tick) > self._session_params.inactivity_timeout_ns) {
        terminateSession(soe_protocol.DisconnectReason.timeout, false);
    }

    self._data_input_channel.runTick();
}

pub fn handlePacket(self: *SoeSessionHandler, packet: []u8) !void {
    const op_code = soe_packet_utils.validatePacket(
        packet,
        self._session_params,
    ) catch {
        self.terminateSession(
            soe_protocol.DisconnectReason.corrupt_packet,
            true,
            false,
        );
        return;
    };

    if (self.state == SessionState.waiting_on_client_to_open_session) {
        // TODO: _application.OnSessionOpened();
        self.state == SessionState.running;
    }

    // We set this after packet validation as a primitive method of stopping the connection
    // if all we've received is multiple corrupt packets in a row
    self._last_received_packet_tick = std.time.Instant.now();
    packet = packet[@sizeOf(soe_protocol.SoeOpCode)..];
    const is_sessionless: bool = soe_packet_utils.isContextlessPacket(op_code);

    if (is_sessionless) {
        handleContextlessPacket(opCode, packet);
    } else {
        handleContextualPacket(opCode, packet[0 .. packet.len - self._session_params.CrcLength]);
    }
}

/// Sends a contextual packet.
/// <param name="opCode">The OP code of the packet to send.</param>
/// <param name="packetData">The packet data, not including the OP code.</param>
pub fn sendContextualPacket(self: *SoeSessionHandler, op_code: soe_protocol.SoeOpCode, packet_data: []u8) !void {
    const extra_bytes: i32 = @sizeOf(soe_protocol.SoeOpCode) +
        @intFromBool(self._session_params.is_compression_enabled) +
        self._session_params.crc_length;

    if (packet_data.len + extra_bytes > self._session_params.remote_udp_length) {
        return error.PacketLargerThanRemoteUdplength;
    }

    var writer = BinaryWriter.init(self._contextual_send_buffer);

    writer.writeU16BE(@intFromEnum(op_code));
    if (self._session_params.is_compression_enabled)
        writer.writeBool(false); // Compression is not implemented at the moment
    // TODO: writer.WriteBytes(packetData);
    soe_packet_utils.appendCrc(&writer, self._session_params.crc_seed, self._session_params.crc_length);

    // TODO: _networkWriter.Send(writer.Consumed);
}

/// Terminates the session. This may be called whenever the session needs to close,
/// e.g. when the other party has disconnected, or an internal error has occurred.
/// <param name="reason">The termination reason.</param>
/// <param name="notifyRemote">Whether to notify the remote party.</param>
/// <param name="terminatedByRemote">Indicates whether this termination has come from the remote party.</param>
fn terminateSession(
    self: *SoeSessionHandler,
    reason: soe_protocol.DisconnectReason,
    notify_remote: bool,
    terminated_by_remote: bool,
) void {
    if (self.state == SessionState.terminated) {
        return;
    }

    self.termination_reason = reason;

    // Naive flush of the output channel
    // TODO: _dataOutputChannel.RunTick(CancellationToken.None);

    if (notify_remote and self.state == SessionState.running) {
        const disconnect = soe_packets.Disconnect{ .session_id = self.session_id, .reason = reason };
        var buffer = [soe_packets.Disconnect.SIZE]u8;
        disconnect.serialize(&buffer);
        self.sendContextualPacket(soe_protocol.SoeOpCode.disconnect, buffer);
    }

    self.state = SessionState.terminated;
    self.terminated_by_remote = terminated_by_remote;
    // TODO: _application.OnSessionClosed(reason);
    // TODO: Deregister from socket handler
}

fn sendHeartbeatIfRequired(self: *SoeSessionHandler) void {
    const maySendHeartbeat = self.mode == SessionMode.client and
        self.state == SessionState.running and
        self._session_params.heartbeat_after_ns != 0 and
        std.time.Instant.since(self._last_received_packet_tick) > self._session_params.heartbeat_after_ns;

    if (maySendHeartbeat) {
        self.sendContextualPacket(soe_protocol.SoeOpCode.heartbeat, [0]u8);
    }
}

pub const SessionMode = enum { client, server };

/// Enumerates the states that a `SoeProtocolHandler` can be in.
pub const SessionState = enum {
    /// The handler is negotiating a session.
    negotiating,
    /// The handler is waiting on the client to send a packet indicating that the session can start.
    waiting_on_client_to_open_session,
    /// The handler is running.
    running,
    /// The handler has terminated.
    terminated,
};
