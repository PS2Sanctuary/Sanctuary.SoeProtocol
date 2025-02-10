const ApplicationParams = @import("../soe_protocol.zig").ApplicationParams;
const binary_primitives = @import("../utils/binary_primitives.zig");
const BinaryWriter = @import("../utils/BinaryWriter.zig");
const pooling = @import("../pooling.zig");
const Rc4State = @import("Rc4State.zig");
const soe_packets = @import("../soe_packets.zig");
const soe_packet_utils = @import("../utils/soe_packet_utils.zig");
const soe_protocol = @import("../soe_protocol.zig");
const SoeSessionHandler = @import("../SoeSessionHandler.zig");
const std = @import("std");
const utils = @import("utils.zig");

/// Contains logic to handle reliable data packets and extract the proxied application data.
pub const ReliableDataOutputChannel = @This();

// === External private fields ===
_session_handler: *const SoeSessionHandler,
_allocator: std.mem.Allocator,
_session_params: *const soe_protocol.SessionParams,
_app_params: *const ApplicationParams,
_data_pool: *pooling.PooledDataManager,

// === Internal private fields ===
_rc4_state: ?Rc4State,
_stash: []StashedItem,
/// The next sequence number to output.
_current_sequence: i64 = 0,
/// The next reliable data sequence that we expect to receive.
_window_start_sequence: i64 = 0,
/// The maximum length of reliable data that may be sent.
_max_data_len: u32 = 0,
/// The maximum length of reliable data that may be stored in the `_multi_buffer`.
_multi_max_data_len: u32 = 0,
/// Stores the multi-buffer.
_multi_buffer: *pooling.PooledData,
/// The number of packets that have been written to the current multibuffer
_multi_buffer_count: i8 = 0,
/// The current length of the data that has been received into the `_current_buffer`.
_running_data_len: usize = 0,
/// The expected length of the data that should be received into the `_current_buffer`.
_expected_data_len: usize = 0,
/// The last reliable data sequence that we acknowledged.
_last_ack_all_seq: i64 = -1,
_last_ack_received_time: std.time.Instant,
/// The time that data was last put onto the stack
_last_data_submission_time: std.time.Instant,

// === Public fields ===
output_stats: OutputStats = OutputStats{},

pub fn init(
    max_data_size: u32,
    session_handler: *const SoeSessionHandler,
    allocator: std.mem.Allocator,
    session_params: *const soe_protocol.SessionParams,
    app_params: *const ApplicationParams,
    data_pool: *pooling.PooledDataManager,
) !ReliableDataOutputChannel {
    // Take a copy of the RC4 state
    var my_rc4_state: ?Rc4State = null;
    if (app_params.initial_rc4_state) |state| {
        my_rc4_state = state.copy();
    }

    // Pre-fill the stash
    var stash = try allocator.alloc(
        StashedItem,
        @intCast(session_params.*.max_queued_incoming_data_packets),
    );
    for (0..stash.len) |i| {
        stash[i] = StashedItem{ .data = null, .is_fragment = false };
    }

    var channel = ReliableDataOutputChannel{
        ._session_handler = session_handler,
        ._allocator = allocator,
        ._session_params = session_params,
        ._app_params = app_params,
        ._data_pool = data_pool,
        ._rc4_state = my_rc4_state,
        ._stash = stash,
        ._last_ack_received_time = try std.time.Instant.now(),
        ._multi_buffer = undefined,
        ._last_data_submission_time = try std.time.Instant.now(),
    };
    try channel.setMaxDataLength(max_data_size);

    try channel.setNewMultiBuffer();

    return channel;
}

pub fn deinit(self: *ReliableDataOutputChannel) void {
    for (self._stash) |*element| {
        if (element.data) |data| {
            data.releaseRef();
            element.data = null;
        }
    }
    self._allocator.free(self._stash);

    self._multi_buffer.releaseRef();
}

pub fn setMaxDataLength(self: *ReliableDataOutputChannel, max_data_len: u32) !void {
    if (self._current_sequence != 0) {
        @panic("The maximum length may not be changed after data has been enqueued");
    }

    self._max_data_len = max_data_len;
    // The multibuffer `data_len` includes the contextual packet header, but this is not part of the
    // reliable data and hence doesn't contribute to the `_max_data_len` limit
    self._multi_max_data_len = max_data_len + self._session_handler.contextual_header_len;
}

pub fn runTick(self: *ReliableDataOutputChannel) !void {
    const now = try std.time.Instant.now();

    if (now.since(self._last_data_submission_time) > 1 * std.time.ns_per_ms) {
        try self.flushMultiBuffer();
    }
}

