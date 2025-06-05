using System;

namespace Sanctuary.SoeProtocol.Abstractions;

/// <summary>
/// Represents an SOE session.
/// </summary>
public interface ISoeSession
{
    /// <summary>
    /// The ID of the session. This will return <c>0</c> if a session has not yet been negotiated.
    /// </summary>
    uint SessionId { get; }

    /// <summary>
    /// Send a contextual SOE packet to the remote.
    /// </summary>
    /// <param name="opCode">The OP code of the packet.</param>
    /// <param name="packetData">The packet data. Do not wrap the data with frame details such as the CRC.</param>
    void SendContextualPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData);
}
