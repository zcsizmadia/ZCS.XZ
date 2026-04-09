namespace ZCS.XZ;

/// <summary>
/// Configuration options for <see cref="XZCompressStream"/>.
/// Controls compression level, extreme mode, thread count, and internal buffer size.
/// </summary>
/// <remarks>
/// <para>
/// The default settings produce a single-threaded encoder at level 6 (the liblzma default).
/// Set <see cref="Threads"/> to a value greater than 1 to enable multithreaded encoding via
/// <c>lzma_stream_encoder_mt</c>, or set it to 0 to auto-detect based on
/// <see cref="Environment.ProcessorCount"/>.
/// </para>
/// </remarks>
public sealed class XZCompressOptions
{
    /// <summary>
    /// Gets or sets the compression level (0–9). Default is <see cref="XZCompressionLevel.Default"/> (6).
    /// Higher levels produce smaller output at the cost of more CPU time and memory.
    /// </summary>
    public XZCompressionLevel Level { get; set; } = XZCompressionLevel.Default;

    /// <summary>
    /// Gets or sets a value indicating whether to use extreme mode.
    /// When enabled, the encoder uses a slower variant of the selected preset
    /// that can produce marginally smaller output.
    /// </summary>
    public bool Extreme { get; set; }

    /// <summary>
    /// Gets or sets the number of threads for multithreaded compression.
    /// </summary>
    /// <value>
    /// <list type="bullet">
    ///   <item><description><c>0</c> — auto-detect (uses <see cref="Environment.ProcessorCount"/>).</description></item>
    ///   <item><description><c>1</c> — single-threaded (uses <c>lzma_easy_encoder</c>).</description></item>
    ///   <item><description><c>&gt;1</c> — multithreaded (uses <c>lzma_stream_encoder_mt</c>).</description></item>
    /// </list>
    /// Default is 1.
    /// </value>
    public int Threads { get; set; } = 1;

    /// <summary>
    /// Gets or sets the buffer size (in bytes) used for internal I/O operations.
    /// Default is 81920 (80 KB). Larger buffers may reduce the number of write calls
    /// to the underlying stream but increase memory usage.
    /// </summary>
    public int BufferSize { get; set; } = 81920;

    /// <summary>
    /// Computes the liblzma preset value from <see cref="Level"/> and <see cref="Extreme"/>.
    /// When <see cref="Extreme"/> is <c>true</c>, the <c>LZMA_PRESET_EXTREME</c> flag is OR'd
    /// into the preset.
    /// </summary>
    /// <returns>The liblzma preset value ready for encoder initialization.</returns>
    internal uint GetPreset()
    {
        uint preset = (uint)Level;
        if (Extreme)
        {
            preset |= LibLzmaNativeMethods.LZMA_PRESET_EXTREME;
        }
        return preset;
    }

    /// <summary>
    /// Resolves the effective thread count. If <see cref="Threads"/> is 0 or negative,
    /// returns <see cref="Environment.ProcessorCount"/>; otherwise returns <see cref="Threads"/>.
    /// </summary>
    /// <returns>The effective number of threads to use for encoding.</returns>
    internal int GetThreadCount()
    {
        if (Threads <= 0)
        {
            return Environment.ProcessorCount;
        }
        return Threads;
    }
}