pub fn sendData(self: *ReliableDataOutputChannel, data: []const u8) !void {
    self._last_data_submission_time = try std.time.Instant.now();

    // First try to write to the multibuffer. If this succeeds then we don't need to do more
    if (try self.putInMultiBuffer(data)) {
        return;
    }

    if (data.len > self._max_data_len) {
        // TODO: We need to send in fragments
    } else {
        var pool_item = try self._data_pool.get();
        pool_item.takeRef();

        var writer = BinaryWriter.init(pool_item.data);
        writer.advance(self._session_handler.contextual_header_len);
        writer.writeBytes(data);
        pool_item.data_end_idx = writer.offset + self._session_handler.contextual_trailer_len;

        // Check that this stash space hasn't been previously filled. We're running terribly behind
        // if it has been
        const stash_spot: usize = @intCast(@mod(self._current_sequence, self._session_params.max_queued_outgoing_data_packets));
        var stash_item = self._stash[stash_spot];
        if (stash_item.data != null) {
            self.output_stats.dropped_packets += 1;
            // TODO: How do we handle the fact that we're out of stash space?
        }

        // Replace the data in the stash
        stash_item.data = pool_item;
        stash_item.is_fragment = false;
        self._stash[stash_spot] = stash_item;

        self._current_sequence += 1;
    }
}

/// Attempts to put the given `data` into the multibuffer. If this could not
/// be achieved then `false` is returned and the packet should be dispatched
/// on its own.
fn putInMultiBuffer(self: *ReliableDataOutputChannel, data: []const u8) !bool {
    const total_len = data.len + soe_packet_utils.getVariableLengthSize(@intCast(data.len));

    if (total_len > self._multi_max_data_len - self._multi_buffer.data_end_idx) {
        try self.flushMultiBuffer();
    }

    // Now that we've flushed the buffer, check if we can fit again. If not, dispatch immediately
    if (total_len > self._multi_max_data_len - self._multi_buffer.data_end_idx) {
        return false;
    }

    // Write the length of the data into the multibuffer, passing a reference to the _multi_buffer_position to update
    soe_packet_utils.writeVariableLength(
        self._multi_buffer.data,
        @intCast(data.len),
        &self._multi_buffer.data_end_idx,
    );
    // Copy the data into the multibuffer
    @memcpy(
        self._multi_buffer.data[self._multi_buffer.data_end_idx .. self._multi_buffer.data_end_idx + data.len],
        data,
    );
    self._multi_buffer.data_end_idx += data.len;
    self._multi_buffer_count += 1;

    if (self._multi_buffer.data_end_idx == self._multi_max_data_len) {
        try self.flushMultiBuffer();
    }

    return true;
}

/// Prepares a pooled data object as a new `_multi_buffer`.
fn setNewMultiBuffer(self: *ReliableDataOutputChannel) !void {
    self._multi_buffer = try self._data_pool.get();
    self._multi_buffer.takeRef();
    self._multi_buffer_count = 0;

    var written: usize = self._session_handler.contextual_header_len;
    written += utils.writeMultiDataIndicator(self._multi_buffer.data[written..]);
    self._multi_buffer.data_end_idx = written;
}

fn flushMultiBuffer(self: *ReliableDataOutputChannel) !void {
    self._multi_buffer.data_end_idx += self._session_handler.contextual_trailer_len;

    // Check that this stash space hasn't been previously filled. We're running terribly behind
    // if it has been
    const stash_spot: usize = @intCast(@mod(self._current_sequence, self._session_params.max_queued_outgoing_data_packets));
    var stash_item = self._stash[stash_spot];
    if (stash_item.data != null) {
        self.output_stats.dropped_packets += 1;
        // TODO: How do we handle the fact that we're out of stash space?
    }

    // Replace the data in the stash
    stash_item.data = self._multi_buffer;
    stash_item.is_fragment = false;
    self._stash[stash_spot] = stash_item;

    self._current_sequence += 1;
    // TODO: If there is only a single packet in the multibuffer, then send it as a fragment.
    // We can do this by advancing the start of the pool buffer by the length of the multi
    // data indicator, so it gets overwritten by the underlying SOE contextual sender

    try self.setNewMultiBuffer();
}

pub const OutputStats = struct {
    /// The total number of reliable data packets (incl. fragments) that have been
    /// sent. This count includes resent packets.
    total_sent_data: u32 = 0,
    /// The total number of sequences that needed to be resent.
    resent_count: u32 = 0,
    /// The total number of data bytes received by the channel.
    total_sent_bytes: u64 = 0,
    /// The total number of receive acknowledgement packets (incl. ack all).
    total_received_acknowledgements: u32 = 0,
    /// The number of packets that have been dropped because the stash was out of space.
    dropped_packets: u32 = 0,
};

const StashedItem = struct {
    data: ?*pooling.PooledData = null,
    is_fragment: bool = false,
};

// =====
// BEGIN TESTS
// =====

