const ApplicationParams = @import("../soe_protocol.zig").ApplicationParams;
const binary_primitives = @import("../utils/binary_primitives.zig");
const pooling = @import("../pooling.zig");
const Rc4State = @import("Rc4State.zig");
const soe_packets = @import("../soe_packets.zig");
const soe_packet_utils = @import("../utils/soe_packet_utils.zig");
const soe_protocol = @import("../soe_protocol.zig");
const SoeSessionHandler = @import("../SoeSessionHandler.zig");
const std = @import("std");
const utils = @import("utils.zig");

/// Contains logic to handle reliable data packets and extract the proxied application data.
pub const ReliableDataInputChannel = @This();

/// Gets the maximum length of time that data may go un-acknowledged.
const MAX_ACK_DELAY_NS = std.time.ns_per_ms * 30;

// External private fields
_session_handler: *const SoeSessionHandler,
_allocator: std.mem.Allocator,
_session_params: *const soe_protocol.SessionParams,
_app_params: *const ApplicationParams,
_data_pool: pooling.PooledDataManager,

// Internal private fields
_rc4_state: ?Rc4State,
_stash: []StashedItem,
_window_start_sequence: i64 = 0,
_buffered_ack_all: ?soe_packets.AcknowledgeAll = null,
_current_buffer: ?[]u8 = null,
_running_data_len: usize = 0,
_expected_data_len: usize = 0,
_last_ack_all_seq: i64 = -1,
_last_ack_all_time: std.time.Instant,

// Public fields
input_stats: InputStats = InputStats{},

pub fn init(
    session_handler: *const SoeSessionHandler,
    allocator: std.mem.Allocator,
    session_params: *const soe_protocol.SessionParams,
    app_params: *const ApplicationParams,
    data_pool: pooling.PooledDataManager,
) !ReliableDataInputChannel {
    var my_rc4_state: ?Rc4State = null;
    if (app_params.initial_rc4_state) |state| {
        my_rc4_state = state.copy();
    }

    return ReliableDataInputChannel{
        ._session_handler = session_handler,
        ._allocator = allocator,
        ._session_params = session_params,
        ._app_params = app_params,
        ._data_pool = data_pool,
        ._rc4_state = my_rc4_state,
        ._stash = try allocator.alloc(
            StashedItem,
            @intCast(session_params.*.max_queued_incoming_data_packets),
        ),
        ._last_ack_all_time = try std.time.Instant.now(),
    };
}

pub fn deinit(self: *ReliableDataInputChannel) void {
    for (self._stash) |element| {
        if (element.data) |data| {
            data.releaseRef();
            element.data = null;
        }
    }

    self._allocator.free(self._stash);

    if (self._current_buffer) |current_buffer| {
        self._allocator.free(current_buffer);
    }
}

/// Runs a single tick of operations for the ReliableDataInputChannel. This includes
/// acknowledging processed data packets.
pub fn runTick(self: *ReliableDataInputChannel) void {
    if (self._buffered_ack_all) |ack_all| {
        self.sendAckAll(ack_all);
        self._buffered_ack_all = null;
    }

    const to_ack = self._window_start_sequence - 1;

    // No need to perform an ack all if we're acking everything individually
    // or we've already acked up to the current window start sequence
    if (self._session_params.acknowledge_all_data or to_ack <= self._last_ack_all_seq) {
        return;
    }

    const now = try std.time.Instant.now();
    // ack if:
    // - at least 30ms have passed since the last ack time and
    // - our seq to ack is greater than the last ack seq + half of the ack window
    const need_ack = now.since(self._last_ack_all_time) > 30 * std.time.ns_per_ms or
        to_ack >= self._last_ack_all_seq + self._session_params.data_ack_window / 2;

    if (need_ack) {
        const ack_all = soe_packets.AcknowledgeAll{ .sequence = to_ack };
        self.sendAckAll(ack_all);
    }
}

/// Handles reliable data.
pub fn handleReliableData(self: *ReliableDataInputChannel, data: []u8) void {
    var my_data = data;

    if (!self.preprocessData(&my_data, false)) {
        return;
    }

    self.processData(my_data);
    self.consumeStashedDataFragments();
}

/// Handles a reliable data fragment.
pub fn handleReliableDataFragment(self: *ReliableDataInputChannel, data: []u8) !void {
    var my_data = data;

    if (!self.preprocessData(&my_data, true)) {
        return;
    }

    // At this point we know this fragment can be written directly to the buffer
    // as it is next in the sequence.
    try self.writeImmediateFragmentToBuffer(my_data);

    // Attempt to process the current buffer now, as the stashed fragments may belong to a new buffer
    // consumeStashedDataFragments will attempt to process the current buffer as it releases stashes
    self.tryProcessCurrentBuffer();
    self.consumeStashedDataFragments();
}

