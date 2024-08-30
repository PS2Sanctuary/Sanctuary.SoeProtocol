const std = @import("std");

const SOE_PROTOCOL_VERSION: u32 = 3;
const CRC_LENGTH: u8 = 2;
const DEFAULT_UDP_LENGTH: u32 = 512;
const DEFAULT_SESSION_HEARTBEAT_AFTER_NS: i64 = std.time.ns_per_s * 25;
const DEFAULT_SESSION_INACTIVITY_TIMEOUT_NS: i64 = std.time.ns_per_s * 30;

/// Enumerates the packet OP codes used in the SOE protocol.
const SoeOpCode = enum(u16) {
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
