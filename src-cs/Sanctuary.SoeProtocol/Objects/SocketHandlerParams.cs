using Sanctuary.SoeProtocol.Abstractions;
using System;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Options used to configure and control the <see cref="SoeSocketHandler"/>.
/// </summary>
public class SocketHandlerParams
{
    /// <summary>
    /// The default session parameters used when creating new session handlers.
    /// </summary>
    public required SessionParameters DefaultSessionParams { get; set; }

    /// <summary>
    /// A callback used to create a new application handler.
    /// </summary>
    public required Func<IApplicationProtocolHandler> AppCreationCallback { get; set; }

    /// <summary>
    /// The size of the packet pool used by the socket handler (i.e. how many packets you expect to be waiting in queues
    /// at any one time). This value should scale with the number of expected connections.
    /// </summary>
    public int PacketPoolSize { get; init; } = 5192;

    /// <summary>
    /// Whether sessions are allowed to remap their ports.
    /// </summary>
    public bool AllowPortRemaps { get; set; }

    /// <summary>
    /// Instructs the socket handler to stop when the active session is terminated. Useful when running in client mode.
    /// This flag only affects the RunAsync method.
    /// </summary>
    public bool StopOnLastSessionTerminated { get; set; }
}
