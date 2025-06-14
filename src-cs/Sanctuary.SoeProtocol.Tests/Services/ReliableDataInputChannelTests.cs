﻿using BinaryPrimitiveHelpers;
using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Tests.Mocks;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Sanctuary.SoeProtocol.Tests.Services;

public class ReliableDataInputChannelTests : IDisposable
{
    private const int DATA_LENGTH = 16;

    private static readonly NativeSpanPool SpanPool = new(DATA_LENGTH + 6, 8); // + 6, space for sequence + CompleteDataLength

    private readonly Queue<byte[]> _dataOutputQueue;
    private readonly MockNetworkInterface _netInterface;
    private readonly SessionParameters _sessionParams;
    private readonly ReliableDataInputChannel _channel;

    public ReliableDataInputChannelTests()
    {
        _dataOutputQueue = new Queue<byte[]>();
        _netInterface = new MockNetworkInterface();

        _sessionParams = new SessionParameters
        {
            ApplicationProtocol = "TestProtocol",
            RemoteUdpLength = 512,
            AcknowledgeAllData = true,
            MaximumAcknowledgeDelay = TimeSpan.Zero
        };
        SoeProtocolHandler handler = new
        (
            null!,
            SessionMode.Client,
            _sessionParams,
            SpanPool,
            _netInterface,
            new MockApplicationProtocolHandler()
        );

        _channel = new ReliableDataInputChannel
        (
            handler,
            handler.SessionParams,
            handler.ApplicationParams,
            SpanPool,
            data => _dataOutputQueue.Enqueue(data.ToArray())
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestSequentialFragmentInsert(bool ackAll)
    {
        _sessionParams.AcknowledgeAllData = ackAll;

        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, out byte[] data0);
        byte[] fragment1 = GetDataFragment(1, null, out byte[] data1);
        byte[] fragment2 = GetDataFragment(2, null, out byte[] data2);

        _channel.HandleReliableDataFragment(fragment0);
        AssertCanPopAck(0, !ackAll);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment1);
        AssertCanPopAck(1, !ackAll);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment2);
        AssertCanPopAck(2, !ackAll);
        Assert.Single(_dataOutputQueue);

        Assert.Empty(_netInterface.SentData); // Check no superfluous acknowledgements have been sent

        int offset = 0;
        Span<byte> stitchedData = _dataOutputQueue.Dequeue();

        Assert.Equal(data0, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data1, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data2, stitchedData[offset..]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestNonSequentialFragmentInsert(bool ackAll)
    {
        _sessionParams.AcknowledgeAllData = ackAll;

        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, out byte[] data0);
        byte[] fragment1 = GetDataFragment(1, null, out byte[] data1);
        byte[] fragment2 = GetDataFragment(2, null, out byte[] data2);

        _channel.HandleReliableDataFragment(fragment2);
        AssertCanPopAck(2, false);

        _channel.HandleReliableDataFragment(fragment0);
        AssertCanPopAck(0, !ackAll);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment1);
        AssertCanPopAck(ackAll ? 1 : 2, !ackAll);
        Assert.Single(_dataOutputQueue);

        Assert.Empty(_netInterface.SentData); // Check no superfluous acknowledgements have been sent

        int offset = 0;
        Span<byte> stitchedData = _dataOutputQueue.Dequeue();

