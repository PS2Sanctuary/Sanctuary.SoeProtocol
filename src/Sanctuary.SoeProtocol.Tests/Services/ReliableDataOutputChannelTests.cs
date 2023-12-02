using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Tests.Mocks;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol.Tests.Services;

public class ReliableDataOutputChannelTests : IDisposable
{
    private const int FRAGMENT_WINDOW_SIZE = 8;
    private const int MAX_DATA_LENGTH = (int)SoeConstants.DefaultUdpLength - sizeof(SoeOpCode) - sizeof(ushort) - SoeConstants.CrcLength;

    private static readonly NativeSpanPool SpanPool = new(512, 8);

    private readonly MockNetworkInterface _netInterface;
    private readonly ReliableDataOutputChannel _channel;

    public ReliableDataOutputChannelTests()
    {
        _netInterface = new MockNetworkInterface();

        SoeProtocolHandler handler = new
        (
            SessionMode.Client,
            new SessionParameters
            {
                ApplicationProtocol = "TestProtocol",
                RemoteUdpLength = SoeConstants.DefaultUdpLength,
                IsCompressionEnabled = false,
                CrcLength = SoeConstants.CrcLength,
                MaxQueuedOutgoingReliableDataPackets = FRAGMENT_WINDOW_SIZE
            },
            SpanPool,
            _netInterface,
            new MockApplicationProtocolHandler()
        );

        _channel = new ReliableDataOutputChannel(handler, SpanPool, MAX_DATA_LENGTH + sizeof(ushort));
    }

    [Fact]
    public async Task TestRepeatsDataOnAckFailure()
    {
        const int fragmentCount = 4;

        const int packetLength = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (fragmentCount - 1);
        byte[] packet = GeneratePacket(packetLength);

        _channel.EnqueueData(packet);
        _channel.RunTick(CancellationToken.None);
        AssertReceivedPacketsEqualBuffer(_netInterface, packet, true);

        // Don't acknowledge
        await Task.Delay(ReliableDataOutputChannel.ACK_WAIT_MILLISECONDS + 100);
        _channel.RunTick(CancellationToken.None);
        AssertReceivedPacketsEqualBuffer(_netInterface, packet, true);
    }

    [Fact]
    public async Task TestRepeatsDataFromArbitraryPositionOnAckDelay()
    {
        const int fragmentCount = 4;

        const int packetLength = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (fragmentCount - 1);
        byte[] packet = GeneratePacket(packetLength);

        _channel.EnqueueData(packet);
        _channel.RunTick(CancellationToken.None);

        AssertReceivedPacketsEqualBuffer(_netInterface, packet, true);
        _channel.NotifyOfAcknowledgeAll(new AcknowledgeAll(1));

        await Task.Delay(ReliableDataOutputChannel.ACK_WAIT_MILLISECONDS + 100);
        _channel.RunTick(CancellationToken.None);

        const int expectedConsumed = MAX_DATA_LENGTH - 4 + MAX_DATA_LENGTH;
        AssertReceivedPacketsEqualBuffer(_netInterface, packet.AsSpan(expectedConsumed), false);
    }

    [Fact]
    public async Task TestRepeatsFullWindowOfDataFromArbitraryPositionOnAckDelay()
    {
        const int fragmentCount = FRAGMENT_WINDOW_SIZE * 2;

        const int packetLength = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (fragmentCount - 1);
        byte[] packet = GeneratePacket(packetLength);
        _channel.EnqueueData(packet);

        _channel.RunTick(CancellationToken.None);

        const int expectedReceiveLength = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (FRAGMENT_WINDOW_SIZE - 1);
        AssertReceivedPacketsEqualBuffer
        (
            _netInterface,
            packet.AsSpan(0, expectedReceiveLength),
            true
        );

        _channel.NotifyOfAcknowledgeAll(new AcknowledgeAll(FRAGMENT_WINDOW_SIZE - 2));
        await Task.Delay(ReliableDataOutputChannel.ACK_WAIT_MILLISECONDS + 100);
        _channel.RunTick(CancellationToken.None);

        const int expectedConsumed = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (FRAGMENT_WINDOW_SIZE - 2);
        const int expectedRepeatLength = MAX_DATA_LENGTH * (FRAGMENT_WINDOW_SIZE);

        AssertReceivedPacketsEqualBuffer
        (
            _netInterface,
            packet.AsSpan(expectedConsumed, expectedRepeatLength),
            false
        );
    }

