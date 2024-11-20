namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Enumerates the modes that a <see cref="SoeProtocolHandler"/> can be in.
/// </summary>
public enum SessionMode
{
    /// <summary>
    /// The handler should act as a client.
    /// </summary>
    Client,

    /// <summary>
    /// The handler should act as a server.
    /// </summary>
    Server
}
