﻿namespace Sanctuary.SoeProtocol;

/// <summary>
/// Enumerates the packet OP codes used in the SOE protocol.
/// </summary>
public enum SoeOpCode : ushort
{
    /// <summary>
    /// Represents an invalid packet. Note that this is not part of the protocol specification.
    /// </summary>
    Invalid = 0,

    /// <summary>
    /// Used to request the start of a session.
    /// </summary>
    SessionRequest = 0x01,

    /// <summary>
    /// Used to confirm the start of a session, and set connection details.
    /// </summary>
    SessionResponse = 0x02,

    /// <summary>
    /// Use to encapsulate two or more SOE protocol packets.
    /// </summary>
    MultiPacket = 0x03,

    /// <summary>
    /// Used to indicate that a party is closing the session.
    /// </summary>
    Disconnect = 0x05,

    /// <summary>
    /// Used to keep a session alive, when no data has been receiving by either party
    /// for some time.
    /// </summary>
    Heartbeat = 0x06,

    /// <summary>
    /// It is not entirely clear how this packet type is utilised.
    /// </summary>
    NetStatusRequest = 0x07,

    /// <summary>
    /// It is not entirely clear how this packet type is utilised.
    /// </summary>
    NetStatusResponse = 0x08,

    /// <summary>
    /// Used to transfer small buffers of application data.
    /// </summary>
    ReliableData = 0x09,

    /// <summary>
    /// Used to transfer large buffers of application data in multiple fragments.
    /// </summary>
    ReliableDataFragment = 0x0D,

    /// <summary>
    /// Used to acknowledge a single reliable data packet.
    /// </summary>
    Acknowledge = 0x11,

    /// <summary>
    /// Used to acknowledge all reliable data packets up to a particular sequence.
    /// </summary>
    AcknowledgeAll = 0x15,

    /// <summary>
    /// Used to indicate that the receiving party does not have a session
    /// associated with the sender's address.
    /// </summary>
    UnknownSender = 0x1D,

    /// <summary>
    /// Used to request that a session be remapped to another port.
    /// </summary>
    RemapConnection = 0x1E
}
