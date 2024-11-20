using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace System;

/// <summary>
/// Defines extension methods for the <see cref="Span{T}"/> struct.
/// </summary>
public static class SpanExtensions
{
    /// <summary>
    /// Swaps the position of two elements in a span.
    /// </summary>
    /// <typeparam name="T">The generic type of the span.</typeparam>
    /// <param name="data">The span to swap the elements in.</param>
    /// <param name="index1">The index of the first element.</param>
    /// <param name="index2">The index of the second element.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Swap<T>(this Span<T> data, int index1, int index2)
    {
        // ReSharper disable once SwapViaDeconstruction
        T temp = data[index1];
        data[index1] = data[index2];
        data[index2] = temp;
    }
}
