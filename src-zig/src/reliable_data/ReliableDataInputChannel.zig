const ApplicationParams = @import("../soe_protocol.zig").ApplicationParams;
const Rc4State = @import("Rc4State.zig");
const SessionParams = @import("../soe_protocol.zig").SessionParams;
const std = @import("std");

/// Contains logic to handle reliable data packets and extract the proxied application data.
pub const ReliableDataInputChannel = @This();

/// Gets the maximum length of time that data may go un-acknowledged.
const MAX_ACK_DELAY_NS = std.time.ns_per_ms * 30;

_session_params: *const SessionParams,
_app_params: *const ApplicationParams,
_rc4_state: Rc4State,
