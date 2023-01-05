namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains parameters used to control a session.
/// </summary>
public class SessionParameters
{
    /// <summary>
    /// Gets the application protocol being proxied by this session.
    /// </summary>
    public string ApplicationProtocol { get; }

    /// <summary>
    /// Gets the maximum length of a UDP packet that this party
    /// can send or receive.
    /// </summary>
    public required uint UdpLength { get; init; }

    /// <summary>
    /// Gets or sets the maximum length of a UDP packet that the other
    /// party in the session can receive.
    /// </summary>
    public uint RemoteUdpLength { get; set; }

    /// <summary>
    /// Gets or sets the seed used to calculate packet CRC hashes.
    /// </summary>
    public uint CrcSeed { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes used to store a packet CRC hash.
    /// Must be between 0 and 4, inclusive.
    /// </summary>
    public byte CrcLength { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether compression is enabled
    /// for the session.
    /// </summary>
    public bool IsCompressionEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether encryption is enabled
    /// for the session.
    /// </summary>
    public bool IsEncryptionEnabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of raw packets that may queued
    /// for either processing or sending.
    /// </summary>
    public int MaxQueuedRawPackets { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionParameters"/> class.
    /// </summary>
    /// <param name="applicationProtocol">The application protocol to be proxied by the session.</param>
    public SessionParameters(string applicationProtocol)
    {
        ApplicationProtocol = applicationProtocol;
        CrcLength = SoeConstants.CrcLength;
        UdpLength = SoeConstants.DefaultUdpLength;
        MaxQueuedRawPackets = 256;
    }
}
