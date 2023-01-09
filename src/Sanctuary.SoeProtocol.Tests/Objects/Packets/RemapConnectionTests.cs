using Sanctuary.SoeProtocol.Objects.Packets;

namespace Sanctuary.SoeProtocol.Tests.Objects.Packets;

public class RemapConnectionTests
{
    [Fact]
    public void RoundTrip_Succeeds()
    {
        RemapConnection remapConnection = new(16, 32);

        byte[] buffer = new byte[RemapConnection.Size];
        remapConnection.Serialize(buffer);

        RemapConnection deserialized = RemapConnection.Deserialize(buffer, true);
        Assert.Equal(remapConnection, deserialized);
    }
}
