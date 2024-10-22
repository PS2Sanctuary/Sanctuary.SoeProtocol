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
remote: network.EndPoint,
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
        .remote = remote,
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
        self.terminateSession(.timeout, false, false);
    }

    self._data_input_channel.runTick();
}

pub fn handlePacket(self: *SoeSessionHandler, packet: []u8) !void {
    const op_code = soe_packet_utils.validatePacket(
        packet,
        self._session_params,
    ) catch {
        self.terminateSession(.corrupt_packet, true, false);
        return;
    };

    if (self.state == SessionState.waiting_on_client_to_open_session) {
        self._app_params.callOnSessionOpened();
        self.state == SessionState.running;
    }

    // We set this after packet validation as a primitive method of stopping the connection
    // if all we've received is multiple corrupt packets in a row
    self._last_received_packet_tick = std.time.Instant.now();
    packet = packet[@sizeOf(soe_protocol.SoeOpCode)..];
    const is_sessionless: bool = soe_packet_utils.isContextlessPacket(op_code);

    if (is_sessionless) {
        self.handleContextlessPacket(op_code, packet);
    } else {
        self.handleContextualPacket(op_code, packet[0 .. packet.len - self._session_params.CrcLength]);
    }
}

/// Sends a contextual packet.\
/// `op_code`: The OP code of the packet to send.\
/// `packet_data`: The packet data, not including the OP code.
pub fn sendContextualPacket(self: *SoeSessionHandler, op_code: soe_protocol.SoeOpCode, packet_data: []u8) !void {
    const extra_bytes: i32 = @sizeOf(soe_protocol.SoeOpCode) +
        @intFromBool(self._session_params.is_compression_enabled) +
        self._session_params.crc_length;

    if (packet_data.len + extra_bytes > self._session_params.remote_udp_length) {
        std.debug.panic("packet_data is too long (max len: {d})", .{self._session_params.remote_udp_length - extra_bytes});
    }

    var writer = BinaryWriter.init(self._contextual_send_buffer);

    writer.writeU16BE(@intFromEnum(op_code));
    if (self._session_params.is_compression_enabled)
        writer.writeBool(false); // Compression is not implemented at the moment
    writer.writeBytes(packet_data);
    soe_packet_utils.appendCrc(&writer, self._session_params.crc_seed, self._session_params.crc_length);

    self._parent.sendSessionData(self, writer.getConsumed()) catch |err| {
        std.debug.panic("Failed to send contextual packet due to socket error: {s}", .{err});
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
) void {
    if (self.state == SessionState.terminated) {
        return;
    }

    self.termination_reason = reason;

    // Naive flush of the output channel
    // TODO: _dataOutputChannel.RunTick(CancellationToken.None);

    if (notify_remote and self.state == SessionState.running) {
        const disconnect = soe_packets.Disconnect{ .session_id = self.session_id, .reason = reason };
        var buffer: [soe_packets.Disconnect.SIZE]u8 = undefined;
        disconnect.serialize(&buffer);
        self.sendContextualPacket(soe_protocol.SoeOpCode.disconnect, buffer);
    }

    self.state = SessionState.terminated;
    self.terminated_by_remote = terminated_by_remote;
    self._app_params.callOnSessionClosed(reason);

    // Remove ourselves from the socket handler
    self._parent.terminateSession(self);
}

// ===== Start Contextless Packet Handling =====

/// Sends a session request to the remote. The underlying network writer must be connected,
/// and the handler must be in client mode, and ready for negotiation.
fn sendSessionRequest(self: *SoeSessionHandler) void {
    if (self.state != SessionState.negotiating) {
        std.debug.panic("Can only send a session request while in the negotiating state", .{});
    }

    if (self.mode != SessionMode.client) {
        std.debug.panic("Can only send a session request while in client mode", .{});
    }

    const session_id: u32 = std.Random.DefaultPrng.random().int(u32);
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

    var buffer: [packet_size]u8 = undefined;
    sreq.serialize(buffer, true);
    self._parent.sendSessionData(self, &buffer);
}

fn handleContextlessPacket(self: *SoeSessionHandler, op_code: soe_protocol.SoeOpCode, packet: []u8) void {
    switch (op_code) {
        .session_request => handleSessionRequest(packet),
        .session_response => handleSessionResponse(packet),
        .unknown_sender => self.terminateSession(.unreachable_connection, false, false),
        .remap_connection => std.debug.panic("Remap requests must be handled by the SoeSocketHandler"), // TODO: we can request this
        _ => std.debug.panic("{any} is not a contextless packet", .{op_code}),
    }
}

fn handleSessionRequest(self: *SoeSessionHandler, packet: []u8) void {
    if (self.mode == SessionMode.client) {
        self.terminateSession(.connecting_to_self, false, false);
        return;
    }

    const sreq = soe_packets.SessionRequest.deserialize(packet, false);
    self._session_params.remote_udp_length = sreq.udp_length;
    self.session_id = sreq.session_id;

    if (self.state != SessionState.negotiating) {
        self.terminateSession(.connect_error, true, false);
        return;
    }

    const protocols_match: bool = sreq.soe_protocol_version == soe_protocol.SOE_PROTOCOL_VERSION and
        sreq.application_protocol == self._session_params.application_protocol;
    if (!protocols_match) {
        self.terminateSession(.protocol_mismatch, true, false);
        return;
    }

    self._session_params.crc_length = soe_protocol.CRC_LENGTH;
    self._session_params.crc_seed = std.Random.DefaultPrng.random().int(u32);

    // TODO: self._data_output_channel.setMaxDataLength(calculateMaxDataLength());

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
    self._parent.sendSessionData(self, &buffer);

    self.state = SessionState.waiting_on_client_to_open_session;
}

fn handleSessionResponse(self: *SoeSessionHandler, packet: []u8) void {
    if (self.mode == SessionMode.server) {
        self.terminateSession(.connecting_to_self, false, false);
        return;
    }

    const sresp = soe_packets.SessionResponse.deserialize(packet, false);
    self._session_params.remote_udp_length = sresp.udp_length;
    self._session_params.crc_length = sresp.crc_length;
    self._session_params.crc_seed = sresp.crc_seed;
    self._session_params.is_compression_enabled = sresp.is_compression_enabled;
    self.session_id = sresp.session_id;
    // TODO: self._data_output_channel.setMaxDataLength(calculateMaxDataLength());

    if (self.state != SessionState.negotiating) {
        self.terminateSession(.connect_error, true, false);
    }

    if (sresp.soe_protocol_version != soe_protocol.SOE_PROTOCOL_VERSION) {
        self.terminateSession(.protocol_mismatch, true, false);
        return;
    }

    self.state = SessionState.running;
    self._app_params.callOnSessionOpened();
}

// ===== End Contextless Packet Handling =====

// ===== Start Contextual Packet Handling =====

fn handleContextualPacket(self: *SoeSessionHandler, op_code: soe_protocol.SoeOpCode, packet_data: []u8) void {
    // TODO: MemoryStream? decompressedData = null;

    if (self._session_params.is_compression_enabled) {
        const is_compressed: bool = packet_data[0] > 0;
        packet_data = packet_data[1..];

        if (is_compressed) {
            // decompressedData = SoePacketUtils.Decompress(packetData);
            // packetData = decompressedData.GetBuffer()
            //     .AsSpan(0, (int)decompressedData.Length);
        }
    }

    self.handleContextualPacketInternal(op_code, packet_data);
    //decompressedData?.Dispose();
}

fn handleContextualPacketInternal(
    self: *SoeSessionHandler,
    op_code: soe_protocol.SoeOpCode,
    packet_data: []u8,
) void {
    switch (op_code) {
        .multi_packet => {
            var offset: usize = 0;
            while (offset < packet_data.len) {
                const sub_packet_len: i32 = soe_packet_utils.readVariableLength(packet_data, &offset);
                if (sub_packet_len < @sizeOf(soe_protocol.SoeOpCode) or sub_packet_len > packet_data.len - offset) {
                    self.terminateSession(.corrupt_packet, true, false);
                    return;
                }

                const sub_packet_op_code = soe_packet_utils.readSoeOpCode(packet_data[offset..]) catch {
                    self.terminateSession(.corrupt_packet, true, false);
                    return;
                };

                self.handleContextualPacketInternal(
                    sub_packet_op_code,
                    packet_data[offset + @sizeOf(soe_protocol.SoeOpCode) .. offset + sub_packet_len],
                );

                offset += sub_packet_len;
            }
        },
        .disconnect => {
            const disconnect = soe_packets.Disconnect.deserialize(packet_data);
            self.terminateSession(disconnect.reason, false, true);
        },
        .heartbeat => {
            if (self.mode == SessionMode.server)
                self.sendContextualPacket(.Heartbeat, [0]u8);
        },
        .reliable_data => {
            self._data_input_channel.handleReliableData(packet_data);
        },
        .reliable_data_fragment => {
            self._data_input_channel.handleReliableDataFragment(packet_data);
        },
        .acknowledge => {
            //const ack = soe_packets.Acknowledge.deserialize(packet_data);
            // TODO: _dataOutputChannel.NotifyOfAcknowledge(ack);
        },
        .acknowledge_all => {
            //const ackAll = soe_packets.AcknowledgeAll.deserialize(packet_data);
            // TODO: _dataOutputChannel.NotifyOfAcknowledgeAll(ackAll);
        },
        _ => {
            std.debug.panic("The contextual handler does not support {any} packets", .{op_code});
        },
    }
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
