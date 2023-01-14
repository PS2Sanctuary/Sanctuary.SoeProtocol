using System;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains parameters used to control a session.
/// </summary>
public class SessionParameters
{
    /// <summary>
    /// Gets the application protocol being proxied by this session.
    /// </summary>
    public required string ApplicationProtocol { get; init; }

    /// <summary>
    /// Gets the maximum length of a UDP packet that this party
    /// can send or receive.
    /// </summary>
    public uint UdpLength { get; init; }

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
    public int MaxQueuedRawPackets { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of data fragments that may be
    /// queued for either stitching or dispatch.
    /// </summary>
    public int MaxQueuedReliableDataPackets { get; init; }

    /// <summary>
    /// Gets the timespan after which to send a heartbeat, if no contextual
    /// packets have been received within the interval. Set to <see cref="TimeSpan.Zero"/>
    /// to disable heart-beating.
    /// </summary>
    public TimeSpan HeartbeatAfter { get; init; }

    /// <summary>
    /// Gets the default timespan after which to consider a session inactive, if no
    /// contextual packets have been received within the interval. Set to
    /// <see cref="TimeSpan.Zero"/> to prevent a session from being terminated
    /// due to inactivity.
    /// </summary>
    public TimeSpan InactivityTimeout { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionParameters"/> class.
    /// </summary>
    public SessionParameters()
    {
        CrcLength = SoeConstants.CrcLength;
        UdpLength = SoeConstants.DefaultUdpLength;
        MaxQueuedRawPackets = 256;
        MaxQueuedReliableDataPackets = 128;
        HeartbeatAfter = SoeConstants.DefaultSessionHeartbeatAfter;
        InactivityTimeout = SoeConstants.DefaultSessionInactivityTimeout;
    }

    public SessionParameters Clone()
        => (SessionParameters)this.MemberwiseClone();
}
