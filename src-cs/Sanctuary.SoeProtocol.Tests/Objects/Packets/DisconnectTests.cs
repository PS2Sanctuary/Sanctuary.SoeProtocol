using Sanctuary.SoeProtocol.Objects.Packets;

namespace Sanctuary.SoeProtocol.Tests.Objects.Packets;

public class DisconnectTests
{
    [Fact]
    public void RoundTrip_Succeeds()
    {
        Disconnect disconnect = new(5, DisconnectReason.Application);

        byte[] buffer = new byte[Disconnect.Size];
        disconnect.Serialize(buffer);

        Disconnect deserialized = Disconnect.Deserialize(buffer);
        Assert.Equal(disconnect, deserialized);
    }
}
