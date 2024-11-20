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
pub const ReliableDataOutputChannel = @This();

// === External private fields ===
_session_handler: *const SoeSessionHandler,
_allocator: std.mem.Allocator,
_session_params: *const soe_protocol.SessionParams,
_app_params: *const ApplicationParams,
_data_pool: pooling.PooledDataManager,

// === Internal private fields ===
_rc4_state: ?Rc4State,
_stash: []StashedItem,
/// The next reliable data sequence that we expect to receive.
_window_start_sequence: i64 = 0,
/// Stores the multi-buffer.
_multi_buffer: []u8,
/// The current position into the multi-buffer that we have reached
_multi_buffer_position: usize,
/// The current length of the data that has been received into the `_current_buffer`.
_running_data_len: usize = 0,
/// The expected length of the data that should be received into the `_current_buffer`.
_expected_data_len: usize = 0,
/// The last reliable data sequence that we acknowledged.
_last_ack_all_seq: i64 = -1,
_last_ack_received_time: std.time.Instant,

// === Public fields ===
input_stats: OutputStats = OutputStats{},

pub fn init(
    max_data_size: u16,
    session_handler: *const SoeSessionHandler,
    allocator: std.mem.Allocator,
    session_params: *const soe_protocol.SessionParams,
    app_params: *const ApplicationParams,
    data_pool: pooling.PooledDataManager,
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
        ._multi_buffer = try allocator.alloc(u8, max_data_size),
        ._multi_buffer_position = utils.MULTI_DATA_INDICATOR.len,
    };

    channel._multi_buffer_position = utils.writeMultiDataIndicator(channel._multi_buffer);

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
    self._allocator.free(self._multi_buffer);
}

pub fn sendData(self: *ReliableDataOutputChannel, data: []const u8) !void {
    // First try to write to the multibuffer. If this succeeds then we don't need to do more
    if (self.putInMultiBuffer(data)) {
        return;
    }
}

/// Attempts to put the given `data` into the multibuffer. If this could not
/// be achieved then `false` is returned and the packet should be dispatched
/// on its own.
fn putInMultiBuffer(self: *ReliableDataOutputChannel, data: []const u8) bool {
    const total_len = data.len + soe_packet_utils.getVariableLengthSize(@intCast(data.len));

    if (total_len > self._multi_buffer.len - self._multi_buffer_position) {
        // TODO: flush the multi-buffer
        self._multi_buffer_position = utils.MULTI_DATA_INDICATOR.len;
    }

    // Now that we've flushed the buffer, check if we can fit again. If not, dispatch immediately
    if (total_len > self._multi_buffer.len - self._multi_buffer_position) {
        return false;
    }

    // Write the length of the data into the multibuffer, passing a reference to the _multi_buffer_position to update
    soe_packet_utils.writeVariableLength(
        self._multi_buffer,
        @intCast(data.len),
        &self._multi_buffer_position,
    );
    // Copy the data into the multibuffer
    @memcpy(
        self._multi_buffer[self._multi_buffer_position .. self._multi_buffer_position + data.len],
        data,
    );
    self._multi_buffer_position += data.len;

    if (self._multi_buffer_position == self._multi_buffer.len) {
        // TODO: flush the multi buffer
        self._multi_buffer_position = utils.MULTI_DATA_INDICATOR.len;
    }

    return true;
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
    app_params: *ApplicationParams,

    fn init() !*tests {
        const test_class = try std.testing.allocator.create(tests);
        test_class.session_params = soe_protocol.SessionParams{};

        test_class.app_params = try std.testing.allocator.create(ApplicationParams);
        test_class.app_params.is_encryption_enabled = false;
        test_class.app_params.initial_rc4_state = Rc4State.init(&[_]u8{ 0, 1, 2, 3, 4 });

        return test_class;
    }

    fn deinit(self: *tests) void {
        std.testing.allocator.destroy(self.app_params);
        std.testing.allocator.destroy(self);
    }

    test putInMultiBuffer {
        var plain_data = [_]u8{ 0, 1, 2, 3, 4 };
        const plain_data_var_len = soe_packet_utils.getVariableLengthSize(plain_data.len);

        const test_class = try tests.init();
        defer test_class.deinit();

        var channel = try test_class.getChannel(15);
        defer channel.deinit();

        // Assert that the multi-data-indicator has been written to the multibuffer
        try std.testing.expectEqual(utils.MULTI_DATA_INDICATOR.len, channel._multi_buffer_position);
        try std.testing.expectEqualSlices(
            u8,
            &utils.MULTI_DATA_INDICATOR,
            channel._multi_buffer[0..utils.MULTI_DATA_INDICATOR.len],
        );

        // Test a first successful write
        var success = channel.putInMultiBuffer(&plain_data);
        try std.testing.expect(success);
        try std.testing.expectEqual(
            utils.MULTI_DATA_INDICATOR.len + plain_data_var_len + plain_data.len,
            channel._multi_buffer_position,
        );
        try std.testing.expectEqualSlices(
            u8,
            utils.MULTI_DATA_INDICATOR ++ &[_]u8{5} ++ &plain_data,
            channel._multi_buffer[0..channel._multi_buffer_position],
        );

        // Test a sequential write that fits into the buffer
        success = channel.putInMultiBuffer(&plain_data);
        try std.testing.expect(success);
        try std.testing.expectEqual(
            utils.MULTI_DATA_INDICATOR.len + (plain_data_var_len + plain_data.len) * 2,
            channel._multi_buffer_position,
        );
        try std.testing.expectEqualSlices(
            u8,
            utils.MULTI_DATA_INDICATOR ++ &[_]u8{5} ++ &plain_data ++ &[_]u8{5} ++ &plain_data,
            channel._multi_buffer[0..channel._multi_buffer_position],
        );

        // Test a write which will consume two bytes (one for the length indicator). This will require the
        // multibuffer to be flushed as it only has space for two more bytes. Check that this occurs
        success = channel.putInMultiBuffer(&[_]u8{1});
        try std.testing.expect(success);
        try std.testing.expectEqual(
            utils.MULTI_DATA_INDICATOR.len + 2,
            channel._multi_buffer_position,
        );
        try std.testing.expectEqualSlices(
            u8,
            &utils.MULTI_DATA_INDICATOR ++ &[_]u8{ 1, 1 },
            channel._multi_buffer[0..channel._multi_buffer_position],
        );
        // TODO: Test we got a good flush

        // Test a write which will fill the entire buffer without overflowing. We should get a single flush
        // Our MB should be a pos 4 as of the last test, and our buffer is 15. Leaving 1 for the length indicator,
        // this means our data here should have 10 bytes
        success = channel.putInMultiBuffer(&[_]u8{ 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        try std.testing.expect(success);
        try std.testing.expectEqual(
            utils.MULTI_DATA_INDICATOR.len,
            channel._multi_buffer_position,
        );
        try std.testing.expectEqualSlices(
            u8,
            &utils.MULTI_DATA_INDICATOR,
            channel._multi_buffer[0..channel._multi_buffer_position],
        );
        // TODO: Test we got a good flush
    }

    fn getChannel(self: tests, max_data_size: u16) !ReliableDataOutputChannel {
        return try ReliableDataOutputChannel.init(
            max_data_size,
            undefined,
            std.testing.allocator,
            &self.session_params,
            self.app_params,
            pooling.PooledDataManager.init(
                std.testing.allocator,
                self.session_params.udp_length,
                max_data_size,
            ),
        );
    }
};
