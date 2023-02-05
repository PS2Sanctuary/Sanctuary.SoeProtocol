using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Objects.Packets;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Sanctuary.SoeProtocol.Services;

/// <summary>
/// Contains logic to convert application data into reliable data packets.
/// </summary>
public sealed class ReliableDataOutputChannel : IDisposable
{
    /// <summary>
    /// Gets the maximum amount of time to wait for an acknowledgement
    /// </summary>
    public const int ACK_WAIT_MILLISECONDS = 500; // TODO: High ping could mess this up. Needs to be dynamic

    private readonly SoeProtocolHandler _handler;
    private readonly SessionParameters _sessionParams;
    private readonly ApplicationParameters _applicationParams;
    private readonly NativeSpanPool _spanPool;
    private readonly List<StashedOutputPacket> _dispatchStash;
    private readonly SemaphoreSlim _packetOutputQueueLock;

    // Data-related
    private Rc4KeyState? _cipherState;
    private int _maxDataLength;

    // Sequencing
    private long _windowStartSequence;
    private long _currentSequence;
    private long _totalSequence;
    private long _lastAcknowledged;
    private long _newAcknowledgement;
    private long _lastAckAt;

    // MultiBuffer related
    private NativeSpan _multiBuffer;
    private int _multiBufferOffset;
    private int _multiBufferItemCount;
    private int _multiBufferFirstItemOffset;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReliableDataOutputChannel"/> class.
    /// </summary>
    /// <param name="handler">The parent handler.</param>
    /// <param name="spanPool">The native span pool to use.</param>
    /// <param name="maxDataLength">The maximum length of data that may be sent by the output channel.</param>
    public ReliableDataOutputChannel
    (
        SoeProtocolHandler handler,
        NativeSpanPool spanPool,
        int maxDataLength
    )
    {
        _handler = handler;
        _sessionParams = handler.SessionParams;
        _applicationParams = handler.ApplicationParams;
        _spanPool = spanPool;

        _dispatchStash = new List<StashedOutputPacket>();
        _packetOutputQueueLock = new SemaphoreSlim(1);

        _cipherState = _applicationParams.EncryptionKeyState?.Copy();
        SetMaxDataLength(maxDataLength);
        SetupNewMultiBuffer();

        _windowStartSequence = 0;
        _currentSequence = 0;
        _totalSequence = 0;
        _lastAcknowledged = -1;
        _newAcknowledgement = -1;
    }

    /// <summary>
    /// Enqueues data to be sent on the reliable channel.
    /// </summary>
    /// <param name="data">The data.</param>
    public void EnqueueData(ReadOnlySpan<byte> data)
        => EnqueueDataInternal(data, false);

    /// <summary>
    /// Runs a tick of the output channel, which will send queued data.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    public void RunTick(CancellationToken ct)
    {
        _packetOutputQueueLock.Wait(ct);

        int slide = (int)(_newAcknowledgement - _lastAcknowledged);
        if (slide > 0)
        {
            for (int i = 0; i < slide; i++)
                _spanPool.Return(_dispatchStash[i].DataSpan);
            _dispatchStash.RemoveRange(0, slide);

            _lastAcknowledged = _newAcknowledgement;
            _windowStartSequence = _newAcknowledgement + 1;

            // Just in case we've received a late ack, after reverting to repeat
            if (_currentSequence < _windowStartSequence)
                _currentSequence = _windowStartSequence;
        }

        // Pull anything from the multi-buffer
        EnqueueMultiBuffer();
        _packetOutputQueueLock.Release();

        // Ensure we're not expecting an ack when we've only
        // just started sending a block of data
        if (_windowStartSequence == _currentSequence)
            _lastAckAt = Stopwatch.GetTimestamp();

        // Been a while since we received an ack? Send again from the start of the window
        if (Stopwatch.GetElapsedTime(_lastAckAt).Milliseconds >= ACK_WAIT_MILLISECONDS)
            _currentSequence = _windowStartSequence;

        // Send everything we haven't sent from the current window
        long lastSequenceToSend = Math.Min(_totalSequence, _currentSequence + _sessionParams.MaxQueuedReliableDataPackets);

        while (_currentSequence < lastSequenceToSend)
        {
            ct.ThrowIfCancellationRequested();

            int index = (int)(_currentSequence - _windowStartSequence);
            StashedOutputPacket stashedPacket = _dispatchStash[index];

            SoeOpCode opCode = stashedPacket.IsFragment ? SoeOpCode.ReliableDataFragment : SoeOpCode.ReliableData;
            _handler.SendContextualPacket(opCode, stashedPacket.DataSpan.UsedSpan);
            _currentSequence++;
        }
    }

