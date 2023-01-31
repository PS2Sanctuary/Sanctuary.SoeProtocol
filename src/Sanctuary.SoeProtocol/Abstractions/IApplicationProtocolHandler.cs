using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;

namespace Sanctuary.SoeProtocol.Abstractions;

/// <summary>
/// Represents an application protocol handler, with appropriate hooks for the
/// underlying session handler.
/// </summary>
public interface IApplicationProtocolHandler
{
    /// <summary>
    /// Gets the parameters used to control the underlying SOE session.
    /// </summary>
    ApplicationParameters SessionParams { get; }

    /// <summary>
    /// Initializes the application handler.
    /// </summary>
    /// <param name="sessionHandler">The underlying SOE session handler.</param>
    void Initialise(ISessionHandler sessionHandler);

    /// <summary>
    /// Notifies the handler that the underlying SOE session has opened.
    /// </summary>
    void OnSessionOpened();

    /// <summary>
    /// Allows the handler to process app data. This method should not
    /// perform any long running work.
    /// </summary>
    /// <param name="data">The application data.</param>
    void HandleAppData(ReadOnlySpan<byte> data);

    /// <summary>
    /// Notifies the handler that the underlying SOE session has closed.
    /// </summary>
    /// <param name="disconnectReason">The reason that the session was terminated.</param>
    void OnSessionClosed(DisconnectReason disconnectReason);
}
