using System;
using System.Buffers.Binary;
using System.Text;

namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// A binary reader implementation optimised for allocation-free reads
/// of primitive types off an underlying <see cref="ReadOnlySpan{T}"/>.
/// </summary>
public ref struct BinaryReader
{
    /// <summary>
    /// The underlying span.
    /// </summary>
    public ReadOnlySpan<byte> Span { get; }

    /// <summary>
    /// The offset into the <see cref="Span"/> that the reader is at.
    /// </summary>
    public int Offset { get; private set; }

    /// <summary>
    /// Indicates whether the reader has consumed the entirety of the <see cref="Span"/>
    /// </summary>
    public bool IsAtEnd => Offset == Span.Length;

    /// <summary>
    /// This constructor is invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public BinaryReader()
    {
        throw new InvalidOperationException("A binary reader must be initialized with an underlying span");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryReader"/> struct.
    /// </summary>
    /// <param name="span">The underlying span to read data from.</param>
    public BinaryReader(ReadOnlySpan<byte> span)
    {
        Span = span;
        Offset = 0;
    }

    /// <summary>
    /// Advances the <see cref="Offset"/> of the reader by the given amount.
    /// </summary>
    /// <param name="amount">The amount to advance the offset by.</param>
    public void Advance(int amount)
        => Offset += amount;

    /// <summary>
    /// Rewinds the <see cref="Offset"/> of the reader by the given amount.
    /// </summary>
    /// <param name="amount">The amount to rewind the offset by.</param>
    public void Rewind(int amount)
        => Offset -= amount;

    /// <summary>
    /// Reads a byte.
    /// </summary>
    /// <returns>A byte value.</returns>
    public byte ReadByte()
        => Span[Offset++];

    /// <summary>
    /// Reads a boolean.
    /// </summary>
    /// <returns>A boolean value</returns>
    /// <exception cref="Exception">Thrown if the read value was not a valid boolean.</exception>
    public bool ReadBool()
        => Span[Offset++] switch
        {
            0 => false,
            1 => true,
            _ => throw new Exception("Warning: attempted to read 'boolean' value other than 0 or 1")
        };

    /// <summary>
    /// Reads a unsigned 16-bit integer in big endian format.
    /// </summary>
    /// <returns>A ushort value</returns>
    public ushort ReadUInt16BE()
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(Span[Offset..]);
        Offset += sizeof(ushort);
        return value;
    }

    /// <summary>
    /// Reads a unsigned 16-bit integer in little endian format.
    /// </summary>
    /// <returns>A ushort value.</returns>
    public ushort ReadUInt16LE()
    {
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(Span[Offset..]);
        Offset += sizeof(ushort);
        return value;
    }

    /// <summary>
    /// Reads a signed 16-bit integer in big endian format.
    /// </summary>
    /// <returns>A short value.</returns>
    public short ReadInt16BE()
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(Span[Offset..]);
        Offset += sizeof(short);
        return value;
    }

    /// <summary>
    /// Reads a signed 16-bit integer in little endian format.
    /// </summary>
    /// <returns>A short value.</returns>
    public short ReadInt16LE()
    {
        short value = BinaryPrimitives.ReadInt16LittleEndian(Span[Offset..]);
        Offset += sizeof(short);
        return value;
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer in big endian format.
    /// </summary>
    /// <returns>A uint value.</returns>
    public uint ReadUInt32BE()
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(Span[Offset..]);
        Offset += sizeof(uint);
        return value;
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer in little endian format.
    /// </summary>
    /// <returns>A uint value.</returns>
    public uint ReadUInt32LE()
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(Span[Offset..]);
        Offset += sizeof(uint);
        return value;
    }

    /// <summary>
    /// Reads a signed 32-bit integer in big endian format.
    /// </summary>
    /// <returns>A int value.</returns>
    public int ReadInt32BE()
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(Span[Offset..]);
        Offset += sizeof(int);
        return value;
    }

    /// <summary>
    /// Reads a signed 32-bit integer in little endian format.
    /// </summary>
    /// <returns>A int value.</returns>
    public int ReadInt32LE()
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(Span[Offset..]);
        Offset += sizeof(int);
        return value;
    }

    /// <summary>
    /// Reads an unsigned 24-bit integer in big endian format.
    /// </summary>
    /// <returns>A uint value.</returns>
    public uint ReadUInt24BE()
    {
        uint value = 0;
        value |= (uint)ReadByte() << 16;
        value |= (uint)ReadByte() << 8;
        value |= ReadByte();
        return value;
    }

    /// <summary>
    /// Reads an unsigned 64-bit integer in big endian format.
    /// </summary>
    /// <returns>A ulong value.</returns>
    public ulong ReadUInt64BE()
    {
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(Span[Offset..]);
        Offset += sizeof(ulong);
        return value;
    }

    /// <summary>
    /// Reads an unsigned 64-bit integer in little endian format.
    /// </summary>
    /// <returns>A ulong value.</returns>
    public ulong ReadUInt64LE()
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Span[Offset..]);
        Offset += sizeof(ulong);
        return value;
    }

    /// <summary>
    /// Reads a signed 64-bit integer in big endian format.
    /// </summary>
    /// <returns>A long value.</returns>
    public long ReadInt64BE()
    {
        long value = BinaryPrimitives.ReadInt64BigEndian(Span[Offset..]);
        Offset += sizeof(long);
        return value;
    }

    /// <summary>
    /// Reads a signed 64-bit integer in little endian format.
    /// </summary>
    /// <returns>A long value.</returns>
    public long ReadInt64LE()
    {
        long value = BinaryPrimitives.ReadInt64LittleEndian(Span[Offset..]);
        Offset += sizeof(long);
        return value;
    }

    /// <summary>
    /// Reads a signed 32-bit float in little endian format.
    /// </summary>
    /// <returns>A float value.</returns>
    public float ReadSingleLE()
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(Span[Offset..]);
        Offset += sizeof(float);
        return value;
    }

    /// <summary>
    /// Reads a span of bytes.
    /// </summary>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>A span of the bytes read.</returns>
    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        ReadOnlySpan<byte> slice = Span.Slice(Offset, length);
        Offset += length;
        return slice;
    }

    /// <summary>
    /// Reads a null-terminated string.
    /// </summary>
    /// <param name="encoding">The encoding to parse the string using.</param>
    /// <returns>A string value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no null-terminator was present in the underlying buffer.
    /// </exception>
    public string ReadStringNullTerminated(Encoding? encoding = null)
    {
        encoding ??= Encoding.ASCII;

        int terminatorIndex = Span[Offset..].IndexOf((byte)0);
        if (terminatorIndex == -1)
            throw new InvalidOperationException("Null-terminator not found");

        string value = encoding.GetString(Span.Slice(Offset, terminatorIndex));
        Offset += terminatorIndex + 1;

        return value;
    }
}
