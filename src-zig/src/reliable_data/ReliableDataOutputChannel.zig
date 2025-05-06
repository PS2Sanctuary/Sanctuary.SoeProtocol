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
/// The index into the `_multi_buffer` at which the data of the first item starts.
/// Used so we can flush a multi buffer with only one item as a standard data packet.
_multi_first_data_offset: usize = 0,

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

/// Sets the maximum length of data that the channel may dispatch into an SOE reliable data packet.
pub fn setMaxDataLength(self: *ReliableDataOutputChannel, max_data_len: u32) !void {
    if (self._current_sequence != 0) {
        @panic("The maximum length may not be changed after data has been enqueued");
    }

    self._max_data_len = max_data_len;
    // The multibuffer `data_len` includes space for the contextual packet header and reliable sequence,
    // but these are not part of the reliable data and hence don't contribute to the `_max_data_len` limit
    self._multi_max_data_len = max_data_len + self._session_handler.contextual_header_len + @sizeOf(u16);
}

pub fn runTick(self: *ReliableDataOutputChannel) !void {
    const now = try std.time.Instant.now();

    if (now.since(self._last_data_submission_time) > 1 * std.time.ns_per_ms) {
        try self.flushMultiBuffer(); // TODO: We probably need a lock around this. And a lock around stashing
    }

    // TODO: Complete implementation
}

/// Queues data to be sent on the reliable channel. The `data` may be mutated
/// if encryption is enabled.
pub fn sendData(self: *ReliableDataOutputChannel, data: []u8) !void {
    self._last_data_submission_time = try std.time.Instant.now();
    self.output_stats.total_sent_bytes = data.len;

    var my_data: []u8 = data;
    var needs_free: bool = false;
    if (self._app_params.is_encryption_enabled) {
        const enc_result = try self.encryptReliableData(my_data);
        my_data = enc_result[0];
        needs_free = enc_result[1];
    }

    // First try to write to the multibuffer. If this succeeds then we don't need to do more.
    // Otherwise we flush the multibuffer to retain packet ordering, and stash the data.
    if (try self.putInMultiBuffer(my_data)) {
        return;
    } else {
        try self.flushMultiBuffer();
    }

    try self.stashFragment(&my_data, true);
    while (my_data.len > 0) {
        try self.stashFragment(&my_data, false);
    }

    if (needs_free) {
        self._allocator.free(my_data);
    }
}

/// Notifies the output channel of an acknowledge packet.
pub fn receivedAck(self: *ReliableDataOutputChannel, ack: soe_packets.Acknowledge) !void {
    const seq = utils.getTrueIncomingSequence(
        ack.sequence,
        self._window_start_sequence,
        self._session_params.max_queued_outgoing_data_packets,
    );
    try self.processAck(seq);
}

/// Notifies the output channel of an acknowledge-all packet.
pub fn receivedAckAll(self: *ReliableDataOutputChannel, ack: soe_packets.AcknowledgeAll) !void {
    const seq = utils.getTrueIncomingSequence(
        ack.sequence,
        self._window_start_sequence,
        self._session_params.max_queued_outgoing_data_packets,
    );

    var i = self._window_start_sequence;
    while (i <= seq) : (i += 1) {
        try self.processAck(i);
    }
}

fn processAck(self: *ReliableDataOutputChannel, sequence: i64) !void {
    var stash_index = self.getStashIndex(sequence);
    var stashed_item = self._stash[stash_index];

    if (stashed_item.data) |has_data| {
        has_data.releaseRef();
        stashed_item.data = null;
    }
    self._stash[stash_index] = stashed_item;

    // Walk the window forward to either the _current_sequence, or the next un-acked packet
    while (self._window_start_sequence < self._current_sequence) {
        stash_index = self.getStashIndex(self._window_start_sequence);
        if (self._stash[stash_index].data) |_| {
            break;
        } else {
            self._window_start_sequence += 1;
        }
    }

    self._last_ack_received_time = try std.time.Instant.now();
}

fn stashFragment(self: *ReliableDataOutputChannel, remaining_data: *[]u8, is_master: bool) !void {
    var pool_item = try self._data_pool.get();
    pool_item.takeRef();

    var writer = BinaryWriter.init(pool_item.data);
    // We reserve space for the contextual header len as we call SoeSessionHandler.sendPresizedContextualPacket
    // We also advance past the reliable sequence, as the call to stashPackedData will write it
    writer.advance(self._session_handler.contextual_header_len + @sizeOf(u16));

    // Either store what we have left, or take the max data we can, less the sequence marker
    // If we need to store fragments and this is the master packet, also set space for the data len
    var is_fragment = false;
    var amount_to_take = @min(remaining_data.len, self._max_data_len - @sizeOf(u16));
    if (amount_to_take > remaining_data.len and is_master) {
        writer.writeU32BE(@truncate(remaining_data.len));
        amount_to_take -= @sizeOf(u32);
        is_fragment = true;
    }

    // Write our data, and shorten the remaining
    writer.writeBytes(remaining_data.*[0..amount_to_take]);
    remaining_data.* = remaining_data.*[amount_to_take..];

    try self.stashPackedData(pool_item, is_fragment);
}

