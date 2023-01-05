using System;
using System.Buffers.Binary;
using System.Text;

namespace Sanctuary.SoeProtocol.Util;

public ref struct BinaryWriter
{
    public Span<byte> Span { get; }
    public int Offset { get; private set; }

    public BinaryWriter()
    {
        throw new InvalidOperationException("A binary writer must be initialized with an underlying span");
    }

    public BinaryWriter(Span<byte> span)
    {
        Span = span;
        Offset = 0;
    }

    public void Advance(int amount)
        => Offset += amount;

    public void Rewind(int amount)
        => Offset -= amount;

    public void WriteByte(byte value)
        => Span[Offset++] = value;

    public void WriteBool(bool value)
        => Span[Offset++] = (byte)(value ? 1 : 0);

    public void WriteUInt16BE(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(Span[Offset..], value);
        Offset += sizeof(ushort);
    }

    public void WriteUInt16LE(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(Span[Offset..], value);
        Offset += sizeof(ushort);
    }

    public void WriteInt16BE(short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(Span[Offset..], value);
        Offset += sizeof(short);
    }

    public void WriteInt16LE(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(Span[Offset..], value);
        Offset += sizeof(short);
    }

    public void WriteUInt24BE(uint value)
    {
        WriteByte((byte)(value >>> 16));
        WriteByte((byte)(value >>> 8));
        WriteByte((byte)value);
    }

    public void WriteUInt32BE(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(Span[Offset..], value);
        Offset += sizeof(uint);
    }

    public void WriteUInt32LE(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Span[Offset..], value);
        Offset += sizeof(uint);
    }

    public void WriteInt32BE(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(Span[Offset..], value);
        Offset += sizeof(int);
    }

    public void WriteInt32LE(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Span[Offset..], value);
        Offset += sizeof(int);
    }

    public void WriteUInt64BE(ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(Span[Offset..], value);
        Offset += sizeof(ulong);
    }

    public void WriteUInt64LE(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Span[Offset..], value);
        Offset += sizeof(ulong);
    }

    public void WriteInt64BE(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(Span[Offset..], value);
        Offset += sizeof(long);
    }

    public void WriteInt64LE(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(Span[Offset..], value);
        Offset += sizeof(long);
    }

    public void WriteBytes(ReadOnlySpan<byte> buffer)
    {
        buffer.CopyTo(Span[Offset..]);
        Offset += buffer.Length;
    }

    public void WriteStringNullTerminated(string value, Encoding? encoding = null)
    {
        encoding ??= Encoding.ASCII;
        encoding.GetBytes(value, Span[Offset..]);
        Offset += value.Length;
        WriteByte(0);
    }
}
