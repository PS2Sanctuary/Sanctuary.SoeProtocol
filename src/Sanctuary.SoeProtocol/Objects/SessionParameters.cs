using System;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains parameters used to control a session.
/// </summary>
public class SessionParameters : IDisposable
{
    private bool _isDisposed;

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
    public short MaxQueuedReliableDataPackets { get; init; }

    /// <summary>
    /// Gets or sets the data acknowledgement window
    /// </summary>
    public short DataAckWindow { get; set; }

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
    /// Gets the encryption key state to use with this session.
    /// </summary>
    public required Rc4KeyState EncryptionKeyState { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether all data packets should be acknowledged.
    /// </summary>
    public bool AcknowledgeAllData { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionParameters"/> class.
    /// </summary>
    public SessionParameters()
    {
        CrcLength = SoeConstants.CrcLength;
        UdpLength = SoeConstants.DefaultUdpLength;
        MaxQueuedRawPackets = 512;
        MaxQueuedReliableDataPackets = 256;
        DataAckWindow = 32;
        HeartbeatAfter = SoeConstants.DefaultSessionHeartbeatAfter;
        InactivityTimeout = SoeConstants.DefaultSessionInactivityTimeout;
    }

    /// <summary>
    /// Creates a deep clone of this <see cref="SessionParameters"/> object.
    /// </summary>
    /// <returns>The cloned object.</returns>
    public SessionParameters Clone()
        => (SessionParameters)this.MemberwiseClone();

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of managed and unmanaged resources.
    /// </summary>
    /// <param name="disposeManaged">Whether to dispose of managed resources.</param>
    protected virtual void Dispose(bool disposeManaged)
    {
        if (_isDisposed)
            return;

        if (disposeManaged)
            EncryptionKeyState.Dispose();

        _isDisposed = true;
    }
}
