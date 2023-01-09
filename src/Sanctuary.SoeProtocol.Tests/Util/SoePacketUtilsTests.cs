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
        const int CRC_SEED = 5;

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
}
