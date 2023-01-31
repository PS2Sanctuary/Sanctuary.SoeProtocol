using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Tests.Mocks;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;

namespace Sanctuary.SoeProtocol.Tests.Services;

public class ReliableDataChannelEndToEndTests
{
    private const int MAX_DATA_LENGTH = (int)SoeConstants.DefaultUdpLength - sizeof(SoeOpCode) - sizeof(ushort) - SoeConstants.CrcLength;
    private static readonly NativeSpanPool SpanPool = new(512, 16);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestSingleSmallPacket(bool multiDataMode)
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(5)
            },
            multiDataMode
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestMultipleSmallPackets(bool multiDataMode)
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(3),
                GeneratePacket(45),
                GeneratePacket(1),
                GeneratePacket(214)
            },
            multiDataMode
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestMultipleSmallPacketsRequiringFragmentation(bool multiDataMode)
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(3),
                GeneratePacket(45),
                GeneratePacket(1),
                GeneratePacket(214),
                GeneratePacket(214),
                GeneratePacket(214)
            },
            multiDataMode
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestLargestDataPacket(bool multiDataMode)
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(MAX_DATA_LENGTH)
            },
            multiDataMode
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestSingleLargePacket(bool multiDataMode)
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(MAX_DATA_LENGTH + 1)
            },
            multiDataMode
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestMultipleLargePackets(bool multiDataMode)
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket((int)SoeConstants.DefaultUdpLength),
                GeneratePacket((int)SoeConstants.DefaultUdpLength + 7),
                GeneratePacket((int)SoeConstants.DefaultUdpLength + 54),
                GeneratePacket((int)SoeConstants.DefaultUdpLength * 2)
            },
            multiDataMode
        );

    private static void AssertOnPackets
    (
        IReadOnlyCollection<byte[]> packets,
        bool multiDataMode
    )
    {
        Queue<byte[]> receiveQueue = new();
        GetHandlers
        (
            out ReliableDataOutputChannel outputChannel,
            out ReliableDataInputChannel inputChannel,
            out MockNetworkInterface networkInterface,
            receiveQueue
        );

        foreach (byte[] packet in packets)
        {
            outputChannel.EnqueueData(packet);
            if (!multiDataMode)
                outputChannel.RunTick(CancellationToken.None);
        }

        if (multiDataMode)
            outputChannel.RunTick(CancellationToken.None);

        while (networkInterface.SentData.TryDequeue(out byte[]? packet))
        {
            SoeOpCode op = (SoeOpCode)BinaryPrimitives.ReadUInt16BigEndian(packet);
            Span<byte> outputData = packet.AsSpan()[sizeof(SoeOpCode)..^SoeConstants.CrcLength];
            if (op is SoeOpCode.ReliableData)
                inputChannel.HandleReliableData(outputData);
            else if (op is SoeOpCode.ReliableDataFragment)
                inputChannel.HandleReliableDataFragment(outputData);
            else // Ack from input handler
                break;
        }

        Assert.Equal(packets.Count, receiveQueue.Count);
        foreach (byte[] value in packets)
        {
            byte[] recomposed = receiveQueue.Dequeue();
            Assert.Equal(value.Length, recomposed.Length);
            Assert.Equal(value, recomposed);
        }

        outputChannel.Dispose();
        inputChannel.Dispose();
    }

    private static void GetHandlers
    (
        out ReliableDataOutputChannel outputChannel,
        out ReliableDataInputChannel inputChannel,
        out MockNetworkInterface networkInterface,
        Queue<byte[]> receiveQueue
    )
    {
        networkInterface = new MockNetworkInterface();
        const int fragmentWindowSize = 32;

        SoeProtocolHandler handler = new
        (
            SessionMode.Client,
            new SessionParameters
            {
                ApplicationProtocol = "TestProtocol",
                RemoteUdpLength = SoeConstants.DefaultUdpLength,
                IsCompressionEnabled = false,
                CrcLength = SoeConstants.CrcLength,
                MaxQueuedReliableDataPackets = fragmentWindowSize
            },
            SpanPool,
            networkInterface,
            new MockApplicationProtocolHandler()
        );

        outputChannel = new ReliableDataOutputChannel(handler, SpanPool, MAX_DATA_LENGTH + sizeof(ushort));
        inputChannel = new ReliableDataInputChannel
        (
            handler,
            SpanPool,
            data => receiveQueue.Enqueue(data.ToArray())
        );
    }

    private static byte[] GeneratePacket(int size)
    {
        byte[] packet = new byte[size];
        Random.Shared.NextBytes(packet);
        return packet;
    }
}
