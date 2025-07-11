namespace Sanctuary.SoeProtocol.Util.Zlib;

// ReSharper disable EnumUnderlyingTypeIsInt

/// <summary>
/// Contains constant values used to configure zlib.
/// </summary>
public static class ZlibConstants
{
    /// <summary>
    /// The length in bytes of the zlib header.
    /// </summary>
    public const int HeaderLength = 2;

    /// <summary>
    /// <p><strong>From the ZLib manual:</strong></p>
    /// <p>ZLib's <code>windowBits</code> parameter is the base two logarithm of the window size (the size of the
    /// history buffer). It should be in the range 8..15 for this version of the library. Larger values of this
    /// parameter result in better compression at the expense of memory usage. The default value is 15 if deflateInit is
    /// used instead.<br /></p>
    /// <strong>Note</strong>:
    /// <code>windowBits</code> can also be -8..-15 for raw deflate (i.e. no zlib header is written). In this case,
    /// -windowBits determines the window size. <code>Deflate</code> will then generate raw deflate data with no ZLib
    /// header or trailer, and will not compute an adler32 check value.<br />
    /// <p>See also: How to choose a compression level (in comments to <code>CompressionLevel</code>).</p>
    /// </summary>
    public const int Deflate_DefaultWindowBits = -15;

    /// <summary>
    /// <p><strong>From the ZLib manual:</strong></p>
    /// <p>ZLib's <code>windowBits</code> parameter is the base two logarithm of the window size (the size of the
    /// history buffer). It should be in the range 8..15 for this version of the library. Larger values of this
    /// parameter result in better compression at the expense of memory usage. The default value is 15 if deflateInit is
    /// used instead.<br /></p>
    /// </summary>
    public const int ZLib_DefaultWindowBits = 15;

    /// <summary>
    /// <p><strong>From the ZLib manual:</strong></p>
    /// <p>The <code>memLevel</code> parameter specifies how much memory should be allocated for the internal
    /// compression state. <code>memLevel</code> = 1 uses minimum memory but is slow and reduces compression ratio;
    /// <code>memLevel</code> = 9 uses maximum memory for optimal speed. The default value is 8.</p>
    /// <p>See also: How to choose a compression level (in comments to <code>CompressionLevel</code>.)</p>
    /// </summary>
    public const int Deflate_DefaultMemLevel = 8;
}

/// <summary>
/// Enumerates the possible flush methods that can be used with a deflate operation.
/// </summary>
public enum ZlibFlushCode : int
{
    /// <summary>
    /// Allows the algorithm to decide how much data to accumulate before producing output.
    /// This maximizes the compression level that is achieved.
    /// </summary>
    NoFlush = 0,

    /// <summary>
    /// All pending output is flushed to the output buffer and aligned on a byte boundary.
    /// </summary>
    SyncFlush = 2,

    /// <summary>
    /// Indicates that the input buffer contains the entire sequence to be deflated, allowing optimisations to be applied.
    /// </summary>
    Finish = 4,

    /// <summary>
    /// If flush is set to Z_BLOCK, a deflate block is completed and emitted, as for Z_SYNC_FLUSH, but the output is not
    /// aligned on a byte boundary, and up to seven bits of the current block are held to be written as the next byte
    /// after the next deflate block is completed.  In this case, the decompressor may not be provided enough bits at
    /// this point in order to complete decompression of the data provided so far to the compressor.  It may need to
    /// wait for the next block to be emitted.  This is for advanced applications that need to control the emission of
    /// deflate blocks.
    /// </summary>
    Block = 5
}

/// <summary>
/// Enumerates the possible error codes of a zlib operation.
/// </summary>
public enum ZlibErrorCode : int
{
    /// <summary>
    /// Deflate/Inflate: Progress has been made. Supply more input, or more output space.
    /// Otherwise: The operation completed successfully.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// All input has been consumed and all output produced successfully.
    /// </summary>
    StreamEnd = 1,

    /// <summary>
    /// The <see cref="ZlibStream"/> structure is inconsistent (e.g. <see cref="ZlibStream.NextIn"/> or
    /// <see cref="ZlibStream.NextOut"/> are null).
    /// </summary>
    StreamError = -2,

    /// <summary>
    /// Inflate: The input stream is corrupt (doesn't conform to the zlib format,
    /// or incorrect check value, in which case see <see cref="ZlibStream.ErrorMessage"/>).
    /// </summary>
    DataError = -3,

    /// <summary>
    /// Not enough memory to complete the operation.
    /// </summary>
    MemError = -4,

    /// <summary>
    /// No progress is possible or there is not enough room in the
    /// output buffer when <c>..FlushMethod.Finish</c> is used.
    /// This error is not fatal, and inflation can continue with more input and/or more output space.
    /// </summary>
    BufError = -5,

