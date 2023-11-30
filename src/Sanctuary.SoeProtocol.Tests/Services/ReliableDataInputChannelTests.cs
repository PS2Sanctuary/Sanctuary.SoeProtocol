using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Tests.Mocks;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.SoeProtocol.Tests.Services;

public class ReliableDataInputChannelTests
{
    private const int DATA_LENGTH = 8;

    private static readonly NativeSpanPool SpanPool = new(DATA_LENGTH + 6, 8); // + 6, space for sequence + CompleteDataLength

    [Fact]
    public void TestSequentialFragmentInsert()
    {
        Queue<byte[]> dataOutputQueue = new();
        using ReliableDataInputChannel channel = CreateChannel(dataOutputQueue, out MockNetworkInterface networkInterface);

        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, out byte[] data0);
        byte[] fragment1 = GetDataFragment(1, null, out byte[] data1);
        byte[] fragment2 = GetDataFragment(2, null, out byte[] data2);

        channel.HandleReliableDataFragment(fragment0);
        Assert.Empty(dataOutputQueue);
        Assert.Single(networkInterface.SentData);

        channel.HandleReliableDataFragment(fragment1);
        Assert.Empty(dataOutputQueue);
        Assert.Equal(2, networkInterface.SentData.Count);

        channel.HandleReliableDataFragment(fragment2);
        Assert.Single(dataOutputQueue);
        Assert.Equal(3, networkInterface.SentData.Count);

        int offset = 0;
        byte[] stitchedData = dataOutputQueue.Dequeue();

