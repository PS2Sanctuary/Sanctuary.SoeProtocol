﻿using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Tests.Mocks;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Collections.Generic;

namespace Sanctuary.SoeProtocol.Tests.Services;

public class ReliableDataInputChannelTests : IDisposable
{
    private const int DATA_LENGTH = 16;

    private static readonly NativeSpanPool SpanPool = new(DATA_LENGTH + 6, 8); // + 6, space for sequence + CompleteDataLength

    private readonly Queue<byte[]> _dataOutputQueue;
    private readonly MockNetworkInterface _netInterface;
    private readonly ReliableDataInputChannel _channel;

    public ReliableDataInputChannelTests()
    {
        _dataOutputQueue = new Queue<byte[]>();
        _netInterface = new MockNetworkInterface();

        SoeProtocolHandler handler = new
        (
            SessionMode.Client,
            new SessionParameters
            {
                ApplicationProtocol = "TestProtocol",
                RemoteUdpLength = 512,
                AcknowledgeAllData = true
            },
            SpanPool,
            _netInterface,
            new MockApplicationProtocolHandler()
        );

        _channel = new ReliableDataInputChannel
        (
            handler,
            SpanPool,
            data => _dataOutputQueue.Enqueue(data.ToArray())
        );
    }

    [Fact]
    public void TestSequentialFragmentInsert()
    {
        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, out Span<byte> data0);
        byte[] fragment1 = GetDataFragment(1, null, out Span<byte> data1);
        byte[] fragment2 = GetDataFragment(2, null, out Span<byte> data2);

