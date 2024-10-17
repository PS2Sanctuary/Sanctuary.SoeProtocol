const ApplicationParams = @import("../soe_protocol.zig").ApplicationParams;
const binary_primitives = @import("../utils/binary_primitives.zig");
const pooling = @import("../pooling.zig");
const Rc4State = @import("Rc4State.zig");
const SessionParams = @import("../soe_protocol.zig").SessionParams;
const soe_packets = @import("../soe_packets.zig");
const std = @import("std");
const utils = @import("utils.zig");

/// Contains logic to handle reliable data packets and extract the proxied application data.
pub const ReliableDataInputChannel = @This();

/// Gets the maximum length of time that data may go un-acknowledged.
const MAX_ACK_DELAY_NS = std.time.ns_per_ms * 30;

// External private fields
_allocator: std.mem.Allocator,
_session_params: *const SessionParams,
_app_params: *const ApplicationParams,
_data_pool: pooling.PooledDataManager,

// Internal private fields
_rc4_state: ?Rc4State,
_stash: []StashedItem,
_window_start_sequence: i64 = 0,
_buffered_ack_all: ?soe_packets.AcknowledgeAll = undefined,

// Public fields
input_stats: InputStats = InputStats{},

pub fn init(
    allocator: std.mem.Allocator,
    session_params: *const SessionParams,
    app_params: *const ApplicationParams,
    data_pool: pooling.PooledDataManager,
) !ReliableDataInputChannel {
    var my_rc4_state: ?Rc4State = null;
    if (app_params.initial_rc4_state) |state| {
        my_rc4_state = state.copy();
    }

    return ReliableDataInputChannel{
        ._allocator = allocator,
        ._session_params = session_params,
        ._app_params = app_params,
        ._data_pool = data_pool,
        ._rc4_state = my_rc4_state,
        ._stash = try allocator.alloc(
            StashedItem,
            session_params.*.max_queued_incoming_data_packets,
        ),
    };
}

pub fn deinit(self: *ReliableDataInputChannel) void {
    self._allocator.free(self._stash);
}

pub fn runTick(self: *ReliableDataInputChannel) void {
    if (self._buffered_ack_all) |ack_all| {
        std.debug.print(ack_all.sequence); // Just here for compilation
        // TODO: First off, we should send any waiting ack alls
    }

    // TODO: send an ack if necessary
}

/// Handles reliable data.
pub fn handleReliableData(self: *ReliableDataInputChannel, data: []u8) void {
    if (!self.preprocessData(data, false)) {
        return;
    }

    self.processData(data);
    // TODO: consumed any stashed fragments
}

/// handles a reliable data fragment.
pub fn handleReliableDataFragment(self: *ReliableDataInputChannel, data: []u8) void {
    if (!self.preprocessData(data, true)) {
        return;
    }

    // TODO: Write fragment to the buffer, try and process the current buffer, consume any stashed fragments
}

/// Pre-processes reliable data, and stashes it for future processing if required.
/// Returns `true` if the data may be processed immediately.
fn preprocessData(self: *ReliableDataInputChannel, data: *[]u8, is_fragment: bool) bool {
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
        std.debug.print(ack.sequence); // Just here for compilation
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

    // Grab our stash item. We may have already stashed this packet ahead of time, so check for that
    var stash_item = self._stash[stash_spot];
    if (stash_item.data != null) {
        self.input_stats.duplicate_count += 1;
        return false;
    }

    // Create some pooled data and copy our packet in
    var pool_item = self._data_pool.get();
    pool_item.takeRef();
    @memcpy(pool_item.data, data);

    // Update our stash item
    stash_item.is_fragment = is_fragment;
    stash_item.data.* = pool_item;
    return false;
}

/// Checks whether the given reliable data is valid for processing, by ensuring that it is within
/// the current window. If we've already processed it, this method queues an ack all.
/// The `sequence` and `packet_sequence` parameters will be populated.
fn isValidReliableData(self: *ReliableDataInputChannel, data: []const u8, sequence: *i64, packet_sequence: i16) bool {
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

fn processData(self: *ReliableDataInputChannel, data_ptr: *[]u8) void {
    const data = data_ptr.*;

    if (utils.hasMultiData(data)) {
        var offset: usize = 2;
        while (offset < data.len) {
            const length = utils.readVariableLength(data, &offset);
            self.decryptAndCallHandler(&data[offset .. offset + length]);
            offset += length;
        }
    } else {
        self.decryptAndCallHandler(data_ptr);
    }
}

fn decryptAndCallHandler(self: *ReliableDataInputChannel, data_ptr: *[]u8) void {
    var data = data_ptr.*;

    if (self._app_params.is_encryption_enabled) {
        // A single 0x00 byte may be used to prefix encrypted data. We must ignore it
        if (data.len > 1 and data[0] == 0) {
            data = data[1..];
        }
        // We can assume the key state is present, as encryption is enabled
        self._rc4_state.?.transform(data, data);
    }

    self.input_stats.total_received_bytes += data.len;
    self._app_params.handle_app_data(self._app_params.handler_ptr, data_ptr.*);
}

const InputStats = struct {
    total_received: u32 = 0,
    duplicate_count: u32 = 0,
    out_of_order_count: u32 = 0,
    total_received_bytes: u64 = 0,
    acknowledge_count: u32 = 0,
};

const StashedItem = struct {
    data: *pooling.PooledData,
    is_fragment: bool,
};

// =====
// BEGIN TESTS
// =====

pub const tests = struct {
    session_params: SessionParams = .{},
    app_params: ApplicationParams,
    last_received_data: []const u8 = undefined,

    fn init() !*tests {
        const test_class = try std.testing.allocator.create(tests);

        test_class.app_params = ApplicationParams{
            .is_encryption_enabled = true,
            .initial_rc4_state = Rc4State.init(&[_]u8{ 0, 1, 2, 3, 4 }),
            .handler_ptr = test_class,
            .handle_app_data = receiveData,
            .on_session_closed = undefined,
            .on_session_opened = undefined,
        };

        return test_class;
    }

    test decryptAndCallHandler {
        var data = [_]u8{ 0, 1, 2, 3, 4 };
        const test_class = try tests.init();
        defer std.testing.allocator.destroy(test_class);

        // Start with no encryption
        test_class.app_params.is_encryption_enabled = false;
        var channel = try test_class.getChannel();
        defer channel.deinit();

        var data2: []u8 = &data;
        channel.decryptAndCallHandler(&data2);
        try std.testing.expectEqual(data.len, channel.input_stats.total_received_bytes);
        try std.testing.expectEqualSlices(u8, &data, test_class.last_received_data);
    }

    fn receiveData(ptr: *anyopaque, data: []const u8) void {
        const self: *tests = @ptrCast(@alignCast(ptr));
        self.last_received_data = data;
    }

    fn getChannel(self: tests) !ReliableDataInputChannel {
        return try ReliableDataInputChannel.init(
            std.testing.allocator,
            &self.session_params,
            &self.app_params,
            pooling.PooledDataManager.init(
                std.testing.allocator,
                self.session_params.udp_length,
                512,
            ),
        );
    }
};
