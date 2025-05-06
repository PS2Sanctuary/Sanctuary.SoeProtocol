const binary_primitives = @import("./utils/binary_primitives.zig");
const BinaryWriter = @import("./utils/BinaryWriter.zig");
const pooling = @import("./pooling.zig");
const ReliableDataInputChannel = @import("./reliable_data/ReliableDataInputChannel.zig");
const ReliableDataOutputChannel = @import("./reliable_data/ReliableDataOutputChannel.zig");
const soe_packets = @import("./soe_packets.zig");
const soe_packet_utils = @import("./utils/soe_packet_utils.zig");
const soe_protocol = @import("./soe_protocol.zig");
const SoeSocketHandler = @import("./SoeSocketHandler.zig");
const std = @import("std");
const udp_socket = @import("utils/udp_socket.zig");
const zlib = @import("utils/zlib.zig");

/// Manages an individual SOE session.
pub const SoeSessionHandler = @This();

// Internal static fields
var _random = std.Random.DefaultPrng.init(234029380);

// External private fields
_parent: *SoeSocketHandler,
_allocator: std.mem.Allocator,
_session_params: *soe_protocol.SessionParams,
_app_params: *const soe_protocol.ApplicationParams,
_data_pool: *pooling.PooledDataManager,

// Internal private fields
_contextual_send_buffer: []u8,
_data_input_channel: ReliableDataInputChannel,
_data_output_channel: ReliableDataOutputChannel,
_last_received_packet_tick: std.time.Instant,

// Public fields
mode: SessionMode,
remote: std.net.Address,
state: SessionState,
session_id: u32 = 0,
termination_reason: soe_protocol.DisconnectReason = .none,
terminated_by_remote: bool = false,
/// The number of bytes required to store the header of a contextual packet
contextual_header_len: u8,
/// The number of bytes required to store the trailer of a contextual packet.
contextual_trailer_len: u8,

pub fn init(
    mode: SessionMode,
    remote: std.net.Address,
    parent: *SoeSocketHandler,
    allocator: std.mem.Allocator,
    session_params: *soe_protocol.SessionParams,
    app_params: *const soe_protocol.ApplicationParams,
    data_pool: *pooling.PooledDataManager,
) !*SoeSessionHandler {
    const session_handler = try allocator.create(SoeSessionHandler);
    session_handler.* = SoeSessionHandler{
        ._allocator = allocator,
        ._app_params = app_params,
        ._contextual_send_buffer = try allocator.alloc(u8, @intCast(session_params.udp_length)),
        ._data_input_channel = undefined,
        ._data_output_channel = undefined,
        ._data_pool = data_pool,
        ._last_received_packet_tick = try std.time.Instant.now(),
        ._parent = parent,
        ._session_params = session_params,
        .contextual_header_len = @as(u8, @sizeOf(soe_protocol.SoeOpCode)) +
            @intFromBool(session_params.is_compression_enabled),
        .contextual_trailer_len = session_params.crc_length,
        .mode = mode,
        .remote = remote,
        .state = SessionState.negotiating,
    };

    const input_channel = try ReliableDataInputChannel.init(
        session_handler,
        allocator,
        session_params,
        app_params,
        data_pool,
    );
    session_handler._data_input_channel = input_channel;

    const output_channel = try ReliableDataOutputChannel.init(
        session_handler.calculateMaxDataLength(),
        session_handler,
        allocator,
        session_params,
        app_params,
        data_pool,
    );
    session_handler._data_output_channel = output_channel;

    return session_handler;
}

pub fn deinit(self: *SoeSessionHandler) void {
    self._allocator.free(self._contextual_send_buffer);
    self._data_input_channel.deinit();
    self._allocator.destroy(self);
}

pub fn runTick(self: *SoeSessionHandler) !void {
    try self.sendHeartbeatIfRequired();

    const now = try std.time.Instant.now();
    if (now.since(self._last_received_packet_tick) > self._session_params.inactivity_timeout_ns) {
        try self.terminateSession(.timeout, false, false);
    }

    try self._data_input_channel.runTick();
    try self._data_output_channel.runTick();
}

