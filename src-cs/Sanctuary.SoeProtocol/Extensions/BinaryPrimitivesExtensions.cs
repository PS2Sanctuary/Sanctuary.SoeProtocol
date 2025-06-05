// ReSharper disable once CheckNamespace
namespace System.Buffers.Binary;

/// <summary>
/// Contains extension methods for the <see cref="BinaryPrimitives"/> class.
/// </summary>
public static class BinaryPrimitivesExtensions
{
    /// <summary>
    /// Reads an unsigned 24-bit integer in big endian format.
    /// </summary>
    /// <param name="source">The buffer to read the value from.</param>
    /// <returns>A uint value.</returns>
    public static uint ReadUInt24BE(ReadOnlySpan<byte> source)
    {
        uint value = 0;
        value |= (uint)source[0] << 16;
        value |= (uint)source[1] << 8;
        value |= source[2];
        return value;
    }

    /// <summary>
    /// Writes an unsigned 24-bit integer value in big endian form.
    /// </summary>
    /// <param name="target">The buffer to write the value to.</param>
    /// <param name="value">The value.</param>
    public static void WriteUInt24BE(Span<byte> target, uint value)
    {
        target[0] = (byte)(value >>> 16);
        target[1] = (byte)(value >>> 8);
        target[2] = (byte)value;
    }
}
