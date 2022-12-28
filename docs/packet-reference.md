# Packet Reference

### SessionRequest (0x01)

Session request packets request the start of an SOE protocol session.

```csharp
struct SessionRequest
{
    uint UnknownValue1; // Unknown, possibly a magic value. Always observed to be 3.
    uint SessionId; // A randomly generated session idenfiier
    uint UdpLength; // The maximum length of a UDP packet that the sender can receive
    string ApplicationProtocol; // A null-terminated descriptor of the application protocol that the sender wishes to transport
}
```

### Session Response (0x02)

Session response packets confirm the creation of a session.

```csharp
struct SessionResponse
{
    uint SessionId; // The ID of the requested session that is being confirmed
    uint CrcSeed; // A randomly generated seed used to calculate the CRC-32 check value on relevant packets
    byte CrcLength; // The number of bytes that should be used to encode the CRC-32 check value on relevant packets
    bool IsCompressionEnabled; // Indicates whether relevant packets may be compressed.
    byte UnknownValue1; // Unknown. Always appears to have a value of 0.
    uint UdpLength; // The maximum length of a UDP packet that the sending can receive.
    uint UnknownValue2; // Unknown, possibly a magic value. Always observed to be 3.
}
```

### MultiPacket (0x03)

MultiPackets wrap multiple packets. Sub-packets may not be compressed and do not carry a CRC check value. Each sub-packet is prefixed by its length, encoded with a variable-size integer.

To read a multipacket, one should loop until the entire packet has been consumed. Each iteration,
a variable-size integer should be read, which indicates the amount of data to read in order to
extract the next sub-packet in the buffer. E.g.:

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

MultiPackets use a dedicated algorithm for reading and writing variable-size integers.
See [Appendix A](#a-reading-and-writing-multipacket-variable-size-integers).

### Disconnect (0x05)

Disconnect packets are used to indicate that a party is closing the connection.

```csharp
enum DisconnectReason : ushort
{
    None = 0,
    IcmpError = 1,
    Timeout = 2,
    OtherSideTerminated = 3,
    ManagerDeleted = 4,
    ConnectFail = 5,
    Application = 6,
    UnreachableConnection = 7,
    UnacknowledgedTimeout = 8,
    NewConnectionAttempt = 9,
    ConnectionRefused = 10,
    ConnectError = 11,
    ConnectingToSelf = 12,
    ReliableOverflow = 13,
    ApplicationReleased = 14,
    CorruptPacket = 15,
    ProtocolMismatch = 16
}

struct Disconnect
{
    uint SessionId; // The ID of the session that is being closed
    DisconnectReason Reason; // The reason for the closure
}
```

### Heartbeat (0x06)

Heartbeat packets are used to keep a session alive. They are only sent by the game client, if
it has not received a packet from the server in ~25-30 seconds. They carry no data.

### NetStatusRequest (0x07)

It is not entirely clear how these packets are used.

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

Is it not entirely clear how these packets are used.

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

### Data (0x09)



# Appendix

## A. Reading and Writing MultiPacket Variable-Size Integers

```csharp
static uint ReadVariableLength(byte* data, int* offset)
{
    uint value;

    if (data[offset] <= 0xFF && data[offset + 1] == 0)
    {
        // The implied 0x00 in front of all core OP codes given big endian,
        // allows us to use that as an indicator for a length value of 0xFF
        value = data[offset++];
    }
    else if (data[offset + 1] == 0xFF && data[offset + 2] == 0xFF)
    {
        offset += 3;
        value = ReadUInt32BigEndian(data + offset);
        offset += sizeof(uint);
    }
    else
    {
        offset += 1;
        value = ReadUInt16BigEndian(data + offset);
        offset += sizeof(ushort);
    }

    return value;
}

static void WriteVariableLength(byte* buffer, uint length, int* offset)
{
    if (length <= 0xFF)
    {
        // We rely on the sub-packet OP codes all starting with 0x00
        // (given big endian) to signal that a length of 0xFF is not
        // ushort varint.
        buffer[offset++] = (byte)length;
    }
    else if (length < ushort.MaxValue)
    {
        buffer[offset++] = 0xFF;
        WriteUInt16BigEndian(buffer + offset, (ushort)length);
        offset += sizeof(ushort);
    }
    else
    {
        buffer[offset++] = 0xFF;
        buffer[offset++] = 0xFF;
        buffer[offset++] = 0xFF;
        WriteUInt32BigEndian(buffer + offset, length);
        offset += sizeof(uint);
    }
}
```
