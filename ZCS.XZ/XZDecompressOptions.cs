namespace ZCS.XZ;

/// <summary>
/// Configuration options for <see cref="XZDecompressStream"/>.
/// Controls thread count, memory limits, and internal buffer size.
/// </summary>
/// <remarks>
/// <para>
/// The default settings produce a single-threaded auto-detecting decoder with no memory
/// limit, matching the behavior of the constructors that do not take an options object.
/// </para>
/// <para>
/// Set <see cref="Threads"/> to a value greater than 1 to enable multithreaded decoding via
/// <c>lzma_stream_decoder_mt</c>, or set it to 0 to auto-detect based on
/// <see cref="LibLzmaNativeMethods.CpuThreads"/>.
/// </para>
/// </remarks>
public sealed class XZDecompressOptions
{
    /// <summary>
    /// Gets or sets the number of threads for multithreaded decompression.
    /// </summary>
    /// <value>
    /// <list type="bullet">
    ///   <item><description><c>0</c> — auto-detect (uses <see cref="LibLzmaNativeMethods.CpuThreads"/>).</description></item>
    ///   <item><description><c>1</c> — single-threaded (uses <c>lzma_auto_decoder</c>).</description></item>
    ///   <item><description><c>&gt;1</c> — multithreaded (uses <c>lzma_stream_decoder_mt</c>).</description></item>
    /// </list>
    /// Default is 1.
    /// </value>
    /// <remarks>
    /// <para>
    /// <strong>Threaded decoding supports the .xz format only.</strong> The multithreaded
    /// decoder does not auto-detect legacy <c>.lzma</c> or <c>.lz</c> input, so setting this
    /// above 1 gives up the format auto-detection that the single-threaded path provides.
    /// </para>
    /// <para>
    /// Parallelism additionally requires Block Headers carrying compressed and uncompressed
    /// sizes, which only the multithreaded encoder writes. A single-block stream, or one
    /// without those sizes, decodes single-threaded no matter what this is set to.
    /// </para>
    /// </remarks>
    public int Threads { get; set; } = 1;

    /// <summary>
    /// Gets or sets the hard memory limit, in bytes, for the decoder.
    /// If decoding needs more than this even single-threaded, decoding fails with
    /// <c>LZMA_MEMLIMIT_ERROR</c>. Default is <see cref="ulong.MaxValue"/> (no limit).
    /// </summary>
    public ulong MemoryLimit { get; set; } = ulong.MaxValue;

    /// <summary>
    /// Gets or sets the soft memory limit, in bytes, used to cap the worker thread count
    /// during multithreaded decoding.
    /// </summary>
    /// <value>
    /// <c>0</c> (the default) selects one quarter of physical memory, the starting point
    /// recommended by liblzma. Ignored when <see cref="Threads"/> resolves to 1.
    /// </value>
    /// <remarks>
    /// Exceeding this limit reduces the number of threads rather than failing, so it never
    /// causes an error on its own — use <see cref="MemoryLimit"/> to cap memory for real.
    /// Leaving it unset matters: liblzma clamps this value to a minimum of 1 byte, and a
    /// limit of 1 byte forces single-threaded decoding.
    /// </remarks>
    public ulong MemoryLimitThreading { get; set; }

    /// <summary>
    /// Gets or sets the buffer size (in bytes) used for the internal input and output
    /// buffers. Default is 81920 (80 KB).
    /// </summary>
    public int BufferSize { get; set; } = 81920;

    /// <summary>
    /// Resolves the effective thread count. If <see cref="Threads"/> is 0 or negative,
    /// returns <see cref="LibLzmaNativeMethods.CpuThreads"/>; otherwise returns <see cref="Threads"/>.
    /// </summary>
    /// <returns>The effective number of threads to use for decoding.</returns>
    internal int GetThreadCount()
    {
        if (Threads <= 0)
        {
            return LibLzmaNativeMethods.CpuThreads;
        }
        return Threads;
    }

    /// <summary>
    /// Resolves the effective soft threading memory limit, substituting one quarter of
    /// physical memory when <see cref="MemoryLimitThreading"/> is left at 0.
    /// </summary>
    /// <returns>The threading memory limit in bytes.</returns>
    internal ulong GetMemoryLimitThreading()
    {
        if (MemoryLimitThreading != 0)
        {
            return MemoryLimitThreading;
        }

        // liblzma recommends physmem / 4 as a starting point. If liblzma cannot detect
        // physical memory it reports 0, which would clamp to 1 byte and silently disable
        // threading, so fall back to no soft limit and let MemoryLimit do the capping.
        ulong physmem = LibLzmaNativeMethods.PhysicalMemory;
        return physmem == 0 ? ulong.MaxValue : physmem / 4;
    }

    /// <summary>
    /// Builds the native multithreading options struct for this configuration.
    /// </summary>
    /// <param name="flags">Decoder flags to apply (e.g. <c>LZMA_CONCATENATED</c>).</param>
    /// <returns>An <c>lzma_mt</c> struct ready for the multithreaded decoder.</returns>
    internal LibLzmaNativeMethods.LzmaMt CreateMtOptions(uint flags) => new()
    {
        flags = flags,
        threads = (uint)GetThreadCount(),
        memlimit_threading = GetMemoryLimitThreading(),
        memlimit_stop = MemoryLimit,
    };
}