    /// <summary>
    /// Notifies the channel of an acknowledgement packet.
    /// </summary>
    /// <param name="ack">The acknowledgement.</param>
    public void NotifyOfAcknowledge(Acknowledge ack)
    {
        if (GetTrueIncomingSequence(ack.Sequence) <= _lastAcknowledged)
            return;

        _newAcknowledgement = ack.Sequence;
        _lastAckAt = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Notifies the channel of an out-of-order packet.
    /// </summary>
    /// <param name="outOfOrder">The out-of-order packet.</param>
    public void NotifyOfOutOfOrder(OutOfOrder outOfOrder)
    {
        // TODO: We should immediately resend the mis-ordered packet
    }

    /// <summary>
    /// Sets the maximum length of data that may be output in a single packet.
    /// </summary>
    /// <remarks>
    /// This method should not be used after any data has been enqueued on the channel, to ensure that previously
    /// queued packets do not exceed the new limit.
    /// </remarks>
    /// <param name="maxDataLength">The maximum data length.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if this method is called after data has been enqueued.
    /// </exception>
    public void SetMaxDataLength(int maxDataLength)
    {
        if (_currentSequence > 0)
            throw new InvalidOperationException("The maximum length may not be changed after data has been enqueued");

        _maxDataLength = maxDataLength;
    }

    private void EnqueueDataInternal(ReadOnlySpan<byte> data, bool isRecursing)
    {
        byte[]? encryptedSpan = null;
        if (!isRecursing)
            _packetOutputQueueLock.Wait();

        if (!isRecursing && _applicationParams.IsEncryptionEnabled)
            encryptedSpan = Encrypt(data, out data);

        int multiLength = DataUtils.GetVariableLengthSize(data.Length) + data.Length;
        if (_maxDataLength - _multiBufferOffset >= multiLength) // We can fit in the current multi-buffer
        {
            DataUtils.WriteVariableLength(_multiBuffer.FullSpan, (uint)data.Length, ref _multiBufferOffset);
            data.CopyTo(_multiBuffer.FullSpan[_multiBufferOffset..]);

            if (_multiBufferFirstItemOffset is -1)
                _multiBufferFirstItemOffset = _multiBufferOffset;
            _multiBufferOffset += data.Length;
            _multiBufferItemCount++;
        }
        else
        {
            // We must enqueue the current multi-buffer, in order to maintain order
            EnqueueMultiBuffer();

            // Now that we've cleared the multi-buffer, can we fit?
            if (_maxDataLength - _multiBufferOffset >= multiLength)
            {
                EnqueueDataInternal(data, true);
            }
            else
            {
                StashFragment(ref data, true);
                while (data.Length > 0)
                    StashFragment(ref data, false);
            }
        }

        if (encryptedSpan is not null)
            ArrayPool<byte>.Shared.Return(encryptedSpan);

        if (!isRecursing)
            _packetOutputQueueLock.Release();
    }

    private void StashFragment(ref ReadOnlySpan<byte> data, bool isMaster)
    {
        NativeSpan span = _spanPool.Rent();
        BinaryWriter writer = new(span.FullSpan);

        writer.WriteUInt16BE((ushort)_totalSequence);
        int amountToTake = Math.Min(data.Length, _maxDataLength - sizeof(ushort));

        if (isMaster)
        {
            writer.WriteUInt32BE((uint)data.Length);
            amountToTake -= sizeof(uint);
        }

        writer.WriteBytes(data[..amountToTake]);
        span.UsedLength = writer.Offset;

        _dispatchStash.Add(new StashedOutputPacket(true, span));
        _totalSequence++;
        data = data[amountToTake..];
    }

    private void EnqueueMultiBuffer()
    {
        switch (_multiBufferItemCount)
        {
            case 0:
                return;
            case 1: // Just send a normal data packet in this case
                _multiBuffer.StartOffset = _multiBufferFirstItemOffset - sizeof(ushort);
                _multiBuffer.UsedLength = _multiBufferOffset - _multiBuffer.StartOffset;
                BinaryPrimitives.WriteUInt16BigEndian
                (
                    _multiBuffer.UsedSpan,
                    (ushort)_totalSequence
                );
                break;
            default:
                BinaryPrimitives.WriteUInt16BigEndian
                (
                    _multiBuffer.FullSpan,
                    (ushort)_totalSequence
                );
                _multiBuffer.UsedLength = _multiBufferOffset;
                break;
        }

        _dispatchStash.Add(new StashedOutputPacket(false, _multiBuffer));
        _totalSequence++;

        SetupNewMultiBuffer();
    }

    [MemberNotNull(nameof(_multiBuffer))]
    private void SetupNewMultiBuffer()
    {
        _multiBuffer = _spanPool.Rent();
        _multiBufferItemCount = 0;
        _multiBufferFirstItemOffset = -1;
        _multiBufferOffset = sizeof(ushort); // Space for sequence
        DataUtils.WriteMultiDataIndicator(_multiBuffer.FullSpan, ref _multiBufferOffset);
    }

    private byte[] Encrypt(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> output)
    {
        byte[] storage = ArrayPool<byte>.Shared.Rent(data.Length + 1);
        storage[0] = 0;

        // We can assume the key state is not null, as encryption cannot be enabled
        // by the application without setting a key state
        Rc4Cipher.Transform(data, storage.AsSpan(1), ref _cipherState!);
        output = storage[1] == 0
            ? storage
            : storage.AsSpan(1, data.Length);

        return storage;
    }

    private long GetTrueIncomingSequence(ushort packetSequence)
        => DataUtils.GetTrueIncomingSequence
        (
            packetSequence,
            _windowStartSequence,
            _sessionParams.MaxQueuedReliableDataPackets
        );

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (StashedOutputPacket element in _dispatchStash)
            _spanPool.Return(element.DataSpan);
        _dispatchStash.Clear();

        _spanPool.Return(_multiBuffer);
        _cipherState?.Dispose();
        _packetOutputQueueLock.Dispose();
    }

    private readonly record struct StashedOutputPacket(bool IsFragment, NativeSpan DataSpan);
}