        Assert.Equal(data0, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data1, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data2, stitchedData[offset..]);
    }

    [Fact]
    public void TestNonSequentialFragmentInsert()
    {
        Queue<byte[]> dataOutputQueue = new();
        using ReliableDataInputChannel channel = CreateChannel(dataOutputQueue, out MockNetworkInterface networkInterface);

        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, out byte[] data0);
        byte[] fragment1 = GetDataFragment(1, null, out byte[] data1);
        byte[] fragment2 = GetDataFragment(2, null, out byte[] data2);

        channel.HandleReliableDataFragment(fragment2);
        Assert.Single(networkInterface.SentData); // Check only one ack has been sent
        AssertCanPopAck(networkInterface, 2);

        channel.HandleReliableDataFragment(fragment0);
        Assert.Empty(dataOutputQueue);
        Assert.Single(networkInterface.SentData);

        channel.HandleReliableDataFragment(fragment1);
        Assert.Single(dataOutputQueue);
        Assert.Equal(2, networkInterface.SentData.Count);

        int offset = 0;
        byte[] stitchedData = dataOutputQueue.Dequeue();

        Assert.Equal(data0, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data1, stitchedData[offset..(offset += DATA_LENGTH)]);
        Assert.Equal(data2, stitchedData[offset..]);
    }

    [Fact]
    public void TestDataInsert()
    {
        Queue<byte[]> dataOutputQueue = new();
        using ReliableDataInputChannel channel = CreateChannel(dataOutputQueue, out MockNetworkInterface networkInterface);

        byte[] packet0 = GetDataFragment(0, null, out _);
        byte[] packet1 = GetDataFragment(1, null, out _);
        byte[] packet2 = GetDataFragment(2, null, out byte[] data2);

        channel.HandleReliableData(packet0);
        Assert.Single(dataOutputQueue);
        Assert.Single(networkInterface.SentData);

        dataOutputQueue.Clear();
        networkInterface.SentData.Clear();

        channel.HandleReliableData(packet2);
        Assert.Single(networkInterface.SentData);
        AssertCanPopAck(networkInterface, 2);

        channel.HandleReliableData(packet1);
        Assert.Equal(2, dataOutputQueue.Count);
        Assert.Single(networkInterface.SentData);

        dataOutputQueue.Dequeue();
        Assert.Equal(data2, dataOutputQueue.Dequeue());
    }

    [Fact]
    public void TestMultiSequenceInsert()
    {
        Queue<byte[]> dataOutputQueue = new();
        using ReliableDataInputChannel channel = CreateChannel(dataOutputQueue, out MockNetworkInterface networkInterface);

        byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 2, out _);
        byte[] fragment1 = GetDataFragment(1, null, out _);
        byte[] fragment2 = GetDataFragment(2, DATA_LENGTH * 2, out byte[] data2);
        byte[] fragment3 = GetDataFragment(3, null, out byte[] data3);

        channel.HandleReliableDataFragment(fragment0);
        Assert.Empty(dataOutputQueue);
        Assert.Single(networkInterface.SentData);

        channel.HandleReliableDataFragment(fragment1);
        Assert.Single(dataOutputQueue);
        Assert.Equal(2, networkInterface.SentData.Count);

        dataOutputQueue.Clear();
        networkInterface.SentData.Clear();

        channel.HandleReliableDataFragment(fragment3);
        Assert.Single(networkInterface.SentData);
        AssertCanPopAck(networkInterface, 3);

        channel.HandleReliableDataFragment(fragment2);
        Assert.Single(dataOutputQueue);
        Assert.Single(networkInterface.SentData);

        byte[] actualData = dataOutputQueue.Dequeue();
        Assert.Equal(data2, actualData[..DATA_LENGTH]);
        Assert.Equal(data3, actualData[DATA_LENGTH..]);
    }

    [Fact]
    public void TestSequenceWaitingOnData()
    {
        Queue<byte[]> dataOutputQueue = new();
        using ReliableDataInputChannel channel = CreateChannel(dataOutputQueue, out MockNetworkInterface networkInterface);

        byte[] packet0 = GetDataFragment(0, null, out byte[] data0);
        byte[] fragment1 = GetDataFragment(1, DATA_LENGTH * 2, out byte[] data1);
        byte[] fragment2 = GetDataFragment(2, null, out byte[] data2);

        channel.HandleReliableDataFragment(fragment1);
        channel.HandleReliableDataFragment(fragment2);
        Assert.Equal(2, networkInterface.SentData.Count); // We should have two OOO acks
        AssertCanPopAck(networkInterface, 1);
        AssertCanPopAck(networkInterface, 2);

        channel.HandleReliableData(packet0);
        Assert.Equal(2, dataOutputQueue.Count);
        Assert.Single(networkInterface.SentData);
        Assert.Equal(data0, dataOutputQueue.Dequeue());

        byte[] stitched = dataOutputQueue.Dequeue();
        Assert.Equal(data1, stitched[..DATA_LENGTH].ToArray());
        Assert.Equal(data2, stitched[DATA_LENGTH..].ToArray());
    }

    public static byte[] GetDataFragment(ushort sequence, uint? completeDataLength, out byte[] data)
    {
        int length = DATA_LENGTH
            + sizeof(ushort)
            + (completeDataLength is null ? 0 : sizeof(uint));

        data = new byte[DATA_LENGTH];
        Random.Shared.NextBytes(data);

        byte[] buffer = new byte[length];
        BinaryWriter writer = new(buffer);

        writer.WriteUInt16BE(sequence);
        if (completeDataLength is not null)
            writer.WriteUInt32BE(completeDataLength.Value);
        writer.WriteBytes(data);

        return buffer;
    }

    private static ReliableDataInputChannel CreateChannel
    (
        Queue<byte[]> dataOutputQueue,
        out MockNetworkInterface networkInterface
    )
    {
        networkInterface = new MockNetworkInterface();

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
            networkInterface,
            new MockApplicationProtocolHandler()
        );

        return new ReliableDataInputChannel
        (
            handler,
            SpanPool,
            data => dataOutputQueue.Enqueue(data.ToArray())
        );
    }

    private static void AssertCanPopAck(MockNetworkInterface netInterface, ushort expectedSequence)
    {
        Assert.NotEmpty(netInterface.SentData);
        byte[] ack = netInterface.SentData.Dequeue();
        Assert.Equal(SoeOpCode.Acknowledge, SoePacketUtils.ReadSoeOpCode(ack));
        Acknowledge deserialized = Acknowledge.Deserialize(ack.AsSpan(sizeof(SoeOpCode)));
        Assert.Equal(expectedSequence, deserialized.Sequence);
    }
}