    /// <summary>
    /// The version of the underlying library is not the same as what the caller is expecting.
    /// </summary>
    VersionError = -6
}

/// <summary>
/// Used to tune the compression algorithm.
/// </summary>
public enum ZlibCompressionStrategy : int
{
    /// <summary>
    /// For normal data.
    /// </summary>
    DefaultStrategy = 0,

    /// <summary>
    /// For data produced by a filter (or predictor). Filtered data consists mostly of small values with a somewhat
    /// random distribution. In this case, the compression algorithm is tuned to compress them better. The effect of
    /// <see cref="Filtered"/> is to force more Huffman coding and less string matching; it is somewhat intermediate
    /// between <see cref="DefaultStrategy"/> and <see cref="HuffmanOnly"/>.
    /// </summary>
    Filtered = 1,

    /// <summary>
    /// Force Huffman encoding only (no string match).
    /// </summary>
    HuffmanOnly = 2,

    /// <summary>
    /// Limit match distances to one (run-length encoding). RLE is designed to be almost as fast as
    /// <see cref="HuffmanOnly"/>, but give better compression for PNG image data. The strategy parameter only affects
    /// the compression ratio but not the correctness of the compressed output even if it is not set appropriately.
    /// </summary>
    RunLengthEncoding = 3,

    /// <summary>
    /// Prevents the use of dynamic Huffman codes, allowing for a simpler decoder for special applications.
    /// </summary>
    Fixed = 4
}

/// <summary>
/// The compression method to use.
/// </summary>
public enum ZlibCompressionMethod : int
{
    /// <summary>
    /// Use the deflated algorithm.
    /// </summary>
    Deflated = 8
}

/// <summary>
/// ZLib can accept any integer value between 0 and 9 (inclusive) as a valid compression level parameter:
/// 1 gives best speed, 9 gives best compression, 0 gives no compression at all (the input data is simply copied a block
/// at a time). <see cref="ZlibCompressionLevel.DefaultCompression" /> = -1 requests a default compromise between speed
/// and compression (currently equivalent to level 6).
/// </summary>
/// <remarks>
/// <para><strong>How to choose a compression level:</strong><br />
/// The names <see cref="NoCompression" />, <see cref="BestSpeed" />, <see cref="DefaultCompression" />,
/// <see cref="BestCompression" /> are taken over from the corresponding ZLib definitions, which map to our public
/// NoCompression, Fastest, Optimal, and SmallestSize respectively.</para>
/// <em>Optimal Compression:</em>
/// <code>
/// ZlibCompressionLevel compressionLevel = ZLibNative.CompressionLevel.DefaultCompression;
/// int windowBits = 15;  // or -15 if no headers required
/// int memLevel = 8;
/// ZLibNative.CompressionStrategy strategy = ZLibNative.CompressionStrategy.DefaultStrategy;
/// </code>
///
/// <em>Fastest compression:</em>
/// <code>
/// ZlibCompressionLevel compressionLevel = ZLibNative.CompressionLevel.BestSpeed;
/// int windowBits = 15;  // or -15 if no headers required
/// int memLevel = 8;
/// ZLibNative.CompressionStrategy strategy = ZLibNative.CompressionStrategy.DefaultStrategy;
/// </code>
///
/// <em>No compression (even faster, useful for data that cannot be compressed such some image formats):</em>
/// <code>
/// ZlibCompressionLevel compressionLevel = ZLibNative.CompressionLevel.NoCompression;
/// int windowBits = 15;  // or -15 if no headers required
/// int memLevel = 7;
/// ZLibNative.CompressionStrategy strategy = ZLibNative.CompressionStrategy.DefaultStrategy;
/// </code>
///
/// <em>Smallest Size Compression:</em>
/// <code>
/// ZlibCompressionLevel compressionLevel = ZLibNative.CompressionLevel.BestCompression;
/// int windowBits = 15;  // or -15 if no headers required
/// int memLevel = 8;
/// ZLibNative.CompressionStrategy strategy = ZLibNative.CompressionStrategy.DefaultStrategy;
/// </code>
/// </remarks>
public enum ZlibCompressionLevel : int
{
    /// <summary>
    /// No compression is used.
    /// </summary>
    NoCompression = 0,

    /// <summary>
    /// Optimized for time taken to compress the input.
    /// </summary>
    BestSpeed = 1,

    /// <summary>
    /// A default compromise between speed and compression.
    /// </summary>
    DefaultCompression = -1,

    /// <summary>
    /// Optimized for size of the output.
    /// </summary>
    BestCompression = 9
}
