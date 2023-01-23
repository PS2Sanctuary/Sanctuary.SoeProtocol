using Sanctuary.SoeProtocol.Objects;
using System;
using System.Collections.Concurrent;

namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// Represents a fixed-limit pool of fixed-size <see cref="NativeSpan"/> objects.
/// </summary>
public sealed class NativeSpanPool : IDisposable
{
    private readonly int _memorySize;
    private readonly int _poolSize;
    private readonly ConcurrentStack<NativeSpan> _pool;

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
        _pool = new ConcurrentStack<NativeSpan>();
    }

    /// <summary>
    /// Rents a <see cref="NativeSpan"/> object from the pool.
    /// Rented objects must be returned to the pool.
    /// </summary>
    /// <remarks>
    /// This method will allocate a new <see cref="NativeSpan"/> object if the pool is empty.
    /// </remarks>
    /// <returns>The rented object.</returns>
    public NativeSpan Rent()
        => _pool.TryPop(out NativeSpan? span)
            ? span
            : new NativeSpan(_memorySize);

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
    public void Return(NativeSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);

        if (span.FullSpan.Length != _memorySize)
            throw new InvalidOperationException($"The {nameof(NativeSpan)} was not rented from this pool");

        if (span.IsDisposed)
            return;

        if (_pool.Count >= _poolSize)
        {
            span.Dispose();
            return;
        }

        _pool.Push(span);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        while (_pool.TryPop(out NativeSpan? span))
            span.Dispose();
    }
}
