using System;
using System.Buffers.Binary;
using System.Text;

namespace Sanctuary.SoeProtocol.Util;

public ref struct BinaryReader
{
    public ReadOnlySpan<byte> Span { get; }
    public int Offset { get; private set; }
    public bool IsAtEnd => Offset == Span.Length;

    public BinaryReader()
    {
        throw new InvalidOperationException("A binary reader must be initialized with an underlying span");
    }

    public BinaryReader(ReadOnlySpan<byte> span)
    {
        Span = span;
        Offset = 0;
    }

    public void Advance(int amount)
        => Offset += amount;

    public void Rewind(int amount)
        => Offset -= amount;

    public byte ReadByte()
        => Span[Offset++];

    public bool ReadBool()
        => Span[Offset++] switch
        {
            0 => false,
            1 => true,
            _ => throw new Exception("Warning: attempted to read 'boolean' value other than 0 or 1")
        };

    public ushort ReadUInt16BE()
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(Span[Offset..]);
        Offset += sizeof(ushort);
        return value;
    }

    public ushort ReadUInt16LE()
    {
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(Span[Offset..]);
        Offset += sizeof(ushort);
        return value;
    }

    public short ReadInt16BE()
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(Span[Offset..]);
        Offset += sizeof(short);
        return value;
    }

    public short ReadInt16LE()
    {
        short value = BinaryPrimitives.ReadInt16LittleEndian(Span[Offset..]);
        Offset += sizeof(short);
        return value;
    }

    public uint ReadUInt32BE()
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(Span[Offset..]);
        Offset += sizeof(uint);
        return value;
    }

    public uint ReadUInt32LE()
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(Span[Offset..]);
        Offset += sizeof(uint);
        return value;
    }

    public int ReadInt32BE()
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(Span[Offset..]);
        Offset += sizeof(int);
        return value;
    }

    public int ReadInt32LE()
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(Span[Offset..]);
        Offset += sizeof(int);
        return value;
    }

    public uint ReadUInt24BE()
    {
        uint value = 0;
        value |= (uint)ReadByte() << 16;
        value |= (uint)ReadByte() << 8;
        value |= ReadByte();
        return value;
    }

    public ulong ReadUInt64BE()
    {
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(Span[Offset..]);
        Offset += sizeof(ulong);
        return value;
    }

    public ulong ReadUInt64LE()
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Span[Offset..]);
        Offset += sizeof(ulong);
        return value;
    }

    public long ReadInt64BE()
    {
        long value = BinaryPrimitives.ReadInt64BigEndian(Span[Offset..]);
        Offset += sizeof(long);
        return value;
    }

    public long ReadInt64LE()
    {
        long value = BinaryPrimitives.ReadInt64LittleEndian(Span[Offset..]);
        Offset += sizeof(long);
        return value;
    }

    public float ReadSingleLE()
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(Span[Offset..]);
        Offset += sizeof(float);
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        ReadOnlySpan<byte> slice = Span.Slice(Offset, length);
        Offset += length;
        return slice;
    }

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