pub fn handlePacket(self: *SoeSessionHandler, packet: []u8) !void {
    var my_packet = packet;

    const op_code = soe_packet_utils.validatePacket(
        my_packet,
        self._session_params.*,
    ) catch {
        try self.terminateSession(.corrupt_packet, true, false);
        return;
    };

    if (self.state == SessionState.waiting_on_client_to_open_session) {
        self._app_params.callOnSessionOpened(self);
        self.state = SessionState.running;
    }

    // We set this after packet validation as a primitive method of stopping the connection
    // if all we've received is multiple corrupt packets in a row
    self._last_received_packet_tick = try std.time.Instant.now();
    my_packet = my_packet[@sizeOf(soe_protocol.SoeOpCode)..];
    const is_sessionless: bool = soe_packet_utils.isContextlessPacket(op_code);

    if (is_sessionless) {
        self.handleContextlessPacket(op_code, my_packet) catch {
            try self.terminateSession(.corrupt_packet, true, false);
        };
    } else {
        self.handleContextualPacket(
            op_code,
            my_packet[0 .. my_packet.len - self._session_params.crc_length],
        ) catch {
            try self.terminateSession(.corrupt_packet, true, false);
        };
    }
}

/// Sends data in a contextual packet. Header and trailer data will be appended to the data.\
/// `op_code`: The OP code of the packet to send.\
/// `packet_data`: The packet data, not including the OP code.
pub fn sendContextualPacket(self: *const SoeSessionHandler, op_code: soe_protocol.SoeOpCode, packet_data: []u8) !void {
    const total_len = packet_data.len + self.contextual_header_len + self.contextual_trailer_len;

    if (total_len > self._session_params.remote_udp_length) {
        @panic("packet_data is too long");
    }

    const dest_buffer = self._contextual_send_buffer[self.contextual_header_len .. self.contextual_header_len + packet_data.len];
    @memcpy(dest_buffer, packet_data);

    return self.sendPresizedContextualPacket(op_code, self._contextual_send_buffer[0..total_len]);
}

/// Sends a contextual packet that has already had space for its extra bytes allocated. This means the `packet_data`
/// will have padding at the start for an OP code, the compression indicator (if enabled) and at the end for the CRC bytes.
/// Use the `contextual_header_len` and `contextual_trailer_len` fields to allow for this space.\
/// `op_code`: The OP code of the packet to send.\
/// `packet_data`: The packet data, with padding for the packet header and trailer.
pub fn sendPresizedContextualPacket(self: *const SoeSessionHandler, op_code: soe_protocol.SoeOpCode, packet_data: []u8) !void {
    if (packet_data.len > self._session_params.remote_udp_length) {
        std.debug.panic("packet_data is too long (max len: {d})", .{self._session_params.remote_udp_length});
    }

    var writer = BinaryWriter.init(packet_data);

    writer.writeU16BE(@intFromEnum(op_code));
    if (self._session_params.is_compression_enabled)
        writer.writeBool(false); // Compression is not implemented at the moment
    writer.offset = packet_data.len - self._session_params.crc_length;
    soe_packet_utils.appendCrc(
        &writer,
        self._session_params.crc_seed,
        self._session_params.crc_length,
    );

    _ = self._parent.sendSessionData(self, packet_data) catch |err| {
        std.debug.panic("Failed to send contextual packet due to socket error: {any}", .{err});
    };
}

/// Terminates the session. This may be called whenever the session needs to close,
/// e.g. when the other party has disconnected, or an internal error has occurred.\
/// `reason`: The termination reason.\
/// `notify_remote`: Whether to notify the remote party.\
/// `terminated_by_remote`: Indicates whether this termination has come from the remote party.
pub fn terminateSession(
    self: *SoeSessionHandler,
    reason: soe_protocol.DisconnectReason,
    notify_remote: bool,
    terminated_by_remote: bool,
) !void {
    if (self.state == SessionState.terminated) {
        return;
    }

    self.termination_reason = reason;

    // Naive flush of the output channel
    // TODO: we should implement a proper flush method, and only run it
    // in cases where we can reasonably determine this is a 'safe' termination
    try self._data_output_channel.runTick();

    if (notify_remote and self.state == SessionState.running) {
        const disconnect = soe_packets.Disconnect{ .session_id = self.session_id, .reason = reason };
        var buffer: [soe_packets.Disconnect.SIZE]u8 = undefined;
        disconnect.serialize(&buffer);
        _ = self.sendContextualPacket(soe_protocol.SoeOpCode.disconnect, &buffer) catch |err| {
            // It's not good that we errored, but it could be a termination because of network issues
            // In any case, we need to finalise the termination rather than failing here. Remote
            // notification is only a nicety; it should timeout eventually
            std.debug.print("Failed to send disconnect packet: {}", err);
        };
    }

    self.state = SessionState.terminated;
    self.terminated_by_remote = terminated_by_remote;
    self._app_params.callOnSessionClosed(self, reason);

    // Our parent socket handler should remove us during its tick loop
}

