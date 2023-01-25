namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Enumerates the states that a <see cref="SoeProtocolHandler"/> can be in.
/// </summary>
public enum SessionState
{
    /// <summary>
    /// The handler is negotiating a session.
    /// </summary>
    Negotiating,

    /// <summary>
    /// The handler is running.
    /// </summary>
    Running,

    /// <summary>
    /// The handler has terminated.
    /// </summary>
    Terminated
}
