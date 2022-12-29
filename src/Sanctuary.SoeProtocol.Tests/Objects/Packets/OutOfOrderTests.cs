using Sanctuary.SoeProtocol.Objects.Packets;

namespace Sanctuary.SoeProtocol.Tests.Objects.Packets;

public class OutOfOrderTests
{
    [Fact]
    public void RoundTrip_Succeeds()
    {
        OutOfOrder outOfOrder = new(2);

        byte[] buffer = new byte[OutOfOrder.Size];
        outOfOrder.Serialize(buffer);

        OutOfOrder deserialized = OutOfOrder.Deserialize(buffer);
        Assert.Equal(outOfOrder, deserialized);
    }
}
