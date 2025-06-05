// ReSharper disable once CheckNamespace
namespace BinaryPrimitiveHelpers;

/// <summary>
/// Contains extension methods for the <see cref="BinaryPrimitiveReader"/> and <see cref="BinaryPrimitiveWriter"/>
/// structs.
/// </summary>
public static class BinaryPrimitiveHelperExtensions
{
    /// <summary>
    /// Reads an unsigned 24-bit integer in big endian format.
    /// </summary>
    /// <returns>A uint value.</returns>
    public static uint ReadUInt24BE(this ref BinaryPrimitiveReader reader)
    {
        uint value = 0;
        value |= (uint)reader.ReadByte() << 16;
        value |= (uint)reader.ReadByte() << 8;
        value |= reader.ReadByte();
        return value;
    }

    /// <summary>
    /// Writes an unsigned 24-bit integer value in big endian form.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
    /// <param name="value">The value.</param>
    public static void WriteUInt24BE(this ref BinaryPrimitiveWriter writer, uint value)
    {
        writer.WriteByte((byte)(value >>> 16));
        writer.WriteByte((byte)(value >>> 8));
        writer.WriteByte((byte)value);
    }
}
