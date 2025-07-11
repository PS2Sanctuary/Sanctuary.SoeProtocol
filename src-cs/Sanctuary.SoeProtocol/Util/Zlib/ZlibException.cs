using System;

namespace Sanctuary.SoeProtocol.Util.Zlib;

/// <summary>
/// Thrown when a zlib function returns a non-recoverable error code.
/// </summary>
public class ZlibException : Exception
{
    /// <summary>
    /// The error code returned by zlib.
    /// </summary>
    public ZlibErrorCode ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZlibException"/> class.
    /// </summary>
    /// <param name="error">The error code returned by zlib.</param>
    /// <param name="message">The message associated with the error.</param>
    public ZlibException(ZlibErrorCode error, string? message = null)
        : base(message)
    {
        ErrorCode = error;
    }
}
