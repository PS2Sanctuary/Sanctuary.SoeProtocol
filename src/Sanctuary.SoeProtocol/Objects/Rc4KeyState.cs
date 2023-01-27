using Sanctuary.SoeProtocol.Services;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Contains a state that can be used with the <see cref="Rc4Cipher"/> class.
/// </summary>
[SkipLocalsInit]
public sealed unsafe class Rc4KeyState : IDisposable
{
    /// <summary>
    /// Gets the number of bytes to use for the RC4 key state.
    /// </summary>
    public const int LENGTH = 256;

    private byte* _state;

    /// <summary>
    /// Gets the RC4 key state.
    /// </summary>
    public Span<byte> MutableKeyState => new(_state, LENGTH);

    /// <summary>
    /// Gets or sets the first RC4 transform index.
    /// </summary>
    public int Index1;

    /// <summary>
    /// Gets or sets the second RC4 transform index.
    /// </summary>
    public int Index2;

    /// <summary>
    /// Initializes a new instance of the <see cref="Rc4KeyState"/> class.
    /// </summary>
    /// <param name="keyBytes">The key bytes to initialize this state with.</param>
    public Rc4KeyState(ReadOnlySpan<byte> keyBytes)
    {
        _state = (byte*)NativeMemory.Alloc(LENGTH);
        Index1 = 0;
        Index2 = 0;
        Rc4Cipher.ScheduleKey(keyBytes, MutableKeyState);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rc4KeyState"/> class
    /// by copying an existing state.
    /// </summary>
    /// <param name="existingState">The state to copy.</param>
    public Rc4KeyState(Rc4KeyState existingState)
    {
        _state = (byte*)NativeMemory.Alloc(LENGTH);
        Index1 = existingState.Index1;
        Index2 = existingState.Index2;
        existingState.MutableKeyState.CopyTo(MutableKeyState);
    }

    /// <summary>
    /// Copies this <see cref="Rc4KeyState"/> instance.
    /// </summary>
    /// <returns></returns>
    public Rc4KeyState Copy()
        => new(this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_state is null)
            return;

        NativeMemory.Free(_state);
        _state = null;
    }
}
