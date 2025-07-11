using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Util.Zlib;

/// <summary>
/// A wrapper around the zlib deflate API.
/// </summary>
public sealed unsafe class ZlibDeflator : IDisposable
{
    private readonly ZlibCompressionLevel _selectedLevel;
    private readonly bool _includeZlibHeader;
    private readonly int _selectedWindowBits;

    private ZlibStream _stream;

    /// <summary>
    /// Gets or sets a value indicating whether this <see cref="ZlibDeflator"/> instance has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZlibDeflator"/> class.
    /// </summary>
    /// <param name="level">The compression level to use.</param>
    /// <param name="includeZlibHeader">Whether to write the zlib header to the output.</param>
    /// <exception cref="ZlibException"></exception>
    public ZlibDeflator(ZlibCompressionLevel level, bool includeZlibHeader)
    {
        _selectedLevel = level;
        _includeZlibHeader = includeZlibHeader;
        _selectedWindowBits = includeZlibHeader
            ? ZlibConstants.ZLib_DefaultWindowBits
            : ZlibConstants.Deflate_DefaultWindowBits;

        fixed (ZlibStream* stream = &_stream)
        {
            ZlibErrorCode errC = ZlibInterop.DeflateInit2_
            (
                stream,
                level,
                ZlibCompressionMethod.Deflated,
                _selectedWindowBits,
                ZlibConstants.Deflate_DefaultMemLevel,
                ZlibCompressionStrategy.DefaultStrategy
            );

            if (errC is not ZlibErrorCode.Ok)
                GenerateCompressionError(errC, "Failed to initialize deflator");
        }
    }

    /// <summary>
    /// Deflates a buffer.
    /// </summary>
    /// <param name="input">The input buffer.</param>
    /// <param name="output">The buffer to output the deflated bytes to.</param>
    /// <param name="flushMethod">The flush method to use.</param>
    /// <returns>The number of bytes that were written to the <paramref name="output"/>.</returns>
    /// <exception cref="ZlibException"></exception>
    public int Deflate
    (
        ReadOnlySpan<byte> input,
        Span<byte> output,
        ZlibFlushCode flushMethod = ZlibFlushCode.Finish
    )
    {
        CheckNotDisposed();

        fixed (byte* nextIn = input)
        {
            fixed (byte* nextOut = output)
            {
                _stream.NextIn = nextIn;
                _stream.AvailableIn = (uint)input.Length;

                _stream.NextOut = nextOut;
                _stream.AvailableOut = (uint)output.Length;

                fixed (ZlibStream* stream = &_stream)
                {
                    ZlibErrorCode deflateResult = ZlibInterop.Deflate(stream, flushMethod);
                    if (deflateResult is not ZlibErrorCode.StreamEnd)
                        GenerateCompressionError(deflateResult, "Failed to deflate");
                }
            }
        }

        return input.Length - (int)_stream.AvailableOut + (_includeZlibHeader ? ZlibConstants.HeaderLength : 0);
    }

    /// <summary>
    /// Resets the internal state of the deflator.
    /// </summary>
    /// <exception cref="ZlibException"></exception>
    public void Reset()
    {
        CheckNotDisposed();

        _stream.NextIn = null;
        _stream.AvailableIn = 0;
        _stream.NextOut = null;
        _stream.AvailableOut = 0;

        fixed (ZlibStream* stream = &_stream)
        {
            ZlibErrorCode errC = ZlibInterop.DeflateEnd(stream);
            if (errC is not ZlibErrorCode.Ok)
                GenerateCompressionError(errC, "Failed to end deflate");

            errC = ZlibInterop.DeflateInit2_
            (
                stream,
                _selectedLevel,
                ZlibCompressionMethod.Deflated,
                _selectedWindowBits,
                ZlibConstants.Deflate_DefaultMemLevel,
                ZlibCompressionStrategy.DefaultStrategy
            );
            if (errC is not ZlibErrorCode.Ok)
                GenerateCompressionError(errC, "Failed to re-init the deflator");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckNotDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(ZlibDeflator));
    }

    private void GenerateCompressionError(ZlibErrorCode result, string genericMessage)
    {
        string? msg = _stream.ErrorMessage != IntPtr.Zero
                ? Marshal.PtrToStringAnsi(_stream.ErrorMessage)
                : genericMessage;

        throw new ZlibException(result, msg);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        fixed (ZlibStream* stream = &_stream)
            ZlibInterop.DeflateEnd(stream);

        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of the <see cref="ZlibDeflator"/> when the class is deconstructed.
    /// </summary>
    ~ZlibDeflator()
    {
        Dispose();
    }
}
