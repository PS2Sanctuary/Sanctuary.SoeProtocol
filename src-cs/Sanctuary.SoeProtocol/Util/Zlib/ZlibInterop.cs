using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Util.Zlib;

internal static partial class ZlibInterop
{
    [LibraryImport("System.IO.Compression.Native", EntryPoint = "CompressionNative_DeflateInit2_")]
    internal static unsafe partial ZlibErrorCode DeflateInit2_
    (
        ZlibStream* stream,
        ZlibCompressionLevel level,
        ZlibCompressionMethod method,
        int windowBits,
        int memLevel,
        ZlibCompressionStrategy strategy
    );

    [LibraryImport("System.IO.Compression.Native", EntryPoint = "CompressionNative_Deflate")]
    internal static unsafe partial ZlibErrorCode Deflate
    (
        ZlibStream* stream,
        ZlibFlushCode flush
    );

    [LibraryImport("System.IO.Compression.Native", EntryPoint = "CompressionNative_DeflateEnd")]
    internal static unsafe partial ZlibErrorCode DeflateEnd(ZlibStream* stream);

    [LibraryImport("System.IO.Compression.Native", EntryPoint = "CompressionNative_InflateInit2_")]
    internal static unsafe partial ZlibErrorCode InflateInit2_
    (
        ZlibStream* stream,
        int windowBits
    );

    [LibraryImport("System.IO.Compression.Native", EntryPoint = "CompressionNative_Inflate")]
    internal static unsafe partial ZlibErrorCode Inflate
    (
        ZlibStream* stream,
        ZlibFlushCode flush
    );

    [LibraryImport("System.IO.Compression.Native", EntryPoint = "CompressionNative_InflateEnd")]
    internal static unsafe partial ZlibErrorCode InflateEnd(ZlibStream* stream);
}
