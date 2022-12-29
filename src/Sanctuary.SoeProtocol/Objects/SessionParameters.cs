namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains parameters used to control a session.
/// </summary>
public class SessionParameters
{
    /// <summary>
    /// Gets or sets the maximum length of a UDP packet that the other
    /// party in the session can receive.
    /// </summary>
    public uint UdpLength { get; set; }

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
}
