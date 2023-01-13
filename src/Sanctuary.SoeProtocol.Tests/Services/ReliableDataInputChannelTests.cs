using Moq;
using Sanctuary.SoeProtocol.Abstractions;
using Sanctuary.SoeProtocol.Objects;
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

    private static readonly NativeSpanPool SpanPool = new(512, 8);

    // [Fact]
    // public void TestSequentialFragmentInsert()
    // {
    //     using DataSequencer sequencer = CreateChannel(state);
    //
    //     byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, false, true, out byte[] data0);
    //     byte[] fragment1 = GetDataFragment(1, null, false, true, out byte[] data1);
    //     byte[] fragment2 = GetDataFragment(2, null, false, true, out byte[] data2);
    //
    //     sequencer.InsertDataFragment(fragment0, false, true);
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment1, false, true);
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment2, false, true);
    //     Assert.True(sequencer.TryGetCompletedSequence(out IMemoryOwner<byte>? sequence));
    //     Assert.NotNull(sequence);
    //
    //     int offset = 0;
    //     Assert.Equal(data0, sequence.Memory[offset..(offset += DATA_LENGTH)].ToArray());
    //     Assert.Equal(data1, sequence.Memory[offset..(offset += DATA_LENGTH)].ToArray());
    //     Assert.Equal(data2, sequence.Memory[offset..].ToArray());
    //
    //     sequence.Dispose();
    // }
    //
    // [Fact]
    // public void TestNonSequentialFragmentInsert()
    // {
    //     using DataFragmentSequenceState state = GetState();
    //     using DataSequencer sequencer = CreateChannel(state);
    //
    //     byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, false, true, out byte[] data0);
    //     byte[] fragment1 = GetDataFragment(1, null, false, true, out byte[] data1);
    //     byte[] fragment2 = GetDataFragment(2, null, false, true, out byte[] data2);
    //
    //     sequencer.InsertDataFragment(fragment2, false, true);
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment0, false, true);
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment1, false, true);
    //     Assert.True(sequencer.TryGetCompletedSequence(out IMemoryOwner<byte>? sequence));
    //     Assert.NotNull(sequence);
    //
    //     int offset = 0;
    //     Assert.Equal(data0, sequence.Memory[offset..(offset += DATA_LENGTH)].ToArray());
    //     Assert.Equal(data1, sequence.Memory[offset..(offset += DATA_LENGTH)].ToArray());
    //     Assert.Equal(data2, sequence.Memory[offset..].ToArray());
    //
    //     sequence.Dispose();
    // }
    //
    // [Fact]
    // public void TestDataInsert()
    // {
    //     using DataFragmentSequenceState state = GetState();
    //     using DataSequencer sequencer = CreateChannel(state);
    //
    //     byte[] packet0 = GetDataFragment(0, null, false, true, out _);
    //     byte[] packet1 = GetDataFragment(1, null, false, true, out _);
    //     byte[] packet2 = GetDataFragment(2, null, false, true, out byte[] data2);
    //
    //     Assert.True(sequencer.InsertData(packet0, false, true));
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     Assert.Equal(1, state.FragmentStartSequence);
    //     Assert.Equal(1, state.WindowStartSequence);
    //     Assert.Equal(1, state.TotalSequenceCount);
    //
    //     Assert.False(sequencer.InsertData(packet2, false, true));
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     Assert.Equal(1, state.FragmentStartSequence);
    //     Assert.Equal(1, state.WindowStartSequence);
    //     Assert.Equal(2, state.TotalSequenceCount);
    //
    //     Assert.True(sequencer.InsertData(packet1, false, true));
    //     Assert.True(sequencer.TryGetCompletedSequence(out IMemoryOwner<byte>? sequence));
    //     Assert.NotNull(sequence);
    //
    //     Assert.Equal(3, state.FragmentStartSequence);
    //     Assert.Equal(3, state.WindowStartSequence);
    //     Assert.Equal(3, state.TotalSequenceCount);
    //
    //     Assert.Equal(data2, sequence.Memory.ToArray());
    //     sequence.Dispose();
    // }
    //
    // [Fact]
    // public void TestMultiSequenceInsert()
    // {
    //     using DataFragmentSequenceState state = GetState();
    //     using DataSequencer sequencer = CreateChannel(state);
    //
    //     byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 2, false, true, out _);
    //     byte[] fragment1 = GetDataFragment(1, null, false, true, out _);
    //     byte[] fragment2 = GetDataFragment(2, DATA_LENGTH * 2, false, true, out byte[] data2);
    //     byte[] fragment3 = GetDataFragment(3, null, false, true, out byte[] data3);
    //
    //     sequencer.InsertDataFragment(fragment0, false, true);
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment1, false, true);
    //     Assert.True(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment3, false, true);
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment2, false, true);
    //     Assert.True(sequencer.TryGetCompletedSequence(out IMemoryOwner<byte>? sequence));
    //     Assert.NotNull(sequence);
    //
    //     int offset = 0;
    //     Assert.Equal(data2, sequence.Memory[offset..(offset += DATA_LENGTH)].ToArray());
    //     Assert.Equal(data3, sequence.Memory[offset..].ToArray());
    //
    //     sequence.Dispose();
    // }
    //
    // [Fact]
    // public void TestSequentialFragmentInsertWithMixedParameters()
    // {
    //     using DataFragmentSequenceState state = GetState();
    //     using DataSequencer sequencer = CreateChannel(state);
    //
    //     byte[] fragment0 = GetDataFragment(0, DATA_LENGTH * 3, true, false, out byte[] data0);
    //     byte[] fragment1 = GetDataFragment(1, null, false, false, out byte[] data1);
    //     byte[] fragment2 = GetDataFragment(2, null, false, true, out byte[] data2);
    //
    //     sequencer.InsertDataFragment(fragment0, true, false);
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment1, false, false);
    //     Assert.False(sequencer.TryGetCompletedSequence(out _));
    //
    //     sequencer.InsertDataFragment(fragment2, false, true);
    //     Assert.True(sequencer.TryGetCompletedSequence(out IMemoryOwner<byte>? sequence));
    //     Assert.NotNull(sequence);
    //
    //     int offset = 0;
    //     Assert.Equal(data0, sequence.Memory[offset..(offset += DATA_LENGTH)].ToArray());
    //     Assert.Equal(data1, sequence.Memory[offset..(offset += DATA_LENGTH)].ToArray());
    //     Assert.Equal(data2, sequence.Memory[offset..].ToArray());
    //
    //     Assert.Equal(3, state.FragmentStartSequence);
    //     Assert.Equal(3, state.WindowStartSequence);
    //     Assert.Equal(3, state.TotalSequenceCount);
    //
    //     sequence.Dispose();
    // }

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
        Assert.Empty(dataOutputQueue);
        Assert.Empty(networkInterface.SentData);

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
        Rc4KeyState keyState = new(new byte[] { 0, 1, 2, 3, 4 });
        networkInterface = new MockNetworkInterface();

        SoeProtocolHandler handler = new
        (
            SessionMode.Client,
            new SessionParameters
            {
                ApplicationProtocol = "TestProtocol",
                RemoteUdpLength = 512
            },
            SpanPool,
            networkInterface,
            Mock.Of<IApplicationProtocolHandler>(),
            keyState
        );

        return new ReliableDataInputChannel
        (
            handler,
            SpanPool,
            keyState,
            data => dataOutputQueue.Enqueue(data.ToArray())
        );
    }
}
