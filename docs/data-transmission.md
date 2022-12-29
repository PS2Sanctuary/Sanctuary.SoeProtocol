# Data Transmission

The SOE protocol is designed to facilitate reliable and ordered transmission of data over UDP. The data
is typically a higher-level application protocol, such as PlanetSide 2's `LoginUdp` or `ExternalGatewayApi`
protocols.

Please familiarise yourself with the definitions of `ReliableData`, `ReliableDataFragment`, `OutOfOrder`
and `Acknowledge` packets in the [packet reference](./packet-reference.md) before continuing.

## Data Preparation

Before application data is sent, it may be prepared using a pipeline of stages. These stages should, of
course, be repeated in reverse on the receiving end.

### 1. Encryption

Data encryption is optional. In the case of PlanetSide 2, its use is controlled by the application protocol
being proxied, and 'defaults' to disabled (in practice, it is turned on near-to or truly immediately).

Encryption is performed using the [RC4 Stream Cipher](https://en.wikipedia.org/wiki/RC4). The stream state is
maintained for the entirety of the session, rather than being reset for each block of data. The cipher key
is determined by the application protocol in use. For example, PlanetSide 2's `LoginUdp` uses a fixed key,
which in turn transmits a random key to use when it hands over the `ExternalGatewayApi` protocol.

It is important to note that if a data buffer, once encrypted, begins with a `0x00` byte, it **must** be prefixed
with another `0x00` byte. In turn, the receiver must ignore an initial byte of `0x00` if present on an encrypted
data buffer.

### 2. Data Bundling

If multiple small data buffers are present, they may be bundled together, as long as the total length of the
bundle will not exceed the maximum amount of data that can be stored in a single `ReliableData` packet.

To indicate that a bundle has been formed, it must be prefixed with the two bytes `0x00, 0x19`. Then, in the
same way as in a `MultiPacket`, data buffers are placed back-to-back and prefixed by their lengths, encoded
using variable-size integers. However, the length encoding uses a slightly different algorithm, as less assumptions
can be made. See [Appendix C](./appendix.md#c-reading-and-writing-data-bundle-variable-size-integers) for more info.

## Sending Data

Once data has been through the preparation pipeline, it can be sent. If the pipeline buffer is small enough to fit
within a single `ReliableData` packet, it can be sent directly. Else, it will have to be fragmented into multiple
`ReliableDataFragment` packets. To check this, the total size of a `ReliableData` packet given a buffer length can
be calculated as such:

```csharp
// Note: we do not have to factor in being a sub-packet (of a MultiPacket) as data which would have fit, if it
// were to be a sub-packet and hence drop the requisite fields, will be too long for a MultiPacket anyway.

int length = sizeof(SoeOpCode) // 2 bytes
    + (isSessionCompressionEnabled ? 1 : 0) // Compression flag, present if compression is enabled
    + sizeof(ushort) // Sequence
    + dataLength // Length of the data buffer
    + sessionCrcLength // The number of bytes used to store the CRC value
```

If the calculated length is greater than the `UdpLength` of the receiver, as defined in `SessionRequest/Response`, the
data will have to be fragmented.

### Fragmenting Data

Fragmenting data is fairly simple. Using an algorithm similar to the above, the amount of data that can fit into each
`ReliableDataFragment` packet can be calculated. However, it must be taken into consideration that the first fragment
needs to store the total length of the data buffer, so that it can be reassembled by the receiver. Simply split the
data buffer accordingly, and send in however many `ReliableDataFragment` packets are required.

### Sequencing Data

Each `ReliableData`/`ReliableDataFragment` packet carries a sequence number, so that they may be reassembled in order
by the receiver. This sequence number should be incremented for each data packet that is sent, and it is required
that it wrap around back to zero, which implementations must account for.

### Re-sending Data

The receiver must `Acknowledge` data within a reasonable amount of time. Depending on the implementation, they
might need to acknowledge every data sequence, or only the most recent sequence they received every so often, in a
[sliding window](https://en.wikipedia.org/wiki/Sliding_window_protocol) fashion. For example, the PlanetSide 2
client only acknowledges server data packets every so often, but the server acknowledges every data packet sent
by the client.

In the case that a data sequence is *not* acknowledged, the sender must re-send all the data sequences starting
from the unacknowledged sequence, until the receiver acknowledges them. The same must be done in the case that any
`OutOfOrder` packets are received, starting from the lowest `OutOfOrder` sequence.

## Receiving Data

When receiving data, packets must be processed in order of their sequence number. While technically not necessary
for `ReliableData` packets, as they do not need reassembling, the SOE protocol guarantees to the application data
it is proxying that the data will be received in order.

As such, if the implementation receives a sequence that is out-of-order, it must either stash it for use later, or
discard it and send an `OutOfOrder` packet to instruct the sender to re-send it (assuming that in the meantime, the
missing packets will arrive and can be acknowledged).

`ReliableDataFragment` packets need special attention, in order to reassemble them. The first fragment of a large buffer
should be identified. This will contain the `CompleteDataLength` field, which indicates the number of sequences/
amount of data needed to attempt reassembly. Once all required fragments have been received, they can be reassembled
in sequence order to attain the original data buffer.

At this point, the data buffers should be run through the 'preparation' pipeline, in reverse:

1. Check for data bundles, by looking for the magic two bytes (`0x00, 0x19`), and read appropriately by looping
through the buffer, reading the variable-length size and the corresponding sub-buffer on each iteration.

2. Decrypt each data buffer if encryption is enabled, ensuring to skip the first byte of the buffer if it is `0x00`.

### Acknowledging Data

Implementations can either `Acknowledge` each sequence as it arrives, or acknowledge multiple sequences by sending
an `Acknowledge` packet with the most recent sequence received after some period of time. How this period of time
is calculated is not entirely understood; in some cases it appears to be an acknowledgement of every Xth sequence
or after a timeout; in other cases it appears to be entirely time based (e.g. send acknowledgement every 0.5s while
receiving data).
