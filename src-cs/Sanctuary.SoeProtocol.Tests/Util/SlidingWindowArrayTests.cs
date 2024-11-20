using Sanctuary.SoeProtocol.Util;

namespace Sanctuary.SoeProtocol.Tests.Util;

public class SlidingWindowArrayTests
{
    [Fact]
    public void TestLength()
    {
        SlidingWindowArray<byte> slidingWindow = new(8);
        Assert.Equal(8, slidingWindow.Length);
        Assert.Equal(8, slidingWindow.GetUnderlyingArray(out _).Length);
    }

    [Fact]
    public void TestCurrent()
    {
        byte[] existing = { 0, 1, 2 };
        SlidingWindowArray<byte> slidingWindow = new(existing);

        slidingWindow.Slide();
        Assert.Equal(existing[1], slidingWindow.Current);
    }

    [Fact]
    public void TestCtorUsingExistingArray()
    {
        byte[] existing = { 0, 1, 2 };
        SlidingWindowArray<byte> slidingWindow = new(existing);
        Assert.Equal(3, slidingWindow.Length);

        byte[] underlying = slidingWindow.GetUnderlyingArray(out _);
        Assert.Equal(existing, underlying);
    }

    [Fact]
    public void TestIndexer()
    {
        byte[] existing = { 0, 1, 2 };
        SlidingWindowArray<byte> slidingWindow = new(existing);

        for (int i = 0; i < existing.Length; i++)
            Assert.Equal(existing[i], slidingWindow[i]);

        slidingWindow[1] = 5;
        Assert.Equal(5, existing[1]);
    }

    [Fact]
    public void TestSlide()
    {
        byte[] existing = { 0, 1, 2, 4, 5 };
        SlidingWindowArray<byte> slidingWindow = new(existing);

        slidingWindow.Slide(2);
        Assert.Equal(existing[2], slidingWindow[0]);

        slidingWindow.Slide();
        Assert.Equal(existing[3], slidingWindow[0]);
        Assert.Equal(existing[0], slidingWindow[2]);

        slidingWindow.Slide(9);
        Assert.Equal(existing[2], slidingWindow[0]);

        slidingWindow.Slide(-4);
        Assert.Equal(existing[3], slidingWindow[0]);
    }
}
