pub const Rc4State = @import("reliable_data/Rc4State.zig");
pub const soe_packets = @import("soe_packets.zig");
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

/// Bundles parameters used to control a session.
pub const SessionParams = struct {
    /// The application protocol being proxied by this session.
    application_protocol: [:0]u8 = undefined,
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
    max_outgoing_data_queue_time_ms: i32 = 50,
};

/// Parameters used by an application to control the underlying SOE session.
pub const ApplicationParams = struct {
    /// Whether the application data should be encrypted.
    is_encryption_enabled: bool,
    /// The initial encryption state to use with the session.
    initial_rc4_state: Rc4State,
    on_session_opened: fn () void,
    handle_app_data: fn ([]const u8) void,
    on_session_closed: fn (soe_packets.DisconnectReason) void,
};
