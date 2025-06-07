using Sanctuary.SoeProtocol.Services;
using System;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains parameters used to control a session.
/// </summary>
public sealed class SessionParameters
{
    private uint _crcSeed;

    /// <summary>
    /// Gets the application protocol being proxied by this session.
    /// </summary>
    public required string ApplicationProtocol { get; init; }

    /// <summary>
    /// Gets the maximum length of a UDP packet that this party
    /// can send or receive.
    /// </summary>
    public uint UdpLength { get; init; } = SoeConstants.DefaultUdpLength;

    /// <summary>
    /// Gets or sets the maximum length of a UDP packet that the other
    /// party in the session can receive.
    /// </summary>
    public uint RemoteUdpLength { get; set; }

    /// <summary>
    /// Gets or sets the seed used to calculate packet CRC hashes.
    /// </summary>
    public uint CrcSeed
    {
        get => _crcSeed;
        set
        {
            _crcSeed = value;
            CrcState = new Crc32(value);
        }
    }

    /// <summary>
    /// The <see cref="Crc32"/> state used to calculate CRC hashes for this session.
    /// This property is auto-set when <see cref="CrcSeed"/> is set.
    /// </summary>
    public Crc32 CrcState { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes used to store a packet CRC hash.
    /// Must be between 0 and 4, inclusive.
    /// </summary>
    public byte CrcLength { get; set; } = SoeConstants.CrcLength;

    /// <summary>
    /// Gets or sets a value indicating whether compression is enabled
    /// for the session.
    /// </summary>
    public bool IsCompressionEnabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of raw packets that may be queued for either processing or sending.
    /// </summary>
    public int MaxQueuedRawPackets { get; init; } = 512;

    /// <summary>
    /// Gets or sets the maximum number of data fragments that may be queued for either stitching or dispatch.
    /// </summary>
    public short MaxQueuedIncomingReliableDataPackets { get; init; } = 256;

    /// <summary>
    /// Gets or sets the maximum number of reliable data fragments that may be queued for output.
    /// </summary>
    public short MaxQueuedOutgoingReliableDataPackets { get; init; } = 196;

    /// <summary>
    /// Gets or sets the data acknowledgement window
    /// </summary>
    public short DataAckWindow { get; set; } = 32;

    /// <summary>
    /// Gets the timespan after which to send a heartbeat, if no contextual packets have been received within the
    /// interval. Set to <see cref="TimeSpan.Zero"/> to disable heart-beating.
    /// </summary>
    public TimeSpan HeartbeatAfter { get; init; } = SoeConstants.DefaultSessionHeartbeatAfter;

    /// <summary>
    /// Gets the default timespan after which to consider a session inactive, if no contextual packets have been
    /// received within the interval. Set to <see cref="TimeSpan.Zero"/> to prevent a session from being terminated
    /// due to inactivity.
    /// </summary>
    public TimeSpan InactivityTimeout { get; init; } = SoeConstants.DefaultSessionInactivityTimeout;

    /// <summary>
    /// Gets or sets a value indicating whether all data packets should be acknowledged.
    /// </summary>
    public bool AcknowledgeAllData { get; set; }

    /// <summary>
    /// Gets the maximum amount of time that may elapse before acknowledging incoming reliable data sequences.
    /// </summary>
    public TimeSpan MaximumAcknowledgeDelay { get; set; } = TimeSpan.FromMilliseconds(2);

    /// <summary>
    /// Creates a shallow copy of this <see cref="SessionParameters"/> instance.
    /// </summary>
    /// <returns>The copied instance.</returns>
    public SessionParameters Clone()
        => (SessionParameters)MemberwiseClone();
}
