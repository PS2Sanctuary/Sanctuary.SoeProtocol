using Sanctuary.SoeProtocol.Objects.Packets;

namespace Sanctuary.SoeProtocol.Tests.Objects.Packets;

public class SessionResponseTests
{
    [Fact]
    public void RoundTrip_Succeeds()
    {
        SessionResponse sessionResponse = new(531633, 34322, 2, true, 0, 512, 3);

        byte[] buffer = new byte[SessionResponse.Size];
        sessionResponse.Serialize(buffer);

        SessionResponse deserialized = SessionResponse.Deserialize(buffer);
        Assert.Equal(sessionResponse, deserialized);
    }
}
