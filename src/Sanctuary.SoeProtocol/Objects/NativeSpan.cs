using System;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Represents a wrapper around native memory.
/// </summary>
public unsafe readonly struct NativeSpan
{
    private readonly int _len;
    internal readonly byte* _ptr;

    /// <summary>
    /// Gets a span around the underlying native memory.
    /// </summary>
    public Span<byte> Span => new(_ptr, _len);

    /// <summary>
    /// Use of the default construct is invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">Use of the default construct is invalid.</exception>
    public NativeSpan()
    {
        throw new InvalidOperationException($"Use of {nameof(NativeSpan)}'s default constructor is invalid");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeSpan"/> struct.
    /// </summary>
    /// <param name="pointer">A pointer to the native memory to wrap.</param>
    /// <param name="length">The length in bytes of the underlying native memory.</param>
    public NativeSpan(byte* pointer, int length)
    {
        _ptr = pointer;
        _len = length;
    }

    /// <summary>
    /// Allocates a new <see cref="NativeSpan"/> object directly from native memory.
    /// The underlying memory must be freed once use of the span is complete.
    /// </summary>
    /// <param name="length">The number of bytes to allocate.</param>
    /// <returns>The allocated native span.</returns>
    public static NativeSpan Allocate(int length)
        => new((byte*)NativeMemory.Alloc((nuint)length), length);

    /// <summary>
    /// Frees the underlying memory of an allocated <see cref="NativeSpan"/> object.
    /// </summary>
    /// <param name="span">The <see cref="NativeSpan"/> to free.</param>
    public static void Free(NativeSpan span)
        => NativeMemory.Free(span._ptr);
}