/// Places pre-packed pooled data into the stash, and writes the reliable sequence.
fn stashPackedData(self: *ReliableDataOutputChannel, packed_data: *pooling.PooledData, is_fragment: bool) !void {
    binary_primitives.writeU16BE(
        packed_data.getSlice()[self._session_handler.contextual_header_len..],
        @intCast(self._current_sequence),
    );

    // Check that this stash space hasn't been previously filled. We're running terribly behind
    // if it has been
    const stash_spot = self.getStashIndex(self._current_sequence);
    var stash_item = self._stash[stash_spot];
    if (stash_item.data != null) {
        self.output_stats.dropped_packets += 1;
        // TODO: How best do we handle the fact that we're out of stash space?
        return error.OutOfStashSpace;
    }

    // Replace the data in the stash
    stash_item.data = packed_data;
    stash_item.is_fragment = is_fragment;
    self._stash[stash_spot] = stash_item;

    self._current_sequence += 1;
}

fn encryptReliableData(self: *ReliableDataOutputChannel, data: []u8) !struct { []u8, bool } {
    // We can assume the key state is present, as encryption is enabled
    self._rc4_state.?.transform(data, data);

    var my_data = data;
    var needs_free = false;
    // Encrypted blocks that begin with zero must have another zero prefixed
    if (my_data[0] == 0) {
        my_data = try self._allocator.alloc(u8, data.len + 1);
        @memcpy(my_data[1..], data);
        my_data[0] = 0;
        needs_free = true;
    }

    return .{ my_data, needs_free };
}

/// Attempts to put the given `data` into the multibuffer. If this could not
/// be achieved then `false` is returned and the packet should be dispatched
/// on its own.
fn putInMultiBuffer(self: *ReliableDataOutputChannel, data: []const u8) !bool {
    const total_len = data.len + utils.getMultiDataLenSize(@intCast(data.len));

    if (total_len > self._multi_max_data_len - self._multi_buffer.data_end_idx) {
        try self.flushMultiBuffer();
    }

    // Now that we've flushed the buffer, check if we can fit again. If not, dispatch immediately
    if (total_len > self._multi_max_data_len - self._multi_buffer.data_end_idx) {
        return false;
    }

    // Write the length of the data into the multibuffer, passing a reference to the _multi_buffer_position to update
    utils.writeMultiDataLen(
        self._multi_buffer.data,
        @intCast(data.len),
        &self._multi_buffer.data_end_idx,
    );
    if (self._multi_buffer_count == 0) {
        self._multi_first_data_offset = self._multi_buffer.data_end_idx;
    }
    self._multi_buffer.appendData(data);
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
    self._multi_first_data_offset = 0;

    // Leave space for the contextual header and reliable sequence to be written
    var written: usize = self._session_handler.contextual_header_len + @sizeOf(u16);
    written += utils.writeMultiDataIndicator(self._multi_buffer.data[written..]);
    self._multi_buffer.data_end_idx = written;
}

fn flushMultiBuffer(self: *ReliableDataOutputChannel) !void {
    if (self._multi_buffer_count == 0) {
        return;
    }

    self._multi_buffer.data_end_idx += self._session_handler.contextual_trailer_len;

    // There is only a single packet in the multibuffer so we can send it directly as a fragment
    // We can do this by advancing the start of the pool buffer to the start of the first item in
    // the buffer, but leaving space for the SOE contextual sender
    if (self._multi_buffer_count == 1) {
        self._multi_buffer.data_start_idx = self._multi_first_data_offset -
            self._session_handler.contextual_header_len -
            @sizeOf(u16); // reliable sequence
    }

    try self.stashPackedData(self._multi_buffer, false);

    try self.setNewMultiBuffer();
}

/// Gets the index in the `_stash` that a particular reliable sequence should be placed.
fn getStashIndex(self: @This(), sequence: i64) usize {
    return @intCast(@mod(sequence, self._session_params.max_queued_outgoing_data_packets));
}

