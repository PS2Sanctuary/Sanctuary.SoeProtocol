using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using System;

namespace Sanctuary.SoeProtocol.Tests.Mocks;

public sealed class MockApplicationProtocolHandler : IApplicationProtocolHandler
{
    public ApplicationParameters SessionParams { get; }
    public bool HasBeenInitialized { get; private set; }
    public bool HasSessionOpened { get; private set; }
    public bool HasSessionClosed { get; private set; }

    public MockApplicationProtocolHandler()
    {
        SessionParams = new ApplicationParameters(null);
    }

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