pub const tests = struct {
    session_params: soe_protocol.SessionParams,
    app_params: ApplicationParams,
    pool: pooling.PooledDataManager,
    session_handler: *SoeSessionHandler,
    channel: *ReliableDataOutputChannel,

    fn init(max_data_size: u16) !*tests {
        const test_class = try std.testing.allocator.create(tests);
        test_class.session_params = soe_protocol.SessionParams{};

        test_class.pool = pooling.PooledDataManager.init(
            std.testing.allocator,
            test_class.session_params.udp_length,
            max_data_size,
        );

        test_class.app_params = ApplicationParams{
            .is_encryption_enabled = false,
            .initial_rc4_state = Rc4State.init(&[_]u8{ 0, 1, 2, 3, 4 }),
            .handle_app_data = undefined,
            .handler_ptr = undefined,
            .on_session_closed = undefined,
            .on_session_opened = undefined,
        };

        test_class.session_handler = try std.testing.allocator.create(SoeSessionHandler);
        test_class.session_handler.contextual_header_len = 2;

        test_class.channel = try std.testing.allocator.create(ReliableDataOutputChannel);
        test_class.channel.* = try ReliableDataOutputChannel.init(
            max_data_size,
            test_class.session_handler,
            std.testing.allocator,
            &test_class.session_params,
            &test_class.app_params,
            &test_class.pool,
        );

        return test_class;
    }

    fn deinit(self: *tests) void {
        self.channel.deinit();

        std.testing.allocator.destroy(self.channel);
        std.testing.allocator.destroy(self.session_handler);

        self.pool.deinit();
        std.testing.allocator.destroy(self);
    }

    test putInMultiBuffer {
        const test_class = try tests.init(15);
        defer test_class.deinit();

        var channel = test_class.channel;

        var plain_data = [_]u8{ 0, 1, 2, 3, 4 };
        const plain_data_var_len = soe_packet_utils.getVariableLengthSize(plain_data.len);
        const multi_start_index = channel._session_handler.contextual_header_len;
        const data_start_index = channel._session_handler.contextual_header_len + utils.MULTI_DATA_INDICATOR.len;

        // Assert that the multi-data-indicator has been written to the multibuffer
        try std.testing.expectEqual(data_start_index, channel._multi_buffer.data_end_idx);
        try std.testing.expectEqualSlices(
            u8,
            &utils.MULTI_DATA_INDICATOR,
            channel._multi_buffer.getSlice()[multi_start_index..],
        );

        // Test a first successful write
        var success = try channel.putInMultiBuffer(&plain_data);
        try std.testing.expect(success);
        try std.testing.expectEqual(
            data_start_index + plain_data_var_len + plain_data.len,
            channel._multi_buffer.data_end_idx,
        );
        try std.testing.expectEqualSlices(
            u8,
            utils.MULTI_DATA_INDICATOR ++ &[_]u8{5} ++ &plain_data,
            channel._multi_buffer.getSlice()[multi_start_index..],
        );

        // Test a sequential write that fits into the buffer
        success = try channel.putInMultiBuffer(&plain_data);
        try std.testing.expect(success);
        try std.testing.expectEqual(
            data_start_index + (plain_data_var_len + plain_data.len) * 2,
            channel._multi_buffer.data_end_idx,
        );
        try std.testing.expectEqualSlices(
            u8,
            utils.MULTI_DATA_INDICATOR ++ &[_]u8{5} ++ &plain_data ++ &[_]u8{5} ++ &plain_data,
            channel._multi_buffer.getSlice()[multi_start_index..],
        );

        // Test a write which will consume two bytes (one for the length indicator). This will require the
        // multibuffer to be flushed as it only has space for two more bytes. Check that this occurs
        success = try channel.putInMultiBuffer(&[_]u8{1});
        try std.testing.expect(success);
        try std.testing.expectEqual(
            data_start_index + 2,
            channel._multi_buffer.data_end_idx,
        );
        try std.testing.expectEqualSlices(
            u8,
            &utils.MULTI_DATA_INDICATOR ++ &[_]u8{ 1, 1 },
            channel._multi_buffer.getSlice()[multi_start_index..],
        );
        try std.testing.expectEqual(1, channel._current_sequence);

        // Test a write which will fill the entire buffer without overflowing. We should get a single flush
        // Our MB should be at pos 4 as of the last test, and our buffer is 15. Leaving 1 for the length indicator,
        // this means our data here should have 10 bytes
        success = try channel.putInMultiBuffer(&[_]u8{ 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        try std.testing.expect(success);
        try std.testing.expectEqual(
            data_start_index,
            channel._multi_buffer.data_end_idx,
        );
        try std.testing.expectEqualSlices(
            u8,
            &utils.MULTI_DATA_INDICATOR,
            channel._multi_buffer.getSlice()[multi_start_index..],
        );
        try std.testing.expectEqual(2, channel._current_sequence);
    }
};
