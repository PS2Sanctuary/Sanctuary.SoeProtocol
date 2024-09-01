const ApplicationParams = @import("../soe_protocol.zig").ApplicationParams;
const binary_primitives = @import("../utils/binary_primitives.zig");
const Rc4State = @import("Rc4State.zig");
const SessionParams = @import("../soe_protocol.zig").SessionParams;
const soe_packets = @import("../soe_packets.zig");
const std = @import("std");
const utils = @import("utils.zig");

/// Contains logic to handle reliable data packets and extract the proxied application data.
pub const ReliableDataInputChannel = @This();

/// Gets the maximum length of time that data may go un-acknowledged.
const MAX_ACK_DELAY_NS = std.time.ns_per_ms * 30;

// Private properties
_session_params: *const SessionParams,
_app_params: *const ApplicationParams,
_rc4_state: Rc4State,
_window_start_sequence: i64,
_buffered_ack_all: ?soe_packets.AcknowledgeAll,

// Public properties
input_stats: InputStats = InputStats{},

pub fn runTick(self: @This()) void {
    if (self._buffered_ack_all) |ack_all| {
        // TODO: First off, we should send any waiting ack alls
    }
}

/// Pre-processes reliable data, and stashes it for future processing if required.
/// Returns `true` if the data may be processed immediately.
fn preprocessData(self: @This(), data: *[]u8, is_fragment: bool) bool {
    self.input_stats.total_received += 1;
    var sequence: i64 = undefined;
    var packet_sequence: u16 = undefined;

    if (!self.isValidReliableData(data.*, &sequence, &packet_sequence)) {
        return false;
    }

    const ahead = sequence != self._window_start_sequence;

    // Ack this data if we are in ack-all mode or it is ahead of our expectations
    if (self._session_params.acknowledge_all_data || ahead) {
        const ack = soe_packets.Acknowledge{ .sequence = sequence };
        // TODO: Ack this single packet
    }

    // Remove the sequence bytes
    data.* = data.*[@sizeOf(u16)..];

    // We can process this immediately.
    if (!ahead) {
        return true;
    }

    // We've received this data out-of-order, so stash it
    self.input_stats.out_of_order_count += 1;
    const stash_spot = sequence % self._session_params.max_queued_incoming_data_packets;
    // TODO: Stash it!
    return false;
}

/// Checks whether the given reliable data is valid for processing, by ensuring that it is within
/// the current window. If we've already processed it, this method queues an ack all.
/// The `sequence` and `packet_sequence` parameters will be populated.
fn isValidReliableData(self: @This(), data: []const u8, sequence: *i64, packet_sequence: i16) bool {
    packet_sequence.* = binary_primitives.readU16BE(data);
    sequence.* = utils.getTrueIncomingSequence(
        packet_sequence,
        self._window_start_sequence,
        self._session_params.max_queued_incoming_data_packets,
    );

    // If this is too far ahead of our window, just drop it
    if (sequence > self._window_start_sequence + self._session_params.max_queued_incoming_data_packets) {
        return false;
    }

    // Great, we're inside the window
    if (sequence >= self._window_start_sequence) {
        return true;
    }

    // We've already seen this packet. Ack all and mark as duplicate
    self.input_stats.duplicate_count += 1;
    self._buffered_ack_all = soe_packets.AcknowledgeAll{ .sequence = self._window_start_sequence - 1 };
    return false;
}

const InputStats = struct {
    total_received: u32,
    duplicate_count: u32,
    out_of_order_count: u32,
    total_received_bytes: u64,
    acknowledge_count: u32,
};
