using Microsoft.IO;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// Contains utility methods for working with SOE protocol packets.
/// </summary>
public static class SoePacketUtils
{
    private static readonly RecyclableMemoryStreamManager _msManager = new();

    /// <summary>
    /// Gets a value indicating whether the given OP code represents a
    /// packet that is used outside the context of a session.
    /// </summary>
    /// <param name="opCode">The OP code.</param>
    /// <returns><c>True</c> if the packet is session-less.</returns>
    public static bool IsContextlessPacket(SoeOpCode opCode)
        => opCode is SoeOpCode.SessionRequest
            or SoeOpCode.SessionResponse
            or SoeOpCode.UnknownSender
            or SoeOpCode.RemapConnection;

    /// <summary>
    /// Gets a valid indicating whether the given OP code represents a
    /// packet that must be used within the context of a session.
    /// </summary>
    /// <param name="opCode">The OP code.</param>
    /// <returns><c>True</c> if the packet requires a session.</returns>
    public static bool IsContextualPacket(SoeOpCode opCode)
        => opCode is SoeOpCode.MultiPacket
            or SoeOpCode.Disconnect
            or SoeOpCode.Heartbeat
            or SoeOpCode.NetStatusRequest
            or SoeOpCode.NetStatusResponse
            or SoeOpCode.ReliableData
            or SoeOpCode.ReliableDataFragment
            or SoeOpCode.OutOfOrder
            or SoeOpCode.Acknowledge;

    /// <summary>
    /// Appends a CRC check value to the given <see cref="BinaryWriter"/>.
    /// The entirety of the writer's buffer is used to calculate the check.
    /// </summary>
    /// <param name="writer">The writer to append to.</param>
    /// <param name="crcSeed">The CRC seed to use.</param>
    /// <param name="crcLength">The number of bytes to store the CRC check value in.</param>
    public static void AppendCrc(ref BinaryWriter writer, uint crcSeed, byte crcLength)
    {
        if (crcLength is 0)
            return;

        uint crcValue = Crc32.Hash(writer.Span[..writer.Offset], crcSeed);
        switch (crcLength)
        {
            case 1:
                writer.WriteByte((byte)crcValue);
                break;
            case 2:
                writer.WriteUInt16BE((ushort)crcValue);
                break;
            case 3:
                writer.WriteUInt24BE(crcValue);
                break;
            default:
                writer.WriteUInt32BE(crcValue);
                break;
        }
    }

    /// <summary>
    /// Validates that a buffer 'most likely' contains an SOE protocol packet.
    /// </summary>
    /// <param name="packetData">The buffer to validate.</param>
    /// <param name="sessionParams">The current session parameters.</param>
    /// <param name="opCode">The OP code of the packet, if valid.</param>
    /// <returns>The result of the validation.</returns>
    public static SoePacketValidationResult ValidatePacket
    (
        ReadOnlySpan<byte> packetData,
        SessionParameters sessionParams,
        out SoeOpCode opCode
    )
    {
        opCode = SoeOpCode.Invalid;

        if (packetData.Length < sizeof(SoeOpCode))
            return SoePacketValidationResult.TooShort;

        opCode = (SoeOpCode)BinaryPrimitives.ReadUInt16BigEndian(packetData);
        if (!IsContextlessPacket(opCode) && !IsContextualPacket(opCode))
            return SoePacketValidationResult.InvalidOpCode;

        if (GetPacketMinimumLength(opCode, sessionParams) < packetData.Length)
            return SoePacketValidationResult.TooShort;

        if (IsContextlessPacket(opCode) || sessionParams.CrcLength is 0)
            return SoePacketValidationResult.Valid;

        uint crc = sessionParams.CrcLength switch
        {
            1 => packetData[^1],
            2 => BinaryPrimitives.ReadUInt16BigEndian(packetData[^2..]),
            3 => new BinaryReader(packetData[^3..]).ReadUInt24BE(),
            _ => BinaryPrimitives.ReadUInt32BigEndian(packetData[^4..])
        };

        return Crc32.Hash(packetData[..^sessionParams.CrcLength], sessionParams.CrcSeed) == crc
            ? SoePacketValidationResult.Valid
            : SoePacketValidationResult.CrcMismatch;
    }

    public static int GetPacketMinimumLength(SoeOpCode opCode, SessionParameters sessionParams)
        => opCode switch
        {
            SoeOpCode.SessionRequest => SessionRequest.MinSize,
            SoeOpCode.SessionResponse => SessionResponse.Size,
            SoeOpCode.MultiPacket => GetContextualPacketPadding(sessionParams) + 2, // Data length + first byte of data,
            SoeOpCode.Disconnect => GetContextualPacketPadding(sessionParams) + Disconnect.Size,
            SoeOpCode.Heartbeat => GetContextualPacketPadding(sessionParams),
            SoeOpCode.NetStatusRequest => GetContextualPacketPadding(sessionParams),
            SoeOpCode.NetStatusResponse => GetContextualPacketPadding(sessionParams),
            SoeOpCode.ReliableData or SoeOpCode.ReliableDataFragment => GetContextualPacketPadding(sessionParams)
                + sizeof(ushort) + 1, // Sequence + first byte of data,
            SoeOpCode.OutOfOrder => GetContextualPacketPadding(sessionParams) + OutOfOrder.Size,
            SoeOpCode.Acknowledge => GetContextualPacketPadding(sessionParams) + Acknowledge.Size,
            SoeOpCode.UnknownSender => UnknownSender.Size,
            SoeOpCode.RemapConnection => RemapConnection.Size,
            _ => throw new ArgumentOutOfRangeException(nameof(opCode), opCode, "Invalid OP code")
        };

    public static unsafe MemoryStream Decompress(ReadOnlySpan<byte> input, NativeSpanPool pool)
    {
        NativeSpan span = pool.Rent();
        input.CopyTo(span.Span);
        using UnmanagedMemoryStream ums = new(span._ptr, span.Span.Length, span.Span.Length, FileAccess.Read);

        using ZLibStream zs = new(ums, CompressionMode.Decompress);
        using MemoryStream output = _msManager.GetStream();
        zs.CopyTo(output);

        pool.Return(span);
        return output;
    }

    private static int GetContextualPacketPadding(SessionParameters sessionParams)
        => sizeof(SoeOpCode)
            + (sessionParams.IsCompressionEnabled ? 1 : 0)
            + sessionParams.CrcLength;
}
