/// Enumerates the packet OP codes used in the SOE protocol.
#[repr(u16)]
#[derive(Copy, Clone, Debug, Eq, FromPrimitive, PartialEq, ToPrimitive)]
pub enum SoeOpCode {
    /// Represents an invalid packet. Note that this is not part of the protocol specification.
    Invalid = 0,

    /// Used to request the start of a session.
    SessionRequest = 0x01,

    /// Used to confirm the start of a session, and set connection details.
    SessionResponse = 0x02,

    /// Use to encapsulate two or more SOE protocol packets.
    MultiPacket = 0x03,

    /// Used to indicate that a party is closing the session.
    Disconnect = 0x05,

    /// Used to keep a session alive, when no data has been receiving by either party
    /// for some time.
    Heartbeat = 0x06,

    /// It is not entirely clear how this packet type is utilised.
    NetStatusRequest = 0x07,

    /// It is not entirely clear how this packet type is utilised.
    NetStatusResponse = 0x08,

    /// Used to transfer small buffers of application data.
    ReliableData = 0x09,

    /// Used to transfer large buffers of application data in multiple fragments.
    ReliableDataFragment = 0x0D,

    /// Used to indicate that a data sequence was received out-of-order.
    OutOfOrder = 0x11,

    /// Used to acknowledge that a data sequence has been received.
    Acknowledge = 0x15,

    /// Used to indicate that the receiving party does not have a session
    /// associated with the sender's address.
    UnknownSender = 0x1D,

    /// Used to request that a session be remapped to another port.
    RemapConnection = 0x1E,
}
