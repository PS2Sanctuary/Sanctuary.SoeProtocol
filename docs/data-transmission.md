# Data Transmission

The SOE protocol is designed to facilitate reliable and ordered transmission of data over UDP. The data
is typically a higher-level application protocol, such as PlanetSide 2's `LoginUdp` or `ExternalGatewayApi`
protocols.

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
bundle will not exceed the maximum amount of data that can be stored in a single `Data` packet.

To indicate that a bundle has been formed, it must be prefixed with the two bytes `0x00, 0x19`. Then, in the
same way as in a `MultiPacket`, data buffers are placed back-to-back and prefixed by their lengths, encoded
using variable-size integers. However, the length encoding uses a slightly different algorithm, as less assumptions
can be made. See [Appendix C](./appendix.md#c-reading-and-writing-data-bundle-variable-size-integers) for more info.

## Sending Data



// Random notes
acknowledge the receival of a `Data`/`DataFragment` packet.
Depending on the implementation, Acknowledge packets may be sent for each data packet received, or only
every so often. For example, the PlanetSide 2 client only acknowledges server data packets every so
often, but the server acknowledges every data packet sent by the client.