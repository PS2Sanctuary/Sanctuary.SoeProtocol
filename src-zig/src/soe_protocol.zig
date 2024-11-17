pub const Rc4State = @import("reliable_data/Rc4State.zig");
pub const SoeSessionHandler = @import("./SoeSessionHandler.zig");
pub const std = @import("std");

/// The implemented version of the SOE protocol.
pub const SOE_PROTOCOL_VERSION: u32 = 3;
/// The number of bytes used to store the CRC check value of a packet.
pub const CRC_LENGTH: u8 = 2;
/// The default maximum packet length.
pub const DEFAULT_UDP_LENGTH: u32 = 512;

/// Enumerates the packet OP codes used in the SOE protocol.
pub const SoeOpCode = enum(u16) {
    /// Used to request the start of a session.
    session_request = 0x01,
    /// Used to confirm the start of a session, and set connection details.
    session_response = 0x02,
    /// Use to encapsulate two or more SOE protocol packets.
    multi_packet = 0x03,
    /// Used to indicate that a paarty is closing the session.
    disconnect = 0x05,
    /// Used to keep a session alive, when no data has been received by either party for some time.
    heartbeat = 0x06,
    /// Used to transfer small buffers of application data.
    reliable_data = 0x09,
    /// Used to transfer large buffers of application data in multiple fragments.
    reliable_data_fragment = 0x0D,
    /// Used to acknowledge a single reliable data packet.
    acknowledge = 0x11,
    /// Used to acknowledge all reliable data packets up to a particular sequence.
    acknowledge_all = 0x15,
    /// Used to indicate that the receiving party does not have a session associated with the sender's address
    unknown_sender = 0x1D,
    /// Used to request that a session be remapped to another port.
    remap_connection = 0x1E,
};

/// Enumerates the possible session termination codes.
pub const DisconnectReason = enum(u16) {
    /// No termination has occured yet.
    none = 0,
    /// An ICMP error occured, forcing the disconnect.
    icmp_error = 1,
    /// The other party has let the session become inactive.
    timeout = 2,
    /// An internal use code, used to indicate that the other party has sent a disconnect.
    other_side_terminated = 3,
    /// Indicates that the session manager has been disposed of.
    /// Generally occurs when the server/client is shutting down.
    manager_deleted = 4,
    /// An internal use code, indicating a session request attempt has failed.
    connect_fail = 5,
    /// The application is terminating the session.
    application = 6,
    /// An internal use code, indicating that the session must disconnect
    /// as the other party is unreachable.
    unreachable_connection = 7,
    /// Indicates that the session has been closed because a data sequence
    /// was not acknowledged quickly enough.
    unacknowledged_timeout = 8,
    /// Indicates that a session request has failed (often due to the connecting
    /// party attempting a reconnection too quickly), and a new attempt should be
    /// made after a short delay.
    new_connection_attempt = 9,
    /// Indicates that the application did not accept a session request.
    connection_refused = 10,
    /// Indicates that the proper session negotiation flow has not been observed.
    connect_error = 11,
    /// Indicates that a session request has probably been looped back to the sender,
    /// and it should not continue with the connection attempt.
    connecting_to_self = 12,
    /// Indicates that reliable data is being sent too fast to be processed.
    reliable_overflow = 13,
    /// Indicates that the session manager has been orphaned by the application.
    application_released = 14,
    /// Indicates that a corrupt packet was received.
    corrupt_packet = 15,
    /// Indicates that the requested SOE protocol version or application protocol is invalid.
    protocol_mismatch = 16,
};

/// Bundles parameters used to control a session.
pub const SessionParams = struct {
    /// The application protocol being proxied by this session.
    application_protocol: [:0]const u8 = undefined,
    /// The maximum length of a UDP packet that this party can send or receive.
    udp_length: u32 = DEFAULT_UDP_LENGTH,
    /// The maximum length of a UDP packet that the other party can send or receive.
    remote_udp_length: u32 = 0,
    /// The seed used to calculate packet CRC hashes.
    crc_seed: u32 = 0,
    /// The numer of bytes used to store a packet CRC hash. Must be between 0 and 4, inclusive.
    crc_length: u8 = CRC_LENGTH,
    /// Whether compression is enabled for the session.
    is_compression_enabled: bool = false,
    /// The maximum number of data fragments that may be queued for either stitching or dispatch.
    max_queued_incoming_data_packets: i16 = 400,
    /// The maximum number of reliable data fragments that may be queued for output.
    max_queued_outgoing_data_packets: i16 = 400,
    /// The maximum number of reliable packets that may be received before sending an acknowledgement.
    data_ack_window: i16 = 32,
    /// The timespan in nanoseconds after which to send a heartbeat, if no contextual
    /// packets have been received within the interval. Set to `0` to disable heartbeating.
    /// Defaults to 25sec.
    heartbeat_after_ns: i64 = std.time.ns_per_s * 25,
    /// The timespan in nanoseconds after which to consider the session inactive, if no
    /// contextual packets have been received with the interval. Set to `0` to prevent
    /// a session from being terminated due to inactivity. Defaults to 30sec.
    inactivity_timeout_ns: i64 = std.time.ns_per_s * 30,
    /// Whether all data packets should be acknowledged.
    acknowledge_all_data: bool = false,
    /// The maximum amount of time that outgoing data should be held, in the hopes of being
    /// able to bundle multiple small data into a multi-data packet. This is specified in
    /// nanoseconds. Set to `0` to immediately release outgoing data.
    max_outgoing_data_queue_time_ns: i32 = std.time.ns_per_ms * 50,
};

/// Parameters used by an application to control the underlying SOE session.
pub const ApplicationParams = struct {
    /// Whether the application data should be encrypted.
    is_encryption_enabled: bool,
    /// The initial encryption state to use with the session.
    initial_rc4_state: ?Rc4State,
    /// A pointer to the object implementing the `handle_app_data`, `on_session_opened`
    /// and `on_session_closed` methods
    handler_ptr: *anyopaque,
    on_session_opened: *const fn (self: *anyopaque, session: *const SoeSessionHandler) void,
    handle_app_data: *const fn (
        self: *anyopaque,
        session: *const SoeSessionHandler,
        data: []const u8,
    ) void,
    on_session_closed: *const fn (
        self: *anyopaque,
        session: *const SoeSessionHandler,
        disconnect_reason: DisconnectReason,
    ) void,

    pub fn callOnSessionOpened(self: *const ApplicationParams, session: *const SoeSessionHandler) void {
        self.on_session_opened(self.handler_ptr, session);
    }

    pub fn callHandleAppData(
        self: *const ApplicationParams,
        session: *const SoeSessionHandler,
        data: []const u8,
    ) void {
        self.handle_app_data(self.handler_ptr, session, data);
    }

    pub fn callOnSessionClosed(
        self: *const ApplicationParams,
        session: *const SoeSessionHandler,
        reason: DisconnectReason,
    ) void {
        self.on_session_closed(self.handler_ptr, session, reason);
    }
};
