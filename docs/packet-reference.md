# Packet Reference

### SessionRequest (0x01)

Session request packets request the start of an SOE protocol session.

```csharp
struct SessionRequest
{
    uint SoeProtocolVersion; // The version of the SOE protocol that is in use. Version 3 is documented here.
    uint SessionId; // A randomly generated session idenfiier.
    uint UdpLength; // The maximum length of a UDP packet that the sender can receive.
    string ApplicationProtocol; // A null-terminated descriptor of the application protocol that the sender wishes to transport.
}
```

### Session Response (0x02)

Session response packets confirm the creation of a session.

```csharp
struct SessionResponse
{
    uint SessionId; // The ID of the requested session that is being confirmed.
    uint CrcSeed; // A randomly generated seed used to calculate the CRC-32 check value on relevant packets.
    byte CrcLength; // The number of bytes that should be used to encode the CRC-32 check value on relevant packets.
    bool IsCompressionEnabled; // Indicates whether relevant packets may be compressed.
    byte UnknownValue1; // Unknown. Always observed to be 0. Possibly a flag for the initial encryption status of app data.
    uint UdpLength; // The maximum length of a UDP packet that the sender can receive.
    uint SoeProtocolVersion; // The version of the SOE protocol that is in use. Version 3 is documented here.
}
```

### MultiPacket (0x03)

MultiPackets are used to wrap many smaller SOE packets before they are sent. This is often utilised in
the middle of data transmission, where a party wants to send a `ReliableData` packet and simultaneously
`Acknowledge` data from the other party.

> **Note**: typically, when a MultiPacket is carrying multiple of the same sub-packet, compression will be used.

A MultiPacket contains no data other than its sub-packets, which are placed back-to-back and prefixed by
their lengths, encoded using variable-size integers.
See [Appendix B](./appendix.md#b-reading-and-writing-multipacket-variable-size-integers) for more info.
Sub-packets may not be compressed and hence should omit the compression flag. Further, sub-packets should not
include a CRC check value.

To read a MultiPacket, one should loop until the entire packet has been consumed. Each iteration,
a variable-size integer should be read, which indicates the amount of data to read in order to
extract the next sub-packet from the buffer. E.g.:

```csharp
byte* packetData = ...;
int offset = 0;

while (offset < packetData.Length)
{
    uint length = ReadVariableLength(packetData, &offset);
    // Process the sub-packet
    ProcessPacket(packetData + offset, (int)length);
    offset += (int)length;
}
```

### Disconnect (0x05)

Disconnect packets are used to indicate that a party is closing the connection.

```csharp
enum DisconnectReason : ushort
{
    None = 0, // No reason can be given for the disconnect.
    IcmpError = 1, // An ICMP error occured, forcing the disconnect.
    Timeout = 2, // The other party has let the session become inactive.
    OtherSideTerminated = 3, // An internal use code, used to indicate that the other party has sent a disconnect.
    ManagerDeleted = 4, // Indicates that the session manager has been disposed of. Generally occurs when the server/client is shutting down.
    ConnectFail = 5, // An internal use code, indicating a session request attempt has failed.
    Application = 6, // The application is terminating the session.
    UnreachableConnection = 7, // An internal use code, indicating that the session must disconnect as the other party is unreachable.
    UnacknowledgedTimeout = 8, // Indicates that the session has been closed because a data sequence was not acknowledged quickly enough.
    NewConnectionAttempt = 9, // Indicates that a session request has failed (often due to the connecting party attempting a reconnection too quickly), and a new attempt should be made after a short delay.
    ConnectionRefused = 10, // Indicates that the application did not accept a session request.
    ConnectError = 11, // Indicates that the proper session negotiation flow has not been observed.
    ConnectingToSelf = 12, // Indicates that a session request has probably been looped back to the sender, and it should not continue with the connection attempt.
    ReliableOverflow = 13, // Indicates that reliable data is being sent too fast to be processed.
    ApplicationReleased = 14, // Indicates that the session manager has been orphaned by the application.
    CorruptPacket = 15, // Indicates that a corrupt packet was received.
    ProtocolMismatch = 16 // Indicates that the requested SOE protocol version or application protocol is invalid.
}

struct Disconnect
{
    uint SessionId; // The ID of the session that is being closed.
    DisconnectReason Reason; // The reason for the closure.
}
```

### Heartbeat (0x06)

Heartbeat packets are used to keep a session alive. They are sent by the game client if
it has not received a packet from the server in ~25-30 seconds, and the server must
respond with a heartbeat of its own to keep the connection alive.

```csharp
struct Heartbeat
{
}
```

### NetStatusRequest (0x07)

It is not entirely clear how these packets are used, nor if the structure represented here is correct.

```csharp
struct NetStatusRequest
{
    ushort ClientTickCount;
    uint LastClientUpdate;
    uint AverageUpdate;
    uint ShortestUpdate;
    uint LongestUpdate;
    uint LastServerUpdate;
    ulong PacketsSent;
    ulong PacketsReceived;
    ushort UnknownValue1;
}
```

### NetStatusResponse (0x08)

Is it not entirely clear how these packets are used, nor if the structure represented here is correct.

```csharp
struct NetStatusResponse
{
    ushort ClientTickCount;
    uint ServerTickCount;
    ulong ClientPacketsSent;
    ulong ClientPacketsReceived;
    ulong ServerPacketsSent;
    ulong ServerPacketsReceived;
    ushort UnknownValue1;
}
```

### ReliableData (0x09)

ReliableData packets are used to transfer application data that is small enough to not need fragmenting,
given the current `UdpLength` of the connection.

```csharp
struct ReliableData
{
    ushort Sequence; // The sequence number within the data stream. This may wrap around.
    byte[] DataBuffer; // The data being sent.
}
```

### ReliableDataFragment (0x0D)

ReliableDataFragment packets are used to transfer application data that is too large to fit within a
single `ReliableData` packet, given the current `UdpLength` of the connection.

```csharp
struct ReliableDataFragment
{
    ushort Sequence; // The sequence number within the data stream. This may wrap around.
    uint? CompleteDataLength; // The length of the non-fragmented data buffer. Only included in the first fragment.
    byte[] DataBuffer; // A fragment of the data being sent.
}
```

### Acknowledge (0x11)

Acknowledge packets are sent by a party to indicate they have received a particular data packet.

```csharp
struct Acknowledge
{
    ushort Sequence; // The sequence number of the data packet.
}
```

### AcknowledgeAll (0x15)

AcknowledgeAll packets are sent by a party to indicate the most recent data sequence they received,
thereby preventing the need to send many Acknowledge packets.

```csharp
struct AcknowledgeAll
{
    ushort Sequence; // The sequence number of the most recently received data packet.
}
```

### UnknownSender (0x1D)

UnknownSender packets are used to indicate that an SOE protocol packet has been received from a remote
endpoint for which the receiver does not currently have a running session. The remote party, upon
receiving this packet, may either request a remap, start a new session, or terminate.

```csharp
struct UnknownSender
{
}
```

### RemapConnection (0x1E)

RemapConnection packets are used to request that a session which the sender believes should currently exist,
be remapped to the sender's new port (and perhaps IP address, although this is unsafe). This may be required,
for example, in cases where the sender (usually a client) is behind a NAT service, and their external port is changed.

```csharp
struct RemapConnection
{
    uint SessionId; // The ID of the session to remap
    uint CrcSeed; // The CRC seed of the session to remap.
}
```