    [Fact]
    public void TestAllTheData()
    {
        const int maxPacketLength = 512;
        const int maxNonFragmentDataLength = maxPacketLength - sizeof(ushort);
        ushort sequence = 0;
        Queue<byte[]> dataQueue = new();

        for (int i = 0; i < 256; i++)
        {
            byte[] data = new byte[i * 16];
            Random.Shared.NextBytes(data);

            if (data.Length < maxNonFragmentDataLength)
            {
                byte[] fragment = new byte[data.Length + sizeof(ushort)];
                BinaryPrimitives.WriteUInt16BigEndian(fragment, sequence++);
                data.AsSpan().CopyTo(fragment.AsSpan(sizeof(ushort)));
                dataQueue.Enqueue(fragment);
            }
            else
            {
                // Yeah... this is a mess -_-
                Span<byte> remaining = data;

                byte[] fragment = new byte[maxPacketLength];
                BinaryPrimitives.WriteUInt16BigEndian(fragment, sequence++);
                BinaryPrimitives.WriteInt32BigEndian(fragment.AsSpan(sizeof(ushort)), data.Length);
                remaining[..(fragment.Length - sizeof(ushort) - sizeof(uint))]
                    .CopyTo(fragment.AsSpan(sizeof(ushort) + sizeof(uint)));
                remaining = remaining[(fragment.Length - sizeof(ushort) - sizeof(uint))..];
                dataQueue.Enqueue(fragment);

                while (remaining.Length > 0)
                {
                    fragment = new byte[Math.Min(maxPacketLength, remaining.Length + sizeof(ushort))];
                    BinaryPrimitives.WriteUInt16BigEndian(fragment, sequence++);
                    remaining[..(fragment.Length - sizeof(ushort))].CopyTo(fragment.AsSpan(sizeof(ushort)));
                    remaining = remaining[(fragment.Length - sizeof(ushort))..];
                    dataQueue.Enqueue(fragment);
                }
            }

            _channel.EnqueueData(data);
            _channel.NotifyOfAcknowledgeAll(new AcknowledgeAll((ushort)(sequence - 1)));
        }

        while (_netInterface.SentData.Count is not 0)
            Assert.Equal(dataQueue.Dequeue(), _netInterface.SentData.Dequeue()[sizeof(SoeOpCode)..]);
    }

    private static void AssertReceivedPacketsEqualBuffer
    (
        MockNetworkInterface networkInterface,
        ReadOnlySpan<byte> buffer,
        bool expectMasterFragment
    )
    {
        int position = 0;

        while (networkInterface.SentData.TryDequeue(out byte[]? receiveBuffer))
        {
            int dataOffset = sizeof(SoeOpCode)
                + sizeof(ushort) // Sequence
                + (expectMasterFragment ? sizeof(uint) : 0);

            ReadOnlySpan<byte> data = receiveBuffer.AsSpan()[dataOffset..^SoeConstants.CrcLength];
            expectMasterFragment = false;

            foreach (byte t in data)
                Assert.Equal(buffer[position++], t);
        }

        Assert.Equal(buffer.Length, position);
    }

    private static byte[] GeneratePacket(int size)
    {
        byte[] packet = new byte[size];
        Random.Shared.NextBytes(packet);
        return packet;
    }

    public void Dispose()
    {
        _netInterface.Dispose();
        _channel.Dispose();
    }
}
