namespace ZCS.XZ;

/// <summary>
/// Specifies the container format produced by <see cref="XZCompressStream"/>.
/// </summary>
public enum XZFormat
{
    /// <summary>
    /// The modern <c>.xz</c> format. Supports integrity checks, multithreading,
    /// and concatenated streams. This is the default.
    /// </summary>
    Xz = 0,

    /// <summary>
    /// The legacy <c>.lzma</c> (LZMA_Alone) format.
    /// </summary>
    /// <remarks>
    /// Provided for interoperability with tools that cannot read <c>.xz</c>. The format
    /// carries no integrity check, cannot be produced by more than one thread, and does
    /// not support flushing mid-stream — <see cref="XZCompressStream.Flush"/> only forwards
    /// to the underlying stream. Prefer <see cref="Xz"/> for new data.
    /// </remarks>
    LzmaAlone = 1,
}
