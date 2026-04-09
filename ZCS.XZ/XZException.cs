namespace ZCS.XZ;

/// <summary>
/// Represents an error returned by the liblzma native library during
/// encoding, decoding, or initialization.
/// </summary>
/// <remarks>
/// The <see cref="LzmaReturnCode"/> property contains the raw integer
/// return code from the liblzma function that failed. The exception message
/// provides a human-readable description of the error.
/// </remarks>
public class XZException : Exception
{
    /// <summary>
    /// Gets the raw liblzma return code (e.g., <c>LZMA_DATA_ERROR</c>, <c>LZMA_MEM_ERROR</c>).
    /// See <see cref="LibLzmaNativeMethods"/> constants for possible values.
    /// </summary>
    public int LzmaReturnCode { get; }

    /// <summary>
    /// Initializes a new <see cref="XZException"/> with a message derived from the
    /// liblzma return code.
    /// </summary>
    /// <param name="returnCode">The liblzma return code that caused the error.</param>
    public XZException(int returnCode)
        : base(GetMessage(returnCode))
    {
        LzmaReturnCode = returnCode;
    }

    /// <summary>
    /// Initializes a new <see cref="XZException"/> with a custom message.
    /// </summary>
    /// <param name="returnCode">The liblzma return code that caused the error.</param>
    /// <param name="message">A custom error message.</param>
    public XZException(int returnCode, string message)
        : base(message)
    {
        LzmaReturnCode = returnCode;
    }

    /// <summary>
    /// Maps a liblzma return code to a human-readable error message.
    /// </summary>
    /// <param name="code">The liblzma return code.</param>
    /// <returns>A descriptive error message string.</returns>
    private static string GetMessage(int code) => code switch
    {
        LibLzmaNativeMethods.LZMA_MEM_ERROR => "Memory allocation failed (LZMA_MEM_ERROR).",
        LibLzmaNativeMethods.LZMA_MEMLIMIT_ERROR => "Memory usage limit was reached (LZMA_MEMLIMIT_ERROR).",
        LibLzmaNativeMethods.LZMA_FORMAT_ERROR => "The input is not in the .xz format (LZMA_FORMAT_ERROR).",
        LibLzmaNativeMethods.LZMA_OPTIONS_ERROR => "Invalid or unsupported options (LZMA_OPTIONS_ERROR).",
        LibLzmaNativeMethods.LZMA_DATA_ERROR => "Compressed data is corrupt (LZMA_DATA_ERROR).",
        LibLzmaNativeMethods.LZMA_BUF_ERROR => "No progress is possible (LZMA_BUF_ERROR).",
        LibLzmaNativeMethods.LZMA_PROG_ERROR => "Programming error in liblzma usage (LZMA_PROG_ERROR).",
        _ => $"Unknown liblzma error (code={code}).",
    };
}
