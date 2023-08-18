using System;
using System.Buffers.Binary;
using System.Text;

namespace Sanctuary.SoeProtocol.Util;

public ref struct BinaryWriter
{
    /// <summary>
    /// Gets the underlying span being written to.
    /// </summary>
    public Span<byte> Span { get; }

    /// <summary>
    /// Gets the current offset at which data is being written.
    /// </summary>
    public int Offset { get; private set; }

    /// <summary>
    /// Gets a <see cref="Span"/> over the remaining space left
    /// in the writer.
    /// </summary>
    public Span<byte> RemainingSpan => Span[Offset..];

    /// <summary>
    /// Gets a <see cref="Span"/> over the space already written to.
    /// </summary>
    public Span<byte> Consumed => Span[..Offset];

    /// <summary>
    /// Use of <see cref="BinaryWriter"/>'s default constructor is invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public BinaryWriter()
    {
        throw new InvalidOperationException("A binary writer must be initialized with an underlying span");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryWriter"/> struct.
    /// </summary>
    /// <param name="span">The span to write to.</param>
    public BinaryWriter(Span<byte> span)
    {
        Span = span;
        Offset = 0;
    }

    /// <summary>
    /// Advances the offset of the writer.
    /// </summary>
    /// <param name="amount">The number of bytes to advance by.</param>
    public void Advance(int amount)
        => Offset += amount;

    /// <summary>
    /// Rewinds the offset of the writer.
    /// </summary>
    /// <param name="amount">The number of bytes to rewind by.</param>
    public void Rewind(int amount)
        => Offset -= amount;

    /// <summary>
    /// Writes a byte value.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteByte(byte value)
        => Span[Offset++] = value;

    /// <summary>
    /// Writes a boolean value.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteBool(bool value)
        => Span[Offset++] = (byte)(value ? 1 : 0);

    /// <summary>
    /// Writes an unsigned 16-bit integer value in big endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteUInt16BE(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(Span[Offset..], value);
        Offset += sizeof(ushort);
    }

    /// <summary>
    /// Writes an unsigned 16-bit integer value in little endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteUInt16LE(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(Span[Offset..], value);
        Offset += sizeof(ushort);
    }

    /// <summary>
    /// Writes a signed 16-bit integer value in big endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteInt16BE(short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(Span[Offset..], value);
        Offset += sizeof(short);
    }

    /// <summary>
    /// Writes a signed 16-bit integer value in little endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteInt16LE(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(Span[Offset..], value);
        Offset += sizeof(short);
    }

    /// <summary>
    /// Writes an unsigned 24-bit integer value in big endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteUInt24BE(uint value)
    {
        WriteByte((byte)(value >>> 16));
        WriteByte((byte)(value >>> 8));
        WriteByte((byte)value);
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer value in big endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteUInt32BE(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(Span[Offset..], value);
        Offset += sizeof(uint);
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer value in little endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteUInt32LE(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Span[Offset..], value);
        Offset += sizeof(uint);
    }

    /// <summary>
    /// Writes a signed 32-bit integer value in big endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteInt32BE(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(Span[Offset..], value);
        Offset += sizeof(int);
    }

    /// <summary>
    /// Writes a signed 32-bit integer value in little endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteInt32LE(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Span[Offset..], value);
        Offset += sizeof(int);
    }

    /// <summary>
    /// Writes an unsigned 64-bit integer value in big endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteUInt64BE(ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(Span[Offset..], value);
        Offset += sizeof(ulong);
    }

    /// <summary>
    /// Writes an unsigned 64-bit integer value in little endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteUInt64LE(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Span[Offset..], value);
        Offset += sizeof(ulong);
    }

    /// <summary>
    /// Writes a signed 64-bit integer value in big endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteInt64BE(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(Span[Offset..], value);
        Offset += sizeof(long);
    }

    /// <summary>
    /// Writes a signed 64-bit integer value in little endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteInt64LE(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(Span[Offset..], value);
        Offset += sizeof(long);
    }

    /// <summary>
    /// Writes a 32-bit floating point value in little endian form.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteSingleLE(float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(Span[Offset..], value);
        Offset += sizeof(float);
    }

    /// <summary>
    /// Copies the given bytes into the underlying span.
    /// </summary>
    /// <param name="buffer">The buffer to copy.</param>
    public void WriteBytes(ReadOnlySpan<byte> buffer)
    {
        buffer.CopyTo(Span[Offset..]);
        Offset += buffer.Length;
    }

    /// <summary>
    /// Writes a null-terminated string.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <param name="encoding">The encoding to represent the string in.</param>
    public void WriteStringNullTerminated(string value, Encoding? encoding = null)
    {
        encoding ??= Encoding.ASCII;
        encoding.GetBytes(value, Span[Offset..]);
        Offset += value.Length;
        WriteByte(0);
    }
}
