using Sanctuary.SoeProtocol.Objects;
using Sanctuary.SoeProtocol.Util;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Sanctuary.SoeProtocol.Services;

public sealed class ReliableDataOutputChannel : IDisposable
{
    private readonly SoeProtocolHandler _handler;
    private readonly NativeSpanPool _spanPool;
    private readonly SlidingWindowArray<NativeSpan?> _packetOutputStash;
    private readonly int _maxQueuedData;
    private readonly SemaphoreSlim _dataQueueLock;

    // Data-related
    private Rc4KeyState _cipherState;
    private int _maxDataLength;

    // Sequences
    private long _windowStartSequence;
    private long _currentSequence;

    // MultiBuffer related
    private NativeSpan _multiBuffer;
    private int _multiBufferOffset;
    private int _multiBufferItemCount;
    private int _multiBufferFirstItemOffset;

    public ReliableDataOutputChannel
    (
        SoeProtocolHandler handler,
        NativeSpanPool spanPool,
        Rc4KeyState cipherState,
        int maxDataLength
    )
    {
        _handler = handler;
        _spanPool = spanPool;
        _maxQueuedData = handler.SessionParams.MaxQueuedReliableDataPackets;

        _packetOutputStash = new SlidingWindowArray<NativeSpan?>(_maxQueuedData);
        _dataQueueLock = new SemaphoreSlim(1);

        _cipherState = cipherState;
        SetMaxDataLength(maxDataLength);
        SetupNewMultiBuffer();

        _windowStartSequence = 0;
    }

    public void EnqueueData(ReadOnlySpan<byte> data)
        => EnqueueDataInternal(data, true);

    public void RunTick()
    {
        _dataQueueLock.Wait();

        EnqueueMultiBuffer();

        _dataQueueLock.Release();
    }

    /// <summary>
    /// This method should not be used after the data has been
    /// enqueued on the channel.
    /// </summary>
    /// <param name="maxDataLength">The maximum length of data that may be output in a single packet.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if this method is called after data has been enqueued.
    /// </exception>
    public void SetMaxDataLength(int maxDataLength)
    {
        if (_currentSequence > 0)
            throw new InvalidOperationException("The maximum length may not be changed after data has been enqueued");

        _maxDataLength = maxDataLength - sizeof(ushort); // Space for sequence
    }

    private void EnqueueDataInternal(ReadOnlySpan<byte> data, bool needsEncryption)
    {
        byte[]? encryptedSpan = null;
        _dataQueueLock.Wait();

        if (needsEncryption)
            encryptedSpan = Encrypt(data, out data);

        int multiLength = DataUtils.GetVariableLengthSize(data.Length) + data.Length;
        if (_maxDataLength - _multiBufferOffset < multiLength) // We can fit in the current multi-buffer
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
            if (_maxDataLength - _multiBufferOffset < multiLength)
            {
                EnqueueDataInternal(data, false);
            }
            else
            {
                // TODO: Break into fragments
            }
        }

        if (encryptedSpan is not null)
            ArrayPool<byte>.Shared.Return(encryptedSpan);
        _dataQueueLock.Release();
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
                    (ushort)_currentSequence
                );
                break;
            default:
                BinaryPrimitives.WriteUInt16BigEndian
                (
                    _multiBuffer.FullSpan,
                    (ushort)_currentSequence
                );
                _multiBuffer.UsedLength = _multiBufferOffset;
                break;
        }

        _packetOutputStash[_currentSequence % _maxQueuedData] = _multiBuffer;
        _currentSequence++;

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

        Rc4Cipher.Transform(data, storage.AsSpan(1), ref _cipherState);
        output = storage[1] == 0
            ? storage.AsSpan(0, data.Length + 1)
            : storage.AsSpan(1, data.Length);

        return storage;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        for (int i = 0; i < _packetOutputStash.Length; i++)
        {
            if (_packetOutputStash[i] is { } span)
                _spanPool.Return(span);
        }
    }
}
