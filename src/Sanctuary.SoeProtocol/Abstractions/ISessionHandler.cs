using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;

namespace Sanctuary.SoeProtocol.Abstractions;

/// <summary>
/// Represents an object capable of handling an SOE protocol session.
/// </summary>
public interface ISessionHandler
{
    /// <summary>
    /// Gets the operating mode of the session handler.
    /// </summary>
    public SessionMode Mode { get; }

    /// <summary>
    /// Gets the current state of the session handler.
    /// </summary>
    public SessionState State { get; }

    /// <summary>
    /// Gets the ID of the session. This will return <c>0</c>
    /// if a session has not yet been negotiated.
    /// </summary>
    public uint SessionId { get; }

    /// <summary>
    /// Gets the reason that the session handler was terminated.
    /// Will return <see cref="DisconnectReason.None"/> if the handler
    /// has not yet been terminated.
    /// </summary>
    public DisconnectReason TerminationReason { get; }

    /// <summary>
    /// Enqueues data to be sent to the other party.
    /// </summary>
    /// <param name="data">The data.</param>
    public void EnqueueData(ReadOnlySpan<byte> data);

    /// <summary>
    /// Terminates the session handler.
    /// </summary>
    public void TerminateSession();
}
