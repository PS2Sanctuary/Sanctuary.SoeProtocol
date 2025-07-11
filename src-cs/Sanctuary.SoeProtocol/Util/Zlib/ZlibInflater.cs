using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Util.Zlib;

/// <summary>
/// A wrapper around the zlib inflate API.
/// </summary>
public sealed unsafe class ZlibInflater : IDisposable
{
    private readonly int _selectedWindowBits;

    private ZlibStream _stream;

    /// <summary>
    /// Gets or sets a value indicating whether this <see cref="ZlibInflater"/> instance has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZlibInflater"/> class.
    /// </summary>
    /// <param name="zlibHeaderPresent">Whether the zlib header is present in the input.</param>
    /// <exception cref="ZlibException"></exception>
    public ZlibInflater(bool zlibHeaderPresent = true)
    {
        _selectedWindowBits = zlibHeaderPresent
            ? ZlibConstants.ZLib_DefaultWindowBits
            : ZlibConstants.Deflate_DefaultWindowBits;

        fixed (ZlibStream* stream = &_stream)
        {
            ZlibErrorCode errC = ZlibInterop.InflateInit2_(stream, _selectedWindowBits);
            if (errC is not ZlibErrorCode.Ok)
                GenerateCompressionError(errC, "Failed to initialize inflater");
        }
    }

    /// <summary>
    /// Inflates a buffer.
    /// </summary>
    /// <param name="input">The input buffer containing deflated data.</param>
    /// <param name="output">The buffer to write the inflated data to.</param>
    /// <returns>The number of bytes that were written to the <paramref name="output"/>.</returns>
    /// <exception cref="ZlibException"></exception>
    public ulong Inflate(ReadOnlySpan<byte> input, ReadOnlySpan<byte> output)
    {
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
                    ZlibErrorCode inflateResult = ZlibInterop.Inflate(stream, ZlibFlushCode.Finish);
                    if (inflateResult is not ZlibErrorCode.StreamEnd)
                        GenerateCompressionError(inflateResult, "Failed to inflate");
                }
            }
        }

        return (ulong)input.Length - _stream.AvailableOut;
    }

    /// <summary>
    /// Resets the internal state of the inflater.
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
            ZlibErrorCode errC = ZlibInterop.InflateEnd(stream);
            if (errC is not ZlibErrorCode.Ok)
                GenerateCompressionError(errC, "Failed to end inflate");

            errC = ZlibInterop.InflateInit2_(stream, _selectedWindowBits);
            if (errC is not ZlibErrorCode.Ok)
                GenerateCompressionError(errC, "Failed to re-init the inflater");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckNotDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(ZlibInflater));
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
            ZlibInterop.InflateEnd(stream);

        IsDisposed = true;
        GC.SuppressFinalize(this);
    }
    /// <summary>
    /// Disposes of the <see cref="ZlibInflater"/> when the class is deconstructed.
    /// </summary>
    ~ZlibInflater()
    {
        Dispose();
    }
}
