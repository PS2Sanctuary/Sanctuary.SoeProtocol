using Sanctuary.SoeProtocol.Objects.Packets;

namespace Sanctuary.SoeProtocol.Tests.Objects.Packets;

public class AcknowledgeAllTests
{
    [Fact]
    public void RoundTrip_Succeeds()
    {
        AcknowledgeAll ackAll = new(2);

        byte[] buffer = new byte[AcknowledgeAll.Size];
        ackAll.Serialize(buffer);

        AcknowledgeAll deserialized = AcknowledgeAll.Deserialize(buffer);
        Assert.Equal(ackAll, deserialized);
    }
}
