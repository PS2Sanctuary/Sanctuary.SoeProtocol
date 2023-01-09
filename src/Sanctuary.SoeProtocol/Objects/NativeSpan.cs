using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Represents a wrapper around native memory.
/// </summary>
public sealed unsafe class NativeSpan : IDisposable
{
    private readonly int _len;
    private readonly byte* _ptr;

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets the length of the <see cref="WriteSpan"/> that is currently in use.
    /// </summary>
    public int UsedLength { get; set; }

    /// <summary>
    /// Gets a span around the underlying native memory.
    /// </summary>
    public Span<byte> WriteSpan
        => IsDisposed
            ? throw new ObjectDisposedException(nameof(NativeSpan))
            : new Span<byte>(_ptr, _len);

    /// <summary>
    /// Gets a span around the underlying native memory that
    /// is actually being used.
    /// </summary>
    public Span<byte> ReadSpan
        => IsDisposed
            ? throw new ObjectDisposedException(nameof(NativeSpan))
            : new Span<byte>(_ptr, UsedLength);

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
        _ptr = pointer;
        _len = length;
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
        if (data.Length > _len)
            throw new InvalidOperationException("Too long");

        data.CopyTo(WriteSpan);
        UsedLength = data.Length;
    }

    /// <summary>
    /// Creates an <see cref="UnmanagedMemoryStream"/> over the underlying
    /// native memory.
    /// </summary>
    /// <returns>An <see cref="UnmanagedMemoryStream"/> instance.</returns>
    public UnmanagedMemoryStream ToStream()
        => new(_ptr, UsedLength, _len, FileAccess.ReadWrite);

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        NativeMemory.Free(_ptr);
        IsDisposed = true;
    }
}
