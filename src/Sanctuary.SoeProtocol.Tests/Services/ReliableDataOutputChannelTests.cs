using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Services;
using Sanctuary.SoeProtocol.Tests.Mocks;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.SoeProtocol.Tests.Services;

public class ReliableDataOutputChannelTests
{
    private const int FRAGMENT_WINDOW_SIZE = 8;
    private const int MAX_DATA_LENGTH = (int)SoeConstants.DefaultUdpLength - sizeof(SoeOpCode) - sizeof(ushort) - SoeConstants.CrcLength;

    private static readonly NativeSpanPool SpanPool = new(512, 8);

    [Fact]
    public async Task TestRepeatsDataOnAckFailure()
    {
        const int fragmentCount = 4;
        ReliableDataOutputChannel handler = CreateChannel(out MockNetworkInterface networkInterface);

        const int packetLength = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (fragmentCount - 1);
        byte[] packet = GeneratePacket(packetLength);

        handler.EnqueueData(packet);
        handler.RunTick(CancellationToken.None);
        AssertReceivedPacketsEqualBuffer(networkInterface, packet, true);

        // Don't acknowledge
        await Task.Delay(ReliableDataOutputChannel.ACK_WAIT_MILLISECONDS + 100);
        handler.RunTick(CancellationToken.None);
        AssertReceivedPacketsEqualBuffer(networkInterface, packet, true);
    }

    [Fact]
    public async Task TestRepeatsDataFromArbitraryPositionOnAckDelay()
    {
        const int fragmentCount = 4;
        ReliableDataOutputChannel handler = CreateChannel(out MockNetworkInterface networkInterface);

        const int packetLength = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (fragmentCount - 1);
        byte[] packet = GeneratePacket(packetLength);

        handler.EnqueueData(packet);
        handler.RunTick(CancellationToken.None);

        AssertReceivedPacketsEqualBuffer(networkInterface, packet, true);
        handler.NotifyOfAcknowledgeAll(new AcknowledgeAll(1));

        await Task.Delay(ReliableDataOutputChannel.ACK_WAIT_MILLISECONDS + 100);
        handler.RunTick(CancellationToken.None);

        const int expectedConsumed = MAX_DATA_LENGTH - 4 + MAX_DATA_LENGTH;
        AssertReceivedPacketsEqualBuffer(networkInterface, packet.AsSpan(expectedConsumed), false);
    }

    [Fact]
    public async Task TestRepeatsFullWindowOfDataFromArbitraryPositionOnAckDelay()
    {
        const int fragmentCount = FRAGMENT_WINDOW_SIZE * 2;
        ReliableDataOutputChannel channel = CreateChannel(out MockNetworkInterface networkInterface);

        const int packetLength = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (fragmentCount - 1);
        byte[] packet = GeneratePacket(packetLength);
        channel.EnqueueData(packet);

        channel.RunTick(CancellationToken.None);

        const int expectedReceiveLength = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (FRAGMENT_WINDOW_SIZE - 1);
        AssertReceivedPacketsEqualBuffer
        (
            networkInterface,
            packet.AsSpan(0, expectedReceiveLength),
            true
        );

        channel.NotifyOfAcknowledgeAll(new AcknowledgeAll(FRAGMENT_WINDOW_SIZE - 2));
        await Task.Delay(ReliableDataOutputChannel.ACK_WAIT_MILLISECONDS + 100);
        channel.RunTick(CancellationToken.None);

        const int expectedConsumed = MAX_DATA_LENGTH - 4
            + MAX_DATA_LENGTH * (FRAGMENT_WINDOW_SIZE - 2);
        const int expectedRepeatLength = MAX_DATA_LENGTH * (FRAGMENT_WINDOW_SIZE);

        AssertReceivedPacketsEqualBuffer
        (
            networkInterface,
            packet.AsSpan(expectedConsumed, expectedRepeatLength),
            false
        );
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

    private static ReliableDataOutputChannel CreateChannel(out MockNetworkInterface networkInterface)
    {
        networkInterface = new MockNetworkInterface();

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
            networkInterface,
            new MockApplicationProtocolHandler()
        );

        return new ReliableDataOutputChannel(handler, SpanPool, MAX_DATA_LENGTH + sizeof(ushort));
    }

    private static byte[] GeneratePacket(int size)
    {
        byte[] packet = new byte[size];
        Random.Shared.NextBytes(packet);
        return packet;
    }
}
