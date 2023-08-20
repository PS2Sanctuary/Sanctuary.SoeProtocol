using Sanctuary.SoeProtocol.Objects.Packets;

namespace Sanctuary.SoeProtocol.Tests.Objects.Packets;

public class AcknowledgeTests
{
    [Fact]
    public void RoundTrip_Succeeds()
    {
        Acknowledge ack = new(2);

        byte[] buffer = new byte[Acknowledge.Size];
        ack.Serialize(buffer);

        Acknowledge deserialized = Acknowledge.Deserialize(buffer);
        Assert.Equal(ack, deserialized);
    }
}
