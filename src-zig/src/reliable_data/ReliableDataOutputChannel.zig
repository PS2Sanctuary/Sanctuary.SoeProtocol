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

    if (self._current_buffer) |current_buffer| {
        self._allocator.free(current_buffer);
    }
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
fn putInMultiBuffer(self: *ReliableDataOutputChannel, data: []const u8) !bool {
    const total_len = data.len + soe_packet_utils.getVariableLengthSize(data.len);

    if (total_len > self._multi_buffer.len - self._multi_buffer_position) {
        // TODO: flush the multi-buffer
        self._multi_buffer_position = utils.MULTI_DATA_INDICATOR.len;
        return false;
    }

    // Write the length of the data into the multibuffer, passing a reference to the _multi_buffer_position to update
    soe_packet_utils.writeVariableLength(self._multi_buffer, data.len, &self._multi_buffer_position);
    // Copy the data into the multibuffer
    @memcpy(self._multi_buffer[self._multi_buffer_position .. self._multi_buffer_position + data.len], data);
    self._multi_buffer_position += total_len;

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
