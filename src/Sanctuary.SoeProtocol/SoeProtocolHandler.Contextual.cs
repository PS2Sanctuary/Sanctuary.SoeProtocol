﻿using Sanctuary.SoeProtocol.Objects;
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
    private readonly byte[] _contextualSendBuffer;
    private long _lastReceivedPacketTick;

    /// <summary>
    /// Sends a contextual packet.
    /// </summary>
    /// <param name="opCode">The OP code of the packet to send.</param>
    /// <param name="packetData">The packet data, not including the OP code.</param>
    internal void SendContextualPacket(SoeOpCode opCode, ReadOnlySpan<byte> packetData)
    {
        int extraBytes = sizeof(SoeOpCode)
            + (SessionParams.IsCompressionEnabled ? 1 : 0)
            + SessionParams.CrcLength;

        if (packetData.Length + extraBytes > SessionParams.RemoteUdpLength)
            throw new InvalidOperationException("Cannot send a packet larger than the remote UDP length");

        BinaryWriter writer = new(_contextualSendBuffer);

        writer.WriteUInt16BE((ushort)opCode);
        if (SessionParams.IsCompressionEnabled)
            writer.WriteBool(false); // Compression is not implemented at the moment
        writer.WriteBytes(packetData);
        AppendCrc(ref writer, SessionParams.CrcSeed, SessionParams.CrcLength);

        _networkWriter.Send(writer.Consumed);
    }

    private void HandleContextualPacket(SoeOpCode opCode, Span<byte> packetData)
    {
        MemoryStream? decompressedData = null;

        if (SessionParams.IsCompressionEnabled)
        {
            bool isCompressed = packetData[0] > 0;
            packetData = packetData[1..];

            if (isCompressed)
            {
                decompressedData = Decompress(packetData, _spanPool);
                packetData = decompressedData.GetBuffer()
                    .AsSpan(0, (int)decompressedData.Length);
            }
        }

        HandleContextualPacketInternal(opCode, packetData);
        decompressedData?.Dispose();
    }

    private void HandleContextualPacketInternal(SoeOpCode opCode, Span<byte> packetData)
    {
        // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
        switch (opCode)
        {
            case SoeOpCode.MultiPacket:
            {
                int offset = 0;
                while (offset < packetData.Length)
                {
                    int subPacketLength = (int)MultiPacketUtils.ReadVariableLength(packetData, ref offset);
                    if (subPacketLength < 1 || subPacketLength > packetData.Length - offset)
                    {
                        TerminateSession(DisconnectReason.CorruptPacket, true);
                        return;
                    }

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
                OutOfOrder ooo = OutOfOrder.Deserialize(packetData);
                _dataOutputChannel.NotifyOfOutOfOrder(ooo);
                break;
            }
            case SoeOpCode.Acknowledge:
            {
                Acknowledge ack = Acknowledge.Deserialize(packetData);
                _dataOutputChannel.NotifyOfAcknowledge(ack);
                break;
            }
        }
    }

    private void SendHeartbeatIfRequired()
    {
        bool maySendHeartbeat = Mode is SessionMode.Client
            && State is SessionState.Running
            && SessionParams.HeartbeatAfter != TimeSpan.Zero
            && Stopwatch.GetElapsedTime(_lastReceivedPacketTick) > SessionParams.HeartbeatAfter;

        if (maySendHeartbeat)
            SendContextualPacket(SoeOpCode.Heartbeat, Array.Empty<byte>());
    }
}