        Assert.Equal(data0, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data1, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data2, stitchedData[offset..]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestNonFragmentInsert(bool ackAll)
    {
        _sessionParams.AcknowledgeAllData = ackAll;

        byte[] packet0 = GetDataFragment(0, null, out byte[] data0);
        byte[] packet1 = GetDataFragment(1, null, out byte[] data1);
        byte[] packet2 = GetDataFragment(2, null, out byte[] data2);

        _channel.HandleReliableData(packet0);
        AssertCanPopAck(0, !ackAll);
        Assert.Equal(data0, _dataOutputQueue.Dequeue());

        _channel.HandleReliableData(packet2);
        AssertCanPopAck(2, false);

        _channel.HandleReliableData(packet1);
        AssertCanPopAck(ackAll ? 1 : 2, !ackAll);
        Assert.Equal(data1, _dataOutputQueue.Dequeue());
        Assert.Equal(data2, _dataOutputQueue.Dequeue());

        Assert.Empty(_netInterface.SentData); // Check no superfluous acknowledgements have been sent
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestFragmentedInsertOfTwoDatas(bool ackAll)
    {
        _sessionParams.AcknowledgeAllData = ackAll;

        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 2, out byte[] data0);
        byte[] fragment1 = GetDataFragment(1, null, out byte[] data1);
        byte[] fragment2 = GetDataFragment(2, DATA_LENGTH * 2, out byte[] data2);
        byte[] fragment3 = GetDataFragment(3, null, out byte[] data3);

        _channel.HandleReliableDataFragment(fragment0);
        AssertCanPopAck(0, !ackAll);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment1);
        AssertCanPopAck(1, !ackAll);
        Span<byte> actualData = _dataOutputQueue.Dequeue();
        Assert.Equal(data0, actualData[..DATA_LENGTH]);
        Assert.Equal(data1, actualData[DATA_LENGTH..]);

        _channel.HandleReliableDataFragment(fragment3);
        AssertCanPopAck(3, false);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment2);
        AssertCanPopAck(ackAll ? 2 : 3, !ackAll);
        actualData = _dataOutputQueue.Dequeue();
        Assert.Equal(data2, actualData[..DATA_LENGTH]);
        Assert.Equal(data3, actualData[DATA_LENGTH..]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestSequenceWaitingOnData(bool ackAll)
    {
        _sessionParams.AcknowledgeAllData = ackAll;

        byte[] packet0 = GetDataFragment(0, null, out byte[] data0);
        byte[] fragment1 = GetDataFragment(1, DATA_LENGTH * 2, out byte[] data1);
        byte[] fragment2 = GetDataFragment(2, null, out byte[] data2);

        _channel.HandleReliableDataFragment(fragment1);
        _channel.HandleReliableDataFragment(fragment2);
        AssertCanPopAck(1, false);
        AssertCanPopAck(2, false);

        _channel.HandleReliableData(packet0);
        AssertCanPopAck(ackAll ? 0 : 2, !ackAll);
        Assert.Equal(data0, _dataOutputQueue.Dequeue());

        Span<byte> stitched = _dataOutputQueue.Dequeue();
        Assert.Equal(data1, stitched[..DATA_LENGTH].ToArray());
        Assert.Equal(data2, stitched[DATA_LENGTH..].ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestMultiData(bool ackAll)
    {
        _sessionParams.AcknowledgeAllData = ackAll;

        int multiLen = sizeof(ushort) // Sequence
            + DataUtils.MULTI_DATA_INDICATOR.Length
            + 2 // Two packets of len 1
            + MultiPacketUtils.GetVariableLengthSize(1) * 2;
        byte[] multiBuffer = new byte[multiLen];

        // Fill out the data buffer
        int offset = sizeof(ushort);
        DataUtils.WriteMultiDataIndicator(multiBuffer, ref offset);
        DataUtils.WriteVariableLength(multiBuffer, 1, ref offset);
        multiBuffer[offset++] = 2;
        DataUtils.WriteVariableLength(multiBuffer, 1, ref offset);
        multiBuffer[offset] = 4;

        _channel.HandleReliableData(multiBuffer);
        AssertCanPopAck(0, !ackAll);
        Assert.Equal(new byte[] { 2 }, _dataOutputQueue.Dequeue());
        Assert.Equal(new byte[] { 4 }, _dataOutputQueue.Dequeue());

        multiBuffer[1] = 0x01; // Increment the sequence

        _channel.HandleReliableData(multiBuffer);
        AssertCanPopAck(1, !ackAll);
        Assert.Equal(new byte[] { 2 }, _dataOutputQueue.Dequeue());
        Assert.Equal(new byte[] { 4 }, _dataOutputQueue.Dequeue());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestAllTheData(bool ackAll)
    {
        _sessionParams.AcknowledgeAllData = ackAll;

        const int maxPacketLength = 512;
        const int maxNonFragmentDataLength = maxPacketLength - sizeof(ushort);
        ushort sequence = 0;
        Queue<byte[]> dataQueue = new();

        for (int i = 0; i < 256; i++)
        {
            byte[] data = new byte[i * 16];
            Random.Shared.NextBytes(data);
            dataQueue.Enqueue(data);

            if (data.Length < maxNonFragmentDataLength)
            {
                byte[] fragment = new byte[data.Length + sizeof(ushort)];
                BinaryPrimitives.WriteUInt16BigEndian(fragment, sequence);
                data.AsSpan().CopyTo(fragment.AsSpan(sizeof(ushort)));

                _channel.HandleReliableData(fragment);
                AssertCanPopAck(sequence++, !ackAll);
            }
            else
            {
                // Yeah... this is a mess -_-
                Span<byte> remaining = data;

                byte[] fragment = new byte[maxPacketLength];
                BinaryPrimitives.WriteUInt16BigEndian(fragment, sequence);
                BinaryPrimitives.WriteInt32BigEndian(fragment.AsSpan(sizeof(ushort)), data.Length);
                remaining[..(fragment.Length - sizeof(ushort) - sizeof(uint))]
                    .CopyTo(fragment.AsSpan(sizeof(ushort) + sizeof(uint)));
                remaining = remaining[(fragment.Length - sizeof(ushort) - sizeof(uint))..];

                _channel.HandleReliableDataFragment(fragment);
                AssertCanPopAck(sequence++, !ackAll);

                while (remaining.Length > 0)
                {
                    fragment = new byte[Math.Min(maxPacketLength, remaining.Length + sizeof(ushort))];
                    BinaryPrimitives.WriteUInt16BigEndian(fragment, sequence);
                    remaining[..(fragment.Length - sizeof(ushort))].CopyTo(fragment.AsSpan(sizeof(ushort)));
                    remaining = remaining[(fragment.Length - sizeof(ushort))..];

                    _channel.HandleReliableDataFragment(fragment);
                    AssertCanPopAck(sequence++, !ackAll);
                }
            }
        }

        while (_dataOutputQueue.Count is not 0)
            Assert.Equal(dataQueue.Dequeue(), _dataOutputQueue.Dequeue());
    }

    public static byte[] GetDataFragment
    (
        ushort sequence,
        uint? completeDataLength,
        out byte[] data,
        int dataLength = DATA_LENGTH
    )
    {
        int length = dataLength
            + sizeof(ushort) // Sequence
            + (completeDataLength is null ? 0 : sizeof(uint));

        data = new byte[dataLength];
        Random.Shared.NextBytes(data);

        byte[] buffer = new byte[length];
        BinaryPrimitiveWriter writer = new(buffer);

        writer.WriteUInt16BE(sequence);
        if (completeDataLength is not null)
            writer.WriteUInt32BE(completeDataLength.Value);
        writer.WriteBytes(data);

        return buffer;
    }

    private void AssertCanPopAck
    (
        int expectedSequence,
        bool expectAll
    )
    {
        // Run a tick to force ack-alls
        _channel.RunTick();

        Assert.NotEmpty(_netInterface.SentData);
        byte[] ack = _netInterface.SentData.Dequeue();

        SoeOpCode expectedCode = expectAll
            ? SoeOpCode.AcknowledgeAll
            : SoeOpCode.Acknowledge;
        Assert.Equal(expectedCode, SoePacketUtils.ReadSoeOpCode(ack));

        Acknowledge deserialized = Acknowledge.Deserialize(ack.AsSpan(sizeof(SoeOpCode)));
        Assert.Equal(expectedSequence, deserialized.Sequence);
    }

    public void Dispose()
    {
        _dataOutputQueue.Clear();
        _channel.Dispose();
        _netInterface.Dispose();
    }
}