pub fn sendHeartbeat(self: *const SoeSessionHandler) !void {
    try self.sendContextualPacket(soe_protocol.SoeOpCode.heartbeat, &[0]u8{});
}

/// Queues data to be sent on the reliable channel. The `data` may be mutated
/// if encryption is enabled.
pub fn sendReliableData(self: *SoeSessionHandler, data: []u8) !void {
    try self._data_output_channel.sendData(data);
}

pub fn getDataInputStats(self: *const SoeSessionHandler) ReliableDataInputChannel.InputStats {
    return self._data_input_channel.input_stats;
}

pub fn getDataOutputStats(self: *const SoeSessionHandler) ReliableDataOutputChannel.OutputStats {
    return self._data_output_channel.output_stats;
}

// ===== Start Contextless Packet Handling =====

/// Sends a session request to the remote. The underlying network writer must be connected,
/// and the handler must be in client mode, and ready for negotiation.
pub fn sendSessionRequest(self: *SoeSessionHandler) !void {
    if (self.state != SessionState.negotiating) {
        std.debug.panic("Can only send a session request while in the negotiating state", .{});
    }

    if (self.mode != SessionMode.client) {
        std.debug.panic("Can only send a session request while in client mode", .{});
    }

    const session_id: u32 = @truncate(_random.next());
    const sreq = soe_packets.SessionRequest{
        .application_protocol = self._session_params.application_protocol,
        .session_id = session_id,
        .soe_protocol_version = soe_protocol.SOE_PROTOCOL_VERSION,
        .udp_length = self._session_params.udp_length,
    };

    // Unfortunately we can only guarantee our UDP length here, and not the remote's
    const packet_size = sreq.getSize();
    if (packet_size > self._session_params.udp_length) {
        std.debug.panic("The application_protocol string is too long", .{});
    }

    const buffer = try self._allocator.alloc(u8, packet_size);
    defer self._allocator.free(buffer);
    sreq.serialize(buffer, true);
    _ = try self._parent.sendSessionData(self, buffer);
}

fn handleContextlessPacket(self: *SoeSessionHandler, op_code: soe_protocol.SoeOpCode, packet: []u8) !void {
    switch (op_code) {
        .session_request => try self.handleSessionRequest(packet),
        .session_response => try self.handleSessionResponse(packet),
        .unknown_sender => try self.terminateSession(.unreachable_connection, false, false),
        .remap_connection => std.debug.panic("Remap requests must be handled by the SoeSocketHandler", .{}), // TODO: we can request this
        else => std.debug.panic("The contextless handler does not support {any} packets", .{op_code}),
    }
}

fn handleSessionRequest(self: *SoeSessionHandler, packet: []u8) !void {
    if (self.mode == SessionMode.client) {
        try self.terminateSession(.connecting_to_self, false, false);
        return;
    }

    const sreq = try soe_packets.SessionRequest.deserialize(packet, false);
    self._session_params.remote_udp_length = sreq.udp_length;
    self.session_id = sreq.session_id;

    if (self.state != SessionState.negotiating) {
        try self.terminateSession(.connect_error, true, false);
        return;
    }

    const protocols_match: bool = sreq.soe_protocol_version == soe_protocol.SOE_PROTOCOL_VERSION and
        std.mem.eql(u8, sreq.application_protocol, self._session_params.application_protocol);
    if (!protocols_match) {
        try self.terminateSession(.protocol_mismatch, true, false);
        return;
    }

    self._session_params.crc_length = soe_protocol.CRC_LENGTH;
    self._session_params.crc_seed = @truncate(_random.next());

    try self._data_output_channel.setMaxDataLength(self.calculateMaxDataLength());

    const sresp = soe_packets.SessionResponse{
        .session_id = self.session_id,
        .crc_length = self._session_params.crc_length,
        .crc_seed = self._session_params.crc_seed,
        .is_compression_enabled = self._session_params.is_compression_enabled,
        .soe_protocol_version = soe_protocol.SOE_PROTOCOL_VERSION,
        .udp_length = self._session_params.udp_length,
        .unknown_value_1 = 0,
    };
    var buffer: [soe_packets.SessionResponse.SIZE]u8 = undefined;
    sresp.serialize(&buffer, true);
    _ = try self._parent.sendSessionData(self, &buffer);

    self.state = SessionState.waiting_on_client_to_open_session;
}