pub const OutputStats = struct {
    /// The total number of reliable data packets (incl. fragments) that have been
    /// sent. This count includes resent packets.
    total_sent_data: u32 = 0,
    /// The total number of sequences that needed to be resent.
    resent_count: u32 = 0,
    /// The total number of data bytes sent by the channel. This count excludes header/trailer data and does not include resent data.
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
        test_class.session_handler.contextual_trailer_len = 2;

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

    test getStashIndex {
        const test_class = try tests.init(15);
        defer test_class.deinit();

        try std.testing.expectEqual(1, test_class.channel.getStashIndex(1));
        try std.testing.expectEqual(
            0,
            test_class.channel.getStashIndex(test_class.session_params.max_queued_outgoing_data_packets),
        );
    }

    test putInMultiBuffer {
        const max_reliable_data_len = 15;
        const test_class = try tests.init(max_reliable_data_len);
        defer test_class.deinit();

        var channel = test_class.channel;
        const contextual_header = [_]u8{ 0xAA, 0xAA }; // 2 bytes of SOE code, no compression indicator
        const contextual_trailer = [_]u8{ 0xAA, 0xAA }; // 2 bytes of CRC

        var plain_data = [_]u8{ 0, 1, 2, 3, 4 };

        const plain_data_var_len = utils.getMultiDataLenSize(plain_data.len);
        // N.B. @sizeOf(u16) represents the reliable sequence
        const multi_start_index = channel._session_handler.contextual_header_len + @sizeOf(u16);
        const data_start_index = channel._session_handler.contextual_header_len + @sizeOf(u16) + utils.MULTI_DATA_INDICATOR.len;

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
        try std.testing.expectEqual(1, channel._multi_buffer_count);
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
        try std.testing.expectEqual(2, channel._multi_buffer_count);
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
        // multibuffer to be flushed as it only has space for one more byte. Check that this occurs
        success = try channel.putInMultiBuffer(&[_]u8{1});
        try std.testing.expect(success);
        try std.testing.expectEqual(1, channel._multi_buffer_count);
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

        // Test that the multibuffer was flushed correctly
        try std.testing.expectEqualSlices(
            u8,
            &contextual_header ++ // Space for the contextual header, 0xAA as this is how Zig stores uninitialized memory
                &[_]u8{ 0, 0 } ++ // Reliable sequence
                utils.MULTI_DATA_INDICATOR ++
                &[_]u8{5} ++ // First data length
                &plain_data ++
                &[_]u8{5} ++ // Second data length
                &plain_data ++
                &contextual_trailer,
            channel._stash[0].data.?.getSlice(),
        );

        // Test a write which will fill the entire buffer without overflowing. We should get a single flush
        // Our MB should be at pos 4 as of the last test, and our buffer is 15. Leaving 1 for the length indicator,
        // this means our data here should have 10 bytes
        const long_addition = [_]u8{ 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        success = try channel.putInMultiBuffer(&long_addition);
        try std.testing.expect(success);
        try std.testing.expectEqual(0, channel._multi_buffer_count);
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

        // Test that the multibuffer was flushed correctly
        try std.testing.expectEqualSlices(
            u8,
            &contextual_header ++ // Space for the contextual header, 0xAA as this is how Zig stores uninitialized memory
                &[_]u8{ 0, 1 } ++ // Reliable sequence
                utils.MULTI_DATA_INDICATOR ++
                &[_]u8{ 1, 1 } ++ // The single byte we submitted earlier, and its length indicator
                &[_]u8{10} ++ // Len of long addition
                &long_addition ++
                &contextual_trailer,
            channel._stash[1].data.?.getSlice(),
        );

        // Now test a write that will fill the buffer and result in a flush of a single packet
        // To make the fifteen that counts in the buffer, we include the multi data indicator and the length indicator,
        // allowing us 12 bytes of data
        const fill_the_multibuffer = [_]u8{ 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        success = try channel.putInMultiBuffer(&fill_the_multibuffer);
        try std.testing.expect(success);
        try std.testing.expectEqual(0, channel._multi_buffer_count);
        try std.testing.expectEqual(3, channel._current_sequence);
        // Test that the multibuffer was flushed correctly. No multidata-specific additions should be present
        try std.testing.expectEqualSlices(
            u8,
            &[_]u8{ 0xAA, 0x00 } ++ // In practice, this will be overwritten with the contextual header!
                &[_]u8{ 0, 2 } ++ // Reliable sequence
                &fill_the_multibuffer ++
                &contextual_trailer,
            channel._stash[2].data.?.getSlice(),
        );

        // Ensure the method rejects data that is too long to fit
        success = try channel.putInMultiBuffer(&[_]u8{ 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });
        try std.testing.expect(!success);
        try std.testing.expectEqual(0, channel._multi_buffer_count);
    }
};
