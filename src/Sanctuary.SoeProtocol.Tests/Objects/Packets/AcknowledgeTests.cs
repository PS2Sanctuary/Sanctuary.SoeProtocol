using Sanctuary.SoeProtocol.Objects.Packets;

namespace Sanctuary.SoeProtocol.Tests.Objects.Packets;

public class AcknowledgeTests
{
    [Fact]
    public void RoundTrip_Succeeds()
    {
        Acknowledge acknowledge = new(2);

        byte[] buffer = new byte[Acknowledge.Size];
        acknowledge.Serialize(buffer);

        Acknowledge deserialized = Acknowledge.Deserialize(buffer);
        Assert.Equal(acknowledge, deserialized);
    }
}
