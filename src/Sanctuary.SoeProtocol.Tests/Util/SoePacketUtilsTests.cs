using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers.Binary;

namespace Sanctuary.SoeProtocol.Tests.Util;

public class SoePacketUtilsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AppendCrc_Correct_ForAllValidLengths(byte crcLength)
    {
        const uint CRC_SEED = 5;

        byte[] buffer = new byte[4 + crcLength];
        BinaryWriter writer = new(buffer);
        writer.WriteUInt32BE(454653524);

        uint expectedCrc = Crc32.Hash(buffer.AsSpan(0, 4), CRC_SEED);
        byte[] expectedBuffer = new byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(expectedBuffer, expectedCrc);
        SoePacketUtils.AppendCrc(ref writer, CRC_SEED, crcLength);

        for (int i = 0; i < crcLength; i++)
            Assert.Equal(expectedBuffer[4 - crcLength + i], buffer[4 + i]);
    }

    [Fact]
    public void ValidatePacket_InvalidatesPacket_WithShortOpCode()
    {
        byte[] packet = { (byte)SoeOpCode.SessionRequest };
        SoePacketValidationResult result = SoePacketUtils.ValidatePacket(packet, GetSessionParams(), out _);
        Assert.Equal(SoePacketValidationResult.TooShort, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(byte.MaxValue)]
    public void ValidatePacket_InvalidatesPacket_WithInvalidOpCode(byte opCode)
    {
        byte[] packet = { 0, opCode };
        SoePacketValidationResult result = SoePacketUtils.ValidatePacket(packet, GetSessionParams(), out _);
        Assert.Equal(SoePacketValidationResult.InvalidOpCode, result);
    }

    [Fact]
    public void ValidatePacket_Validates_OpOnlyContextlessPacket()
    {
        byte[] packet = { 0, (byte)SoeOpCode.UnknownSender };
        SoePacketValidationResult result = SoePacketUtils.ValidatePacket(packet, GetSessionParams(), out _);
        Assert.Equal(SoePacketValidationResult.Valid, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ValidatePacket_Validates_ValidContextualPacketForAllCrcLengths(byte crcLength)
    {
        SessionParameters sessionParams = GetSessionParams(crcLength);
        byte[] packet = new byte[sizeof(SoeOpCode) + Acknowledge.Size + crcLength];
        BinaryWriter writer = new(packet);

        writer.WriteUInt16BE((ushort)SoeOpCode.Acknowledge);
        new Acknowledge(10).Serialize(packet.AsSpan(sizeof(SoeOpCode)));
        writer.Advance(Acknowledge.Size);
        SoePacketUtils.AppendCrc(ref writer, sessionParams.CrcSeed, crcLength);

        SoePacketValidationResult result = SoePacketUtils.ValidatePacket(packet, sessionParams, out _);
        Assert.Equal(SoePacketValidationResult.Valid, result);
    }

    [Fact]
    public void ValidatePacket_Invalidates_ContextualPacketWithIncorrectCrc()
    {
        const byte CRC_LENGTH = 2;

        SessionParameters sessionParams = GetSessionParams(CRC_LENGTH);
        byte[] packet = new byte[sizeof(SoeOpCode) + Acknowledge.Size + CRC_LENGTH];
        BinaryWriter writer = new(packet);

        writer.WriteUInt16BE((ushort)SoeOpCode.Acknowledge);
        new Acknowledge(10).Serialize(packet.AsSpan(sizeof(SoeOpCode)));
        writer.Advance(Acknowledge.Size);
        SoePacketUtils.AppendCrc(ref writer, 0, CRC_LENGTH);

        SoePacketValidationResult result = SoePacketUtils.ValidatePacket(packet, sessionParams, out _);
        Assert.Equal(SoePacketValidationResult.CrcMismatch, result);
    }

    private static SessionParameters GetSessionParams(byte crcLength = 0)
        => new("TestProtocol")
        {
            IsCompressionEnabled = false,
            CrcSeed = 5,
            CrcLength = crcLength
        };
}