fn handleSessionResponse(self: *SoeSessionHandler, packet: []u8) !void {
    if (self.mode == SessionMode.server) {
        try self.terminateSession(.connecting_to_self, false, false);
        return;
    }

    // Error on deserialization bubbles up and will cause a corrupt_packet termination
    const sresp = try soe_packets.SessionResponse.deserialize(packet, false);
    self._session_params.remote_udp_length = sresp.udp_length;
    self._session_params.crc_length = sresp.crc_length;
    self._session_params.crc_seed = sresp.crc_seed;
    self._session_params.is_compression_enabled = sresp.is_compression_enabled;
    self.session_id = sresp.session_id;
    try self._data_output_channel.setMaxDataLength(self.calculateMaxDataLength());

    if (self.state != SessionState.negotiating) {
        try self.terminateSession(.connect_error, true, false);
    }

    if (sresp.soe_protocol_version != soe_protocol.SOE_PROTOCOL_VERSION) {
        try self.terminateSession(.protocol_mismatch, true, false);
        return;
    }

    self.state = SessionState.running;
    self._app_params.callOnSessionOpened(self);
}

fn calculateMaxDataLength(self: *const SoeSessionHandler) u32 {
    return self._session_params.udp_length -
        self.contextual_header_len -
        self.contextual_trailer_len;
}

// ===== End Contextless Packet Handling =====

// ===== Start Contextual Packet Handling =====

fn handleContextualPacket(self: *SoeSessionHandler, op_code: soe_protocol.SoeOpCode, packet_data: []u8) !void {
    var decompressed: ?std.ArrayList(u8) = null;
    var my_data = packet_data;

    if (self._session_params.is_compression_enabled) {
        const is_compressed: bool = my_data[0] > 0;
        my_data = my_data[1..];

        if (is_compressed) {
            decompressed = try zlib.decompress(
                self._allocator,
                self._session_params.remote_udp_length * 3,
                my_data,
            );
            my_data = decompressed.?.items;
        }
    }

    try self.handleContextualPacketInternal(op_code, my_data);

    if (decompressed) |decom_value| {
        decom_value.deinit();
    }
}

fn handleContextualPacketInternal(
    self: *SoeSessionHandler,
    op_code: soe_protocol.SoeOpCode,
    packet_data: []u8,
) !void {
    switch (op_code) {
        .multi_packet => {
            var offset: usize = 0;
            while (offset < packet_data.len) {
                const sub_packet_len: u32 = soe_packet_utils.readVariableLen(packet_data, &offset);
                if (sub_packet_len < @sizeOf(soe_protocol.SoeOpCode) or sub_packet_len > packet_data.len - offset) {
                    try self.terminateSession(.corrupt_packet, true, false);
                    return;
                }

                const sub_packet_op_code = soe_packet_utils.readSoeOpCode(packet_data[offset..]) catch {
                    try self.terminateSession(.corrupt_packet, true, false);
                    return;
                };

                try self.handleContextualPacketInternal(
                    sub_packet_op_code,
                    packet_data[offset + @sizeOf(soe_protocol.SoeOpCode) .. offset + sub_packet_len],
                );

                offset += sub_packet_len;
            }
        },
        .disconnect => {
            const disconnect = soe_packets.Disconnect.deserialize(packet_data);
            try self.terminateSession(disconnect.reason, false, true);
        },
        .heartbeat => {
            if (self.mode == SessionMode.server) {
                try self.sendContextualPacket(.heartbeat, &[0]u8{});
            }
        },
        .reliable_data => {
            try self._data_input_channel.handleReliableData(packet_data);
        },
        .reliable_data_fragment => {
            try self._data_input_channel.handleReliableDataFragment(packet_data);
        },
        .acknowledge => {
            const ack = soe_packets.Acknowledge.deserialize(packet_data);
            try self._data_output_channel.receivedAck(ack);
        },
        .acknowledge_all => {
            const ack_all = soe_packets.AcknowledgeAll.deserialize(packet_data);
            try self._data_output_channel.receivedAckAll(ack_all);
        },
        else => {
            std.debug.panic("The contextual handler does not support {any} packets", .{op_code});
        },
    }
}

fn sendHeartbeatIfRequired(self: *SoeSessionHandler) !void {
    const now = try std.time.Instant.now();

    const maySendHeartbeat = self.mode == SessionMode.client and
        self.state == SessionState.running and
        self._session_params.heartbeat_after_ns != 0 and
        now.since(self._last_received_packet_tick) > self._session_params.heartbeat_after_ns;

    if (maySendHeartbeat) {
        try self.sendHeartbeat();
    }
}

// ===== End Contextual Packet Handling =====

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
