using Sanctuary.SoeProtocol.Objects.Packets;

namespace Sanctuary.SoeProtocol.Tests.Objects.Packets;

public class SessionRequestTests
{
    [Fact]
    public void RoundTrip_Succeeds()
    {
        SessionRequest sessionRequest = new(3, 5467392, 512, "TestProtocol");

        byte[] buffer = new byte[sessionRequest.GetSize()];
        sessionRequest.Serialize(buffer);

        SessionRequest deserialized = SessionRequest.Deserialize(buffer, true);
        Assert.Equal(sessionRequest, deserialized);
    }
}
