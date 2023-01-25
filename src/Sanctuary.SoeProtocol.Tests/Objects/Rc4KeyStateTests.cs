using Sanctuary.SoeProtocol.Objects;
using System;

namespace Sanctuary.SoeProtocol.Tests.Objects;

public class Rc4KeyStateTests
{
    [Fact]
    public void ExistingStateConstructor_CreatesFullCopy_OfPassedState()
    {
        byte[] keyBytes = { 0, 1, 2, 3, 4 };

        Rc4KeyState keyState = new(keyBytes);
        keyState.Index1 = 5;
        keyState.Index2 = 10;

        Rc4KeyState copied = new(keyState);
        Assert.Equal(keyState.MutableKeyState.ToArray(), copied.MutableKeyState.ToArray());
        Assert.Equal(keyState.Index1, copied.Index1);
        Assert.Equal(keyState.Index2, copied.Index2);

        keyState.Dispose();
        copied.Dispose();
    }

    [Fact]
    public void Copy_CreatesFullCopy_OfPassedState()
    {
        byte[] keyBytes = { 0, 1, 2, 3, 4 };

        Rc4KeyState keyState = new(keyBytes);
        keyState.Index1 = 5;
        keyState.Index2 = 10;

        Rc4KeyState copied = keyState.Copy();
        Assert.Equal(keyState.MutableKeyState.ToArray(), copied.MutableKeyState.ToArray());
        Assert.Equal(keyState.Index1, copied.Index1);
        Assert.Equal(keyState.Index2, copied.Index2);

        keyState.Dispose();
        copied.Dispose();
    }
}