        _channel.HandleReliableDataFragment(fragment0);
        AssertCanPopAck(0, true);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment1);
        AssertCanPopAck(1, true);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment2);
        AssertCanPopAck(2, true);
        Assert.Single(_dataOutputQueue);

        Assert.Empty(_netInterface.SentData); // Check no superfluous acknowledgements have been sent

        int offset = 0;
        Span<byte> stitchedData = _dataOutputQueue.Dequeue();

        Assert.Equal(data0, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data1, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data2, stitchedData[offset..]);
    }

    [Fact]
    public void TestNonSequentialFragmentInsert()
    {
        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, out Span<byte> data0);
        byte[] fragment1 = GetDataFragment(1, null, out Span<byte> data1);
        byte[] fragment2 = GetDataFragment(2, null, out Span<byte> data2);

        _channel.HandleReliableDataFragment(fragment2);
        AssertCanPopAck(2, false);

        _channel.HandleReliableDataFragment(fragment0);
        AssertCanPopAck(0, true);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment1);
        AssertCanPopAck(2, true);
        Assert.Single(_dataOutputQueue);

        Assert.Empty(_netInterface.SentData); // Check no superfluous acknowledgements have been sent

        int offset = 0;
        Span<byte> stitchedData = _dataOutputQueue.Dequeue();

        Assert.Equal(data0, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data1, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data2, stitchedData[offset..]);
    }

    [Fact]
    public void TestNonFragmentInsert()
    {
        byte[] packet0 = GetDataFragment(0, null, out Span<byte> data0);
        byte[] packet1 = GetDataFragment(1, null, out Span<byte> data1);
        byte[] packet2 = GetDataFragment(2, null, out Span<byte> data2);

        _channel.HandleReliableData(packet0);
        AssertCanPopAck(0, true);
        Assert.Equal(data0, _dataOutputQueue.Dequeue());

        _channel.HandleReliableData(packet2);
        AssertCanPopAck(2, false);

        _channel.HandleReliableData(packet1);
        AssertCanPopAck(2, true);
        Assert.Equal(data1, _dataOutputQueue.Dequeue());
        Assert.Equal(data2, _dataOutputQueue.Dequeue());

        Assert.Empty(_netInterface.SentData); // Check no superfluous acknowledgements have been sent
    }

    [Fact]
    public void TestFragmentedInsertOfTwoDatas()
    {
        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 2, out Span<byte> data0);
        byte[] fragment1 = GetDataFragment(1, null, out Span<byte> data1);
        byte[] fragment2 = GetDataFragment(2, DATA_LENGTH * 2, out Span<byte> data2);
        byte[] fragment3 = GetDataFragment(3, null, out Span<byte> data3);

        _channel.HandleReliableDataFragment(fragment0);
        AssertCanPopAck(0, true);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment1);
        AssertCanPopAck(1, true);
        Span<byte> actualData = _dataOutputQueue.Dequeue();
        Assert.Equal(data0, actualData[..DATA_LENGTH]);
        Assert.Equal(data1, actualData[DATA_LENGTH..]);

        _channel.HandleReliableDataFragment(fragment3);
        AssertCanPopAck(3, false);
        Assert.Empty(_dataOutputQueue);

        _channel.HandleReliableDataFragment(fragment2);
        AssertCanPopAck(3, true);
        actualData = _dataOutputQueue.Dequeue();
        Assert.Equal(data2, actualData[..DATA_LENGTH]);
        Assert.Equal(data3, actualData[DATA_LENGTH..]);
    }

    [Fact]
    public void TestSequenceWaitingOnData()
    {
        byte[] packet0 = GetDataFragment(0, null, out Span<byte> data0);
        byte[] fragment1 = GetDataFragment(1, DATA_LENGTH * 2, out Span<byte> data1);
        byte[] fragment2 = GetDataFragment(2, null, out Span<byte> data2);

        _channel.HandleReliableDataFragment(fragment1);
        _channel.HandleReliableDataFragment(fragment2);
        AssertCanPopAck(1, false);
        AssertCanPopAck(2, false);

        _channel.HandleReliableData(packet0);
        AssertCanPopAck(2, true);
        Assert.Equal(data0, _dataOutputQueue.Dequeue());

        Span<byte> stitched = _dataOutputQueue.Dequeue();
        Assert.Equal(data1, stitched[..DATA_LENGTH].ToArray());
        Assert.Equal(data2, stitched[DATA_LENGTH..].ToArray());
    }

    [Fact]
    public void TestMultiData()
    {
        int multiLen = sizeof(ushort) // Sequence
            + DataUtils.MULTI_DATA_INDICATOR.Length
            + 2 // Two packets of len 1
            + MultiPacketUtils.GetVariableLengthSize(1) * 2;
        byte[] multiBuffer = new byte[multiLen];

        // Fill out the data buffer
        int offset = sizeof(ushort);
        DataUtils.WriteMultiDataIndicator(multiBuffer, ref offset);
        MultiPacketUtils.WriteVariableLength(multiBuffer, 1, ref offset);
        multiBuffer[offset++] = 2;
        MultiPacketUtils.WriteVariableLength(multiBuffer, 1, ref offset);
        multiBuffer[offset] = 4;

        _channel.HandleReliableData(multiBuffer);
        AssertCanPopAck(0, true);
        Assert.Equal(new byte[] { 2 }, _dataOutputQueue.Dequeue());
        Assert.Equal(new byte[] { 4 }, _dataOutputQueue.Dequeue());

        multiBuffer[1] = 0x01; // Increment the sequence

        _channel.HandleReliableData(multiBuffer);
        AssertCanPopAck(1, true);
        Assert.Equal(new byte[] { 2 }, _dataOutputQueue.Dequeue());
        Assert.Equal(new byte[] { 4 }, _dataOutputQueue.Dequeue());
    }

    public static byte[] GetDataFragment
    (
        ushort sequence,
        uint? completeDataLength,
        out Span<byte> data,
        int dataLength = DATA_LENGTH
    )
    {
        int length = dataLength
            + sizeof(ushort) // Sequence
            + (completeDataLength is null ? 0 : sizeof(uint));

        byte[] buffer = new byte[length];
        BinaryWriter writer = new(buffer);

        writer.WriteUInt16BE(sequence);
        if (completeDataLength is not null)
            writer.WriteUInt32BE(completeDataLength.Value);

        data = writer.RemainingSpan;
        Random.Shared.NextBytes(data);

        return buffer;
    }

    private void AssertCanPopAck
    (
        ushort expectedSequence,
        bool expectAll
    )
    {
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
