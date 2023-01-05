using Sanctuary.SoeProtocol.Objects;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// Represents a fixed-limit pool of fixed-size <see cref="NativeSpan"/> objects.
/// </summary>
public sealed class NativeSpanPool : IDisposable
{
    private readonly int _memorySize;
    private readonly int _poolSize;
    private readonly ConcurrentStack<nuint> _pool;

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeSpanPool"/> class.
    /// </summary>
    /// <param name="memorySize">
    /// The size in bytes of each <see cref="NativeSpan"/> object to allocate within the pool.
    /// </param>
    /// <param name="poolSize">
    /// The maximum number of <see cref="NativeSpan"/> objects that may be held within the pool.
    /// </param>
    public NativeSpanPool(int memorySize, int poolSize)
    {
        _memorySize = memorySize;
        _poolSize = poolSize;
        _pool = new ConcurrentStack<nuint>();
    }

    /// <summary>
    /// Rents a <see cref="NativeSpan"/> object from the pool.
    /// Rented objects must be returned to the pool.
    /// </summary>
    /// <remarks>
    /// This method will allocate a new <see cref="NativeSpan"/> object if the pool is empty.
    /// </remarks>
    /// <returns>The rented object.</returns>
    public unsafe NativeSpan Rent()
    {
        if (_pool.TryPop(out nuint memoryPointer))
            return new NativeSpan((byte*)memoryPointer, _memorySize);

        byte* ptr = (byte*)NativeMemory.Alloc((nuint)_memorySize);
        return new NativeSpan(ptr, _memorySize);
    }

    /// <summary>
    /// Returns a rented <see cref="NativeSpan"/> object to the pool.
    /// </summary>
    /// <remarks>
    /// The underlying memory of the <see cref="NativeSpan"/> will be freed if the pool is full.
    /// </remarks>
    /// <param name="span">The <see cref="NativeSpan"/> to return.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the given <see cref="NativeSpan"/> is a different length to the memory size of the pool.
    /// </exception>
    public unsafe void Return(NativeSpan span)
    {
        if (span.Span.Length != _memorySize)
            throw new InvalidOperationException($"The {nameof(NativeSpan)} was not rented from this pool");

        if (_pool.Count >= _poolSize)
        {
            NativeMemory.Free(span._ptr);
            return;
        }

        _pool.Push((nuint)span._ptr);
    }

    /// <inheritdoc />
    public unsafe void Dispose()
    {
        while (_pool.TryPop(out nuint nativePtr))
            NativeMemory.Free((byte*)nativePtr);
    }
}
