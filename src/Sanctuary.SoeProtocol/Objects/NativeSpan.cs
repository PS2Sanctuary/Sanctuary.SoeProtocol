using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Sanctuary.SoeProtocol.Objects;

/// <summary>
/// Represents a wrapper around native memory.
/// </summary>
[SkipLocalsInit]
public sealed class NativeSpan
{
    private readonly byte[] _array;

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
    public Span<byte> FullSpan => _array;

    /// <summary>
    /// Gets a span around the underlying native memory that
    /// is actually being used.
    /// </summary>
    public Span<byte> UsedSpan => _array.AsSpan(StartOffset, UsedLength);

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeSpan"/> class.
    /// </summary>
    /// <param name="length">The array to wrap.</param>
    public NativeSpan(int length)
    {
        _array = new byte[length];
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
        if (data.Length > _array.Length)
            throw new InvalidOperationException("The provided data is too long to fit in the underlying native memory");

        data.CopyTo(FullSpan);
        UsedLength = data.Length;
    }

    /// <summary>
    /// Creates an <see cref="UnmanagedMemoryStream"/> over the
    /// <see cref="UsedSpan"/>
    /// </summary>
    /// <returns>An <see cref="UnmanagedMemoryStream"/> instance.</returns>
    public MemoryStream ToStream()
        => new(_array, StartOffset, UsedLength, true);
}
