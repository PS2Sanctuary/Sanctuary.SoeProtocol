using System;
using System.Runtime.InteropServices;

namespace Sanctuary.SoeProtocol.Util.Zlib;

/// <summary>
/// Zlib stream descriptor data structure.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal unsafe struct ZlibStream
{
    /// <summary>
    /// The next input byte.
    /// </summary>
    public byte* NextIn;

    /// <summary>
    /// The next output byte.
    /// </summary>
    public byte* NextOut;

    /// <summary>
    /// The last error message. Null-terminated ANSI string. Null if no error.
    /// </summary>
    public IntPtr ErrorMessage;

    /// <summary>
    /// The internal state.
    /// </summary>
    private readonly IntPtr internalState;

    /// <summary>
    /// The number of bytes available at <see cref="NextIn"/>.
    /// </summary>
    public uint AvailableIn;

    /// <summary>
    /// The remaining free space at <see cref="NextOut"/>.
    /// </summary>
    public uint AvailableOut;
}
