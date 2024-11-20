using System.Runtime.CompilerServices;

namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// Implements a sliding-window array, which indexes its items relative
/// to the current position of the window on the underlying array.
/// </summary>
/// <typeparam name="T">The underlying type of the window array.</typeparam>
[SkipLocalsInit]
public class SlidingWindowArray<T>
{
    private readonly T[] _array;

    /// <summary>
    /// Gets the index into the <see cref="_array"/> at which the current window starts.
    /// </summary>
    private long _windowStart;

    /// <summary>
    /// Gets an item at the given index.
    /// </summary>
    /// <param name="index">The index, relative to the current window.</param>
    /// <returns>The element at the given index.</returns>
    public T this[int index]
    {
        get => _array[TranslateWindowOffsetToArrayIndex(index)];
        set => _array[TranslateWindowOffsetToArrayIndex(index)] = value;
    }

    /// <summary>
    /// Gets an item at the given index.
    /// </summary>
    /// <param name="index">The index, relative to the current window.</param>
    /// <returns>The element at the given index.</returns>
    public T this[long index]
    {
        get => _array[TranslateWindowOffsetToArrayIndex(index)];
        set => _array[TranslateWindowOffsetToArrayIndex(index)] = value;
    }

    /// <summary>
    /// Gets the length of the underlying array.
    /// </summary>
    public int Length => _array.Length;

    /// <summary>
    /// Gets the item currently exposed by the window.
    /// </summary>
    public T Current
    {
        get => this[0];
        set => this[0] = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SlidingWindowArray{T}"/> class.
    /// </summary>
    /// <param name="windowLength">The length of the underlying array to use.</param>
    public SlidingWindowArray(int windowLength)
        : this(new T[windowLength])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SlidingWindowArray{T}"/> class.
    /// </summary>
    /// <param name="array">The underlying array to wrap.</param>
    public SlidingWindowArray(T[] array)
    {
        _array = array;
    }

    /// <summary>
    /// Shifts the start of the window.
    /// </summary>
    /// <param name="offset">The amount to shift the window by.</param>
    public void Slide(int offset = 1)
        => _windowStart = TranslateWindowOffsetToArrayIndex(offset);

    /// <summary>
    /// Gets a reference to the underlying array.
    /// </summary>
    /// <param name="currentWindowStartIndex">The index within the array at which the current window starts.</param>
    /// <returns>The underlying array.</returns>
    public T[] GetUnderlyingArray(out long currentWindowStartIndex)
    {
        currentWindowStartIndex = _windowStart;
        return _array;
    }

    /// <summary>
    /// Translates an offset from the current window position
    /// to an index in the underlying array.
    /// </summary>
    /// <param name="offset">The offset from the current window position.</param>
    /// <returns>The representative index in the underlying array.</returns>
    private long TranslateWindowOffsetToArrayIndex(long offset)
    {
        long index = _windowStart + offset;

        if (index < 0)
            return Length + index % Length;

        if (index >= _array.Length)
            return index % Length;

        return index;
    }
}
