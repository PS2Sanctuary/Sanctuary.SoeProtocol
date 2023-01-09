using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using static Sanctuary.SoeProtocol.Util.SoePacketUtils;
using BinaryWriter = Sanctuary.SoeProtocol.Util.BinaryWriter;

namespace Sanctuary.SoeProtocol;

public partial class SoeProtocolHandler
{
    private long _lastReceivedContextualPacketTick;

    /// <summary>
    /// Sends a contextual packet.
    /// </summary>
    /// <param name="opCode">The OP code of the packet to send.</param>
    /// <param name="packetData">The packet data, not including the OP code.</param>
    internal void SendContextualPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        int extraBytes = sizeof(SoeOpCode)
            + (_sessionParams.IsCompressionEnabled ? 1 : 0)
            + _sessionParams.CrcLength;

        if (packetData.Length + extraBytes > _sessionParams.RemoteUdpLength)
            throw new InvalidOperationException("Cannot send a packet larger than the remote UDP length");

        NativeSpan sendBuffer = _spanPool.Rent();
        BinaryWriter writer = new(sendBuffer.FullSpan);

        writer.WriteUInt16BE((ushort)opCode);
        writer.WriteBool(false); // Compression is not implemented at the moment
        writer.WriteBytes(packetData);
        AppendCrc(ref writer, _sessionParams.CrcSeed, _sessionParams.CrcLength);

        _networkWriter.Send(sendBuffer.FullSpan);
        _spanPool.Return(sendBuffer);
    }

    private void HandleContextualPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        _lastReceivedContextualPacketTick = Stopwatch.GetTimestamp();
        MemoryStream? decompressedData = null;

        if (_sessionParams.IsCompressionEnabled)
        {
            if (packetData[0] > 0)
            {
                decompressedData = Decompress(packetData, _spanPool);
                packetData = decompressedData.GetBuffer()
                    .AsSpan(0, (int)decompressedData.Length);
            }
            else
            {
                packetData = packetData[1..];
            }
        }

        HandleContextualPacketInternal(opCode, packetData);
        decompressedData?.Dispose();
    }

    private void HandleContextualPacketInternal(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
        switch (opCode)
        {
            case SoeOpCode.MultiPacket:
            {
                int offset = 0;
                while (offset < packetData.Length)
                {
                    // TODO: We need to actually perform some length validation here (min/max)
                    int subPacketLength = (int)MultiPacketUtils.ReadVariableLength(packetData, ref offset);
                    SoeOpCode subPacketOpCode = (SoeOpCode)BinaryPrimitives.ReadUInt16BigEndian(packetData[offset..]);

                    HandleContextualPacketInternal
                    (
                        subPacketOpCode,
                        packetData.Slice(offset + sizeof(SoeOpCode), subPacketLength)
                    );
                    offset += subPacketLength;
                }

                break;
            }
            case SoeOpCode.Disconnect:
            {
                Disconnect disconnect = Disconnect.Deserialize(packetData);
                TerminateSession(disconnect.Reason, false);
                break;
            }
            case SoeOpCode.Heartbeat when Mode is SessionMode.Server:
            {
                SendContextualPacket(SoeOpCode.Heartbeat, Array.Empty<byte>());
                break;
            }
            case SoeOpCode.ReliableData:
            {
                _dataInputChannel.HandleReliableData(packetData);
                break;
            }
            case SoeOpCode.ReliableDataFragment:
            {
                _dataInputChannel.HandleReliableDataFragment(packetData);
                break;
            }
            case SoeOpCode.OutOfOrder:
            {
                // TODO: Handle in separate class
                break;
            }
            case SoeOpCode.Acknowledge:
            {
                // TODO: Handle in separate class
                break;
            }
        }
    }

    private void SendHeartbeatIfRequired()
    {
        bool maySendHeartbeat = Mode is SessionMode.Client
            && State is SessionState.Running
            && _sessionParams.HeartbeatAfter != TimeSpan.Zero
            && Stopwatch.GetElapsedTime(_lastReceivedContextualPacketTick) > _sessionParams.HeartbeatAfter;

        if (maySendHeartbeat)
            SendContextualPacket(SoeOpCode.Heartbeat, Array.Empty<byte>());
    }
}
