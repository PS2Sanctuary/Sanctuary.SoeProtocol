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
    /// Gets the timespan indicating how long a session stay alive without receiving any
    /// data. Set to <see cref="TimeSpan.Zero"/> to prevent a session from being terminated
    /// due to inactivity.
    /// </summary>
    public static readonly TimeSpan SessionInactivityTimeout = TimeSpan.FromSeconds(30);
}
