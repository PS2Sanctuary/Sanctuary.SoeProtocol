using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Tests.Mocks;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit.Abstractions;

namespace Sanctuary.SoeProtocol.Tests.Services;

public class ReliableDataChannelEndToEndTests
{
    private const int MAX_DATA_LENGTH = (int)SoeConstants.DefaultUdpLength - sizeof(SoeOpCode) - sizeof(ushort) - SoeConstants.CrcLength;
    private static readonly NativeSpanPool SpanPool = new(512, 16);

    private readonly ITestOutputHelper _ouputHelper;

    public ReliableDataChannelEndToEndTests(ITestOutputHelper outputHelper)
    {
        _ouputHelper = outputHelper;
    }

    [Fact]
    public void TestSingleSmallPacket()
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(5)
            },
            0
        );

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TestMultipleSmallPackets(int numberOfPacketsToMulti)
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(3),
                GeneratePacket(45),
                GeneratePacket(1),
                GeneratePacket(214)
            },
            numberOfPacketsToMulti
        );

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TestMultipleSmallPacketsRequiringFragmentation(int numberOfPacketsToMulti)
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
            numberOfPacketsToMulti
        );

    [Fact]
    public void TestLargestDataPacket()
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(MAX_DATA_LENGTH)
            },
            0
        );

    [Fact]
    public void TestSingleLargePacket()
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket(MAX_DATA_LENGTH + 1)
            },
            0
        );

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void TestMultipleLargePackets(int numberOfPacketsToMulti)
        => AssertOnPackets
        (
            new[]
            {
                GeneratePacket((int)SoeConstants.DefaultUdpLength),
                GeneratePacket((int)SoeConstants.DefaultUdpLength + 7),
                GeneratePacket((int)SoeConstants.DefaultUdpLength + 54),
                GeneratePacket((int)SoeConstants.DefaultUdpLength * 2)
            },
            numberOfPacketsToMulti
        );

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void TestAllThePackets(int multiCount)
    {
        byte[][] packets = new byte[256][];
        for (int i = 1; i <= 256; i++)
            packets[i - 1] = GeneratePacket(i * 256);

        AssertOnPackets(packets, multiCount);
    }

    private void AssertOnPackets
    (
        IReadOnlyList<byte[]> packets,
        int numberOfPacketsToMulti
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

        for (int i = 0; i < packets.Count; i++)
        {
            outputChannel.EnqueueData(packets[i]);

            // If multi-batching is disabled, or we've submitted enough packets to do a multi-dispatch
            if (numberOfPacketsToMulti > 0 && (i + 1) % numberOfPacketsToMulti is not 0)
                continue;

            outputChannel.RunTick(CancellationToken.None);

            byte[]? lastPacket = networkInterface.SentData.LastOrDefault();
            if (lastPacket is null)
                continue;

            ushort sequence = BinaryPrimitives.ReadUInt16BigEndian(lastPacket.AsSpan(sizeof(SoeOpCode)));
            _ouputHelper.WriteLine($"Acknowledging sequence {sequence}");
            outputChannel.NotifyOfAcknowledgeAll(new AcknowledgeAll { Sequence = sequence });
        }
        outputChannel.RunTick(CancellationToken.None);

        while (networkInterface.SentData.TryDequeue(out byte[]? packet))
        {
            SoeOpCode op = (SoeOpCode)BinaryPrimitives.ReadUInt16BigEndian(packet);
            Span<byte> outputData = packet.AsSpan()[sizeof(SoeOpCode)..^SoeConstants.CrcLength];
            ushort sequence = 0;

            if (op is SoeOpCode.ReliableData or SoeOpCode.ReliableDataFragment)
                sequence = BinaryPrimitives.ReadUInt16BigEndian(outputData);

            _ouputHelper.WriteLine($"Handling {op} packet of length {packet.Length} with seq {sequence}");

            if (op is SoeOpCode.ReliableData)
                inputChannel.HandleReliableData(outputData);
            else if (op is SoeOpCode.ReliableDataFragment)
                inputChannel.HandleReliableDataFragment(outputData);
            else if (op is SoeOpCode.Acknowledge)
                outputChannel.NotifyOfAcknowledge(Acknowledge.Deserialize(packet));
            else if (op is SoeOpCode.AcknowledgeAll)
                outputChannel.NotifyOfAcknowledgeAll(AcknowledgeAll.Deserialize(packet));
        }

        Assert.Equal(packets.Count, receiveQueue.Count);
        for (int i = 0; i < packets.Count; i++)
        {
            _ouputHelper.WriteLine($"Checking recomposed packet {i}");
            byte[] recomposed = receiveQueue.Dequeue();
            byte[] expected = packets[i];

            Assert.Equal(expected.Length, recomposed.Length);
            Assert.Equal(expected, recomposed);
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
                MaxQueuedIncomingReliableDataPackets = fragmentWindowSize,
                MaxQueuedOutgoingReliableDataPackets = fragmentWindowSize
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
        Random r = new(23445);

        byte[] packet = new byte[size];
        r.NextBytes(packet);
        return packet;
    }
}
