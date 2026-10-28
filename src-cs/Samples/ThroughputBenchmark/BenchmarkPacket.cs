using System;
using System.Buffers.Binary;

namespace ThroughputBenchmark;

/// <summary>
/// Specifies the type of benchmark message.
/// </summary>
public enum BenchmarkMessageType : byte
{
    /// <summary>
    /// Handshake or invalid message type.
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Data message carrying sequence number and timestamp.
    /// </summary>
    Data = 0x01,

    /// <summary>
    /// Indicates completion of a benchmark stage.
    /// </summary>
    EndStage = 0x02
}

/// <summary>
/// Represents a benchmark packet header and framing helper.
/// </summary>
public readonly record struct BenchmarkPacket
(
    BenchmarkMessageType Type,
    int Stage,
    int Sequence,
    long Timestamp
)
{
    /// <summary>
    /// Header size in bytes: 1 (Type) + 2 (Stage) + 4 (Sequence) + 8 (Timestamp) = 15 bytes.
    /// </summary>
    public const int HeaderSize = 15;

    /// <summary>
    /// Writes the benchmark packet header into the target buffer.
    /// </summary>
    /// <param name="buffer">Target destination buffer.</param>
    public void WriteTo(Span<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
            throw new ArgumentException($"Buffer too small. Must be at least {HeaderSize} bytes.", nameof(buffer));

        buffer[0] = (byte)Type;
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(1, 2), (short)Stage);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(3, 4), Sequence);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(7, 8), Timestamp);
    }

    /// <summary>
    /// Attempts to read a benchmark packet header from the source buffer.
    /// </summary>
    /// <param name="buffer">Source buffer.</param>
    /// <param name="packet">Decoded benchmark packet.</param>
    /// <returns><c>true</c> if successfully decoded; otherwise <c>false</c>.</returns>
    public static bool TryRead(ReadOnlySpan<byte> buffer, out BenchmarkPacket packet)
    {
        if (buffer.Length < HeaderSize)
        {
            packet = default;
            return false;
        }

        BenchmarkMessageType type = (BenchmarkMessageType)buffer[0];
        short stage = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(1, 2));
        int sequence = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(3, 4));
        long timestamp = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(7, 8));

        packet = new BenchmarkPacket(type, stage, sequence, timestamp);
        return true;
    }
}
