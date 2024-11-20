using System;

namespace Sanctuary.SoeProtocol;

/// <summary>
/// Contains ideal constants for this implementation of the SOE protocol.
/// </summary>
public static class SoeConstants
{
    /// <summary>
    /// Gets the implemented version of the SOE protocol.
    /// </summary>
    public const uint SoeProtocolVersion = 3;

    /// <summary>
    /// Gets the number of bytes used to store the CRC check value of a packet.
    /// </summary>
    public const byte CrcLength = 2;

    /// <summary>
    /// Gets the default maximum packet length.
    /// </summary>
    public const uint DefaultUdpLength = 512;

    /// <summary>
    /// Gets the default timespan after which to send a heartbeat, if no contextual
    /// packets have been received within the interval.
    /// </summary>
    public static readonly TimeSpan DefaultSessionHeartbeatAfter = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Gets the default timespan after which to consider a session inactive, if no
    /// contextual packets have been received within the interval.
    /// </summary>
    public static readonly TimeSpan DefaultSessionInactivityTimeout = TimeSpan.FromSeconds(30);
}
