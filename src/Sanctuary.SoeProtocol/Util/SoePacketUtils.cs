namespace Sanctuary.SoeProtocol.Util;

/// <summary>
/// Contains utility methods for working with SOE protocol packets.
/// </summary>
public static class SoePacketUtils
{
    /// <summary>
    /// Gets a value indicating whether the given OP code
    /// represents a session-less packet.
    /// </summary>
    /// <param name="opCode">The OP code.</param>
    /// <returns><c>True</c> if the packet is session-less.</returns>
    public static bool IsSessionlessPacket(SoeOpCode opCode)
        => opCode is SoeOpCode.SessionRequest
            or SoeOpCode.SessionResponse
            or SoeOpCode.UnknownSender
            or SoeOpCode.RemapConnection;

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
}
