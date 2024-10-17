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
    self.input_stats.total_received_data += 1;
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
fn isValidReliableData(
    self: *ReliableDataInputChannel,
    data: []const u8,
    sequence: *i64,
    packet_sequence: i16,
) bool {
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

fn processData(self: *ReliableDataInputChannel, data: []u8) void {
    if (utils.hasMultiData(data)) {
        var offset: usize = 2;
        while (offset < data.len) {
            const length = utils.readVariableLength(data, &offset);
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
    self._app_params.handle_app_data(self._app_params.handler_ptr, my_data);
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
    data: *pooling.PooledData,
    is_fragment: bool,
};

// =====
// BEGIN TESTS
// =====

pub const tests = struct {
    session_params: SessionParams = .{},
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

    fn receiveData(ptr: *anyopaque, data: []const u8) void {
        const self: *tests = @ptrCast(@alignCast(ptr));
        self.last_received_data = data;
        self.received_data_queue.append(data) catch @panic("Failed to add to queue");
    }

    fn getChannel(self: tests) !ReliableDataInputChannel {
        return try ReliableDataInputChannel.init(
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
