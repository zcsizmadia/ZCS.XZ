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
/// <see cref="LibLzmaNativeMethods.CpuThreads"/>.
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
    ///   <item><description><c>0</c> — auto-detect (uses <see cref="LibLzmaNativeMethods.CpuThreads"/>).</description></item>
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
    /// Gets or sets the container format to produce. Default is <see cref="XZFormat.Xz"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="XZFormat.LzmaAlone"/> writes the legacy <c>.lzma</c> format, which supports
    /// neither multithreading nor integrity checks. Combining it with
    /// <see cref="Threads"/> greater than 1 is rejected when the stream is constructed.
    /// </remarks>
    public XZFormat Format { get; set; } = XZFormat.Xz;

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
    /// returns <see cref="LibLzmaNativeMethods.CpuThreads"/>; otherwise returns <see cref="Threads"/>.
    /// </summary>
    /// <returns>The effective number of threads to use for encoding.</returns>
    internal int GetThreadCount()
    {
        if (Threads <= 0)
        {
            return LibLzmaNativeMethods.CpuThreads;
        }
        return Threads;
    }

    /// <summary>
    /// Builds the native multithreading options struct for this configuration.
    /// </summary>
    /// <returns>An <c>lzma_mt</c> struct ready for the multithreaded encoder.</returns>
    internal LibLzmaNativeMethods.LzmaMt CreateMtOptions() => new()
    {
        threads = (uint)GetThreadCount(),
        preset = GetPreset(),
        check = LibLzmaNativeMethods.LZMA_CHECK_CRC64,
    };

    /// <summary>
    /// Estimates how much memory the encoder will use with the current settings.
    /// </summary>
    /// <remarks>
    /// The estimate accounts for <see cref="Threads"/>: single-threaded configurations are
    /// measured against <c>lzma_easy_encoder_memusage</c>, multithreaded ones against
    /// <c>lzma_stream_encoder_mt_memusage</c>. It does not include <see cref="BufferSize"/>,
    /// which is a managed allocation.
    /// </remarks>
    /// <returns>
    /// The approximate encoder memory usage in bytes, or <see cref="ulong.MaxValue"/>
    /// if liblzma considers the current settings invalid.
    /// </returns>
    /// <example>
    /// <code>
    /// var options = new XZCompressOptions { Level = XZCompressionLevel.Maximum };
    /// Console.WriteLine($"{options.GetMemoryUsage() / (1024 * 1024)} MiB");
    /// </code>
    /// </example>
    public ulong GetMemoryUsage()
    {
        if (GetThreadCount() > 1)
        {
            var mt = CreateMtOptions();
            return LibLzmaNativeMethods.lzma_stream_encoder_mt_memusage(ref mt);
        }

        return LibLzmaNativeMethods.lzma_easy_encoder_memusage(GetPreset());
    }

    /// <summary>
    /// Estimates how much memory a decoder will need to read a stream produced with
    /// the current <see cref="Level"/> and <see cref="Extreme"/> settings.
    /// </summary>
    /// <remarks>
    /// Decoder memory usage depends only on the preset the data was compressed with,
    /// not on the thread count, so <see cref="Threads"/> does not affect this value.
    /// It is useful for choosing a memory limit to pass to <see cref="XZDecompressStream"/>.
    /// </remarks>
    /// <returns>
    /// The approximate decoder memory usage in bytes, or <see cref="ulong.MaxValue"/>
    /// if liblzma considers the current settings invalid.
    /// </returns>
    public ulong GetDecoderMemoryUsage()
        => LibLzmaNativeMethods.lzma_easy_decoder_memusage(GetPreset());
}
