using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;

namespace Sanctuary.SoeProtocol.Tests.Mocks;

public sealed class MockApplicationProtocolHandler : IApplicationProtocolHandler
{
    public ApplicationParameters SessionParams { get; } = new(new Rc4KeyState([0, 1, 2, 3, 4]));
    public bool HasBeenInitialized { get; private set; }
    public bool HasSessionOpened { get; private set; }
    public bool HasSessionClosed { get; private set; }

    public void Initialise(ISessionHandler sessionHandler)
        => HasBeenInitialized = true;

    public void OnSessionOpened()
        => HasSessionOpened = true;

    public void HandleAppData(ReadOnlySpan<byte> data)
    {
    }

    public void OnSessionClosed(DisconnectReason disconnectReason)
        => HasSessionClosed = true;
}