fn sendAckAll(self: *ReliableDataInputChannel, ack_all: soe_packets.AcknowledgeAll) !void {
    // Serialize and send the packet
    const buffer: [soe_packets.AcknowledgeAll.SIZE]u8 = undefined;
    ack_all.serialize(buffer);
    try self._session_handler.sendContextualPacket(.acknowledge_all, buffer);

    // Update our last ack sequence and time
    self._last_ack_all_seq = ack_all.sequence;
    self._last_ack_all_time = try std.time.Instant.now();
}

/// Pre-processes reliable data, and stashes it for future processing if required.
/// Returns `true` if the data may be processed immediately.
fn preprocessData(self: *ReliableDataInputChannel, data: *[]u8, is_fragment: bool) bool {
    self.input_stats.total_received_data += 1;
    var sequence: i64 = undefined;
    var packet_sequence: u16 = undefined;

    if (!self.isValidReliableData(data.*, &sequence, &packet_sequence)) {
        return false;
    }

    const ahead = sequence != self._window_start_sequence;

    // Ack this data if we are in ack-all mode or it is ahead of our expectations
    if (self._session_params.acknowledge_all_data or ahead) {
        const ack = soe_packets.Acknowledge{ .sequence = packet_sequence };
        var buffer: [soe_packets.Acknowledge.SIZE]u8 = undefined;
        ack.serialize(&buffer);
        try self._session_handler.sendContextualPacket(.acknowledge, buffer);
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
    var pool_item = try self._data_pool.get();
    pool_item.takeRef();
    @memcpy(pool_item.data, data);

    // Update our stash item
    stash_item.is_fragment = is_fragment;
    stash_item.data = pool_item;
    return false;
}

/// Checks whether the given reliable data is valid for processing, by ensuring that it is within
/// the current window. If we've already processed it, this method queues an ack all.
/// The `sequence` and `packet_sequence` parameters will be populated.
fn isValidReliableData(
    self: *ReliableDataInputChannel,
    data: []const u8,
    sequence: *i64,
    packet_sequence: *u16,
) bool {
    packet_sequence.* = binary_primitives.readU16BE(data);
    sequence.* = utils.getTrueIncomingSequence(
        packet_sequence.*,
        self._window_start_sequence,
        self._session_params.max_queued_incoming_data_packets,
    );

    // If this is too far ahead of our window, just drop it
    if (sequence.* > self._window_start_sequence + self._session_params.max_queued_incoming_data_packets) {
        return false;
    }

    // Great, we're inside the window
    if (sequence.* >= self._window_start_sequence) {
        return true;
    }

    // We've already seen this packet. Ack all and mark as duplicate
    self.input_stats.duplicate_count += 1;
    self._buffered_ack_all = soe_packets.AcknowledgeAll{ .sequence = self._window_start_sequence - 1 };
    return false;
}

/// Writes a fragment to the `_current_buffer`. If the `_current_buffer` is not allocated,
/// the fragment in the `buffer` will be assumed to be a master fragment (i.e. has the len
/// of the full data packet) and a new `_current_buffer` will be alloc'ed to this len.
fn writeImmediateFragmentToBuffer(self: *ReliableDataInputChannel, buffer: []const u8) !void {
    if (self._current_buffer) |current_buffer| {
        // If a current data buffer exists, copy the fragment into the buffer
        @memcpy(current_buffer[self._running_data_len..], buffer);
        self._running_data_len += buffer.len;
    } else {
        // Otherwise, create a new buffer by assuming this is a master fragment and reading
        // the length
        self._expected_data_len = binary_primitives.readU32BE(buffer);
        self._current_buffer = try self._allocator.alloc(u8, self._expected_data_len);
        self._running_data_len = buffer.len - @sizeOf(u32);
        @memcpy(
            self._current_buffer.?[0..self._running_data_len],
            buffer[@sizeOf(u32)..],
        );
    }
}

fn tryProcessCurrentBuffer(self: *ReliableDataInputChannel) void {
    if (self._current_buffer == null or self._running_data_len < self._expected_data_len) {
        return;
    }

    // Process the buffer, free it, and reset fields
    self.processData(self._current_buffer.?);
    self._allocator.free(self._current_buffer.?);
    self._current_buffer = null;
    self._running_data_len = 0;
    self._expected_data_len = 0;
}

fn consumeStashedDataFragments(self: *ReliableDataInputChannel) void {
    // Grab the stash index of our current window start sequence
    var stash_spot: usize = @intCast(@mod(self._window_start_sequence, self._session_params.max_queued_incoming_data_packets));
    var stashed_item = self._stash[stash_spot];

    // Iterate through the stash until we reach an empty slot
    while (stashed_item.data) |pooled_data| {
        if (stashed_item.is_fragment) {
            self.processData(pooled_data.getSlice());
        } else {
            self.writeImmediateFragmentToBuffer(pooled_data.getSlice());
            self.tryProcessCurrentBuffer();
        }

        // Release our stash reference
        stashed_item.data.releaseRef();
        stashed_item.data = null;

        // Increment the window
        self._window_start_sequence += 1;
        stash_spot = @intCast(@mod(self._window_start_sequence, self._session_params.max_queued_incoming_data_packets));
        stashed_item = self._stash[stash_spot];
    }
}

fn processData(self: *ReliableDataInputChannel, data: []u8) void {
    if (utils.hasMultiData(data)) {
        var offset: usize = 2;
        while (offset < data.len) {
            const length = soe_packet_utils.readVariableLength(data, &offset);
            self.decryptAndCallHandler(data[offset .. offset + length]);
            offset += length;
        }
    } else {
        self.decryptAndCallHandler(data);
    }
}

fn decryptAndCallHandler(self: *ReliableDataInputChannel, data: []u8) void {
    var my_data = data;

    if (self._app_params.is_encryption_enabled) {
        // A single 0x00 byte may be used to prefix encrypted data. We must ignore it
        if (my_data.len > 1 and my_data[0] == 0) {
            my_data = my_data[1..];
        }
        // We can assume the key state is present, as encryption is enabled
        self._rc4_state.?.transform(my_data, my_data);
    }

    self.input_stats.total_received_bytes += my_data.len;
    self._app_params.callHandleAppData(my_data);
}

const InputStats = struct {
    /// The total number of reliable data packets (incl. fragments) that have been
    /// received. This count includes duplicate packets.
    total_received_data: u32 = 0,
    /// The total number of duplicate sequences that have been received.
    duplicate_count: u32 = 0,
    /// The total number of sequences that have been received out-of-order.
    out_of_order_count: u32 = 0,
    /// The total number of data bytes received by the channel
    total_received_bytes: u64 = 0,
    acknowledge_count: u32 = 0,
};

const StashedItem = struct {
    data: ?*pooling.PooledData,
    is_fragment: bool,
};

// =====
// BEGIN TESTS
// =====

pub const tests = struct {
    session_params: soe_protocol.SessionParams = soe_protocol.SessionParams{},
    app_params: *ApplicationParams,
    last_received_data: []const u8 = undefined,
    received_data_queue: std.ArrayList([]const u8),

    fn init() !*tests {
        const test_class = try std.testing.allocator.create(tests);
        test_class.received_data_queue = std.ArrayList([]const u8).init(std.testing.allocator);

        test_class.app_params = try std.testing.allocator.create(ApplicationParams);
        test_class.app_params.is_encryption_enabled = false;
        test_class.app_params.initial_rc4_state = Rc4State.init(&[_]u8{ 0, 1, 2, 3, 4 });
        test_class.app_params.handler_ptr = test_class;
        test_class.app_params.handle_app_data = receiveData;
        test_class.app_params.on_session_closed = undefined;
        test_class.app_params.on_session_opened = undefined;

        return test_class;
    }

    fn deinit(self: *tests) void {
        self.received_data_queue.deinit();
        std.testing.allocator.destroy(self.app_params);
        std.testing.allocator.destroy(self);
    }

    test processData {
        var plain_data = [_]u8{ 0, 1, 2, 3, 4 };

        const test_class = try tests.init();
        defer test_class.deinit();

        var channel = try test_class.getChannel();
        defer channel.deinit();

        var input_data: []u8 = &plain_data;
        channel.processData(input_data);
        try std.testing.expectEqualSlices(
            u8,
            &plain_data,
            test_class.received_data_queue.orderedRemove(0),
        );

        var multi_data = utils.MULTI_DATA_INDICATOR ++ [_]u8{ 1, 99, 2, 255, 2 };
        input_data = &multi_data;
        channel.processData(input_data);

        try std.testing.expectEqualSlices(
            u8,
            &[_]u8{99},
            test_class.received_data_queue.orderedRemove(0),
        );

        try std.testing.expectEqualSlices(
            u8,
            &[_]u8{ 255, 2 },
            test_class.received_data_queue.orderedRemove(0),
        );
    }

    test "decryptAndCallHandler_NoEncryption" {
        var expected_data = [_]u8{ 0, 1, 2, 3, 4 };
        var processed_data = [_]u8{ 0, 1, 2, 3, 4 };

        const test_class = try tests.init();
        defer test_class.deinit();

        // Get a channel and disable encryption
        var channel = try test_class.getChannel();
        defer channel.deinit();
        test_class.app_params.is_encryption_enabled = false;

        const input_data: []u8 = &processed_data;
        channel.decryptAndCallHandler(input_data);
        try std.testing.expectEqual(
            expected_data.len,
            channel.input_stats.total_received_bytes,
        );
        try std.testing.expectEqualSlices(
            u8,
            &expected_data,
            test_class.last_received_data,
        );
    }

    test "decryptAndCallHandler_Encryption" {
        // The leading zero on our encrypted data should get stripped
        var expected_data = [_]u8{ 1, 2, 3, 4 };
        var processed_data = [_]u8{ 0, 1, 2, 3, 4 };

        const test_class = try tests.init();
        defer test_class.deinit();

        // Get a channel and enable encryption
        var channel = try test_class.getChannel();
        defer channel.deinit();
        test_class.app_params.is_encryption_enabled = true;

        // Let's transform our expected data
        var my_state = channel._rc4_state.?.copy();
        my_state.transform(&expected_data, &expected_data);

        const input_data: []u8 = &processed_data;
        channel.decryptAndCallHandler(input_data);
        try std.testing.expectEqual(
            expected_data.len,
            channel.input_stats.total_received_bytes,
        );
        try std.testing.expectEqualSlices(
            u8,
            &expected_data,
            test_class.last_received_data,
        );
    }

    test "decryptAndCallHandler_IncrementsTotalReceivedBytes" {
        var data_1 = [_]u8{ 0, 1, 2, 3, 4 };
        var data_2 = [_]u8{ 1, 2, 3, 4 };

        const test_class = try tests.init();
        defer test_class.deinit();

        var channel = try test_class.getChannel();
        defer channel.deinit();
        test_class.app_params.is_encryption_enabled = false;

        var input_data: []u8 = &data_1;
        channel.decryptAndCallHandler(input_data);
        try std.testing.expectEqual(
            5,
            channel.input_stats.total_received_bytes,
        );

        input_data = &data_2;
        channel.decryptAndCallHandler(input_data);
        try std.testing.expectEqual(
            9,
            channel.input_stats.total_received_bytes,
        );

        test_class.app_params.is_encryption_enabled = true;
        input_data = &data_1;
        channel.decryptAndCallHandler(input_data);
        try std.testing.expectEqual(
            13,
            channel.input_stats.total_received_bytes,
        );
    }

    test writeImmediateFragmentToBuffer {
        const data_1 = [_]u8{ 0, 0, 0, 9, 0, 1, 2, 3, 4 };
        const data_2 = [_]u8{ 1, 2, 3, 4 };

        const test_class = try tests.init();
        defer test_class.deinit();
        var channel = try test_class.getChannel();
        defer channel.deinit();

        try channel.writeImmediateFragmentToBuffer(&data_1);
        try std.testing.expectEqual(9, channel._expected_data_len);
        try std.testing.expectEqual(5, channel._running_data_len);
        try std.testing.expectEqualSlices(u8, data_1[4..], channel._current_buffer.?[0..5]);

        try channel.writeImmediateFragmentToBuffer(&data_2);
        try std.testing.expectEqual(9, channel._expected_data_len);
        try std.testing.expectEqual(9, channel._running_data_len);
        try std.testing.expectEqualSlices(u8, data_1[4..] ++ data_2, channel._current_buffer.?);
    }

    fn receiveData(ptr: *anyopaque, data: []const u8) void {
        const self: *tests = @ptrCast(@alignCast(ptr));
        self.last_received_data = data;
        self.received_data_queue.append(data) catch @panic("Failed to add to queue");
    }

    fn getChannel(self: tests) !ReliableDataInputChannel {
        return try ReliableDataInputChannel.init(
            undefined,
            std.testing.allocator,
            &self.session_params,
            self.app_params,
            pooling.PooledDataManager.init(
                std.testing.allocator,
                self.session_params.udp_length,
                512,
            ),
        );
    }
};
