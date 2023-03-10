using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Represents a wrapper around native memory.
/// </summary>
[SkipLocalsInit]
public sealed unsafe class NativeSpan : IDisposable
{
    private readonly int _len;
    private byte* _ptr;

    /// <summary>
    /// Gets a value indicating whether or not this <see cref="NativeSpan"/>
    /// instance has been disposed.
    /// </summary>
    public bool IsDisposed => _ptr is null;

    /// <summary>
    /// Gets or sets the index into the <see cref="FullSpan"/> at which data begins.
    /// </summary>
    public int StartOffset { get; set; }

    /// <summary>
    /// Gets or sets the length of the <see cref="FullSpan"/> that is currently in use.
    /// </summary>
    public int UsedLength { get; set; }

    /// <summary>
    /// Gets a span around the underlying native memory.
    /// </summary>
    public Span<byte> FullSpan
    {
        get
        {
            DisposedCheck();
            return new Span<byte>(_ptr, _len);
        }
    }

    /// <summary>
    /// Gets a span around the underlying native memory that
    /// is actually being used.
    /// </summary>
    public Span<byte> UsedSpan
    {
        get
        {
            DisposedCheck();
            return new Span<byte>(_ptr + StartOffset, UsedLength);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeSpan"/> class
    /// by allocating a new block of native memory.
    /// </summary>
    /// <param name="length">The amount of native memory to allocate.</param>
    public NativeSpan(int length)
        : this((byte*)NativeMemory.Alloc((nuint)length), length)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeSpan"/> class
    /// over an existing block of native memory.
    /// </summary>
    /// <param name="pointer">A pointer to the native memory to wrap.</param>
    /// <param name="length">The length in bytes of the underlying native memory.</param>
    public NativeSpan(byte* pointer, int length)
    {
        if (pointer is null)
            throw new ArgumentException("Cannot create a NativeSpan around a null pointer", nameof(pointer));

        _ptr = pointer;
        _len = length;
        StartOffset = 0;
        UsedLength = 0;
    }

    /// <summary>
    /// Copies data into the underlying memory of the native span,
    /// and sets <see cref="UsedLength"/> correspondingly.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the data is too long to be stored in the underlying allocated memory.
    /// </exception>
    public void CopyDataInto(ReadOnlySpan<byte> data)
    {
        DisposedCheck();
        if (data.Length > _len)
            throw new InvalidOperationException("The provided data is too long to fit in the underlying native memory");

        data.CopyTo(FullSpan);
        UsedLength = data.Length;
    }

    /// <summary>
    /// Creates an <see cref="UnmanagedMemoryStream"/> over the
    /// <see cref="UsedSpan"/>
    /// </summary>
    /// <returns>An <see cref="UnmanagedMemoryStream"/> instance.</returns>
    public UnmanagedMemoryStream ToStream()
    {
        DisposedCheck();

        return new UnmanagedMemoryStream
        (
            _ptr + StartOffset,
            UsedLength,
            _len - StartOffset,
            FileAccess.ReadWrite
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DisposedCheck()
    {
        if (_ptr is null)
            throw new ObjectDisposedException(nameof(NativeSpan));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ptr is null)
            return;

        NativeMemory.Free(_ptr);
        _ptr = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of the <see cref="NativeSpan"/> upon destruction.
    /// </summary>
    ~NativeSpan()
        => Dispose();
}
