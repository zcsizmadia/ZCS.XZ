using System.Runtime.InteropServices;
using static ZCS.XZ.LibLzmaNativeMethods;

namespace ZCS.XZ;

/// <summary>
/// An explicit liblzma filter chain, parsed from the same specification syntax the
/// <c>xz</c> command line uses.
/// </summary>
/// <remarks>
/// <para>
/// A preset such as <see cref="XZCompressionLevel.Maximum"/> selects LZMA2 alone. A filter
/// chain can put a transform in front of it: the BCJ filters rewrite jump and call targets
/// in compiled code, and the delta filter subtracts neighbouring bytes, both of which make
/// the data far more compressible for LZMA2. On an executable or an uncompressed image the
/// difference can be substantial.
/// </para>
/// <para>
/// Filters are separated by <strong>spaces</strong> (or <c>--</c>), and each filter name is
/// followed by <c>:</c> and a comma-separated list of its options — so commas separate
/// options within one filter, not the filters themselves. For example
/// <c>"x86 lzma2:preset=9e"</c> or <c>"delta:dist=4 lzma2:preset=6"</c>. Order matters:
/// input flows into the leftmost filter first, and <c>lzma2</c> normally comes last. Use
/// <see cref="ListSupportedFilters"/> to see everything the loaded liblzma accepts.
/// </para>
/// <para>
/// Decoding needs no filter chain: an <c>.xz</c> stream records its own chain in each block
/// header, so <see cref="XZDecompressStream"/> reads filtered streams without being told.
/// </para>
/// <para>
/// Instances hold unmanaged memory and must be disposed.
/// </para>
/// <example>
/// <code>
/// using var filters = XZFilterChain.Parse("x86 lzma2:preset=9e");
/// using var output = File.Create("program.xz");
/// using var xz = new XZCompressStream(output, new XZCompressOptions { Filters = filters });
/// xz.Write(programBytes);
/// </code>
/// </example>
/// </remarks>
public sealed unsafe class XZFilterChain : IDisposable
{
    private LzmaFilter* _filters;

    private XZFilterChain(LzmaFilter* filters, string spec)
    {
        _filters = filters;
        Spec = spec;
    }

    /// <summary>
    /// Gets the specification string this chain was parsed from.
    /// </summary>
    public string Spec { get; }

    /// <summary>
    /// Gets a pointer to the native filter array, for passing to liblzma.
    /// </summary>
    internal LzmaFilter* Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_filters is null, this);
            return _filters;
        }
    }

    /// <summary>
    /// Parses a filter chain specification.
    /// </summary>
    /// <param name="spec">
    /// A space-separated filter chain, for example <c>"x86 lzma2:preset=9e"</c>, or a bare
    /// preset such as <c>"6"</c>.
    /// </param>
    /// <param name="allFilters">
    /// <c>true</c> to also accept filters that cannot be stored in the <c>.xz</c> format.
    /// Chains parsed this way are usable for raw encoding only, not with
    /// <see cref="XZCompressStream"/>.
    /// </param>
    /// <returns>The parsed chain. Dispose it when finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="spec"/> is not a valid filter chain. The message reports the offset
    /// at which liblzma stopped.
    /// </exception>
    public static XZFilterChain Parse(string spec, bool allFilters = false)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // liblzma writes up to LZMA_FILTERS_MAX entries plus a terminator.
        nuint bytes = (nuint)((LZMA_FILTERS_MAX + 1) * sizeof(LzmaFilter));
        var filters = (LzmaFilter*)NativeMemory.AllocZeroed(bytes);

        uint flags = allFilters ? LZMA_STR_ALL_FILTERS : 0;

        // This function is the odd one out: it reports failure by returning a statically
        // allocated message rather than an lzma_ret, and NULL means success.
        IntPtr error = lzma_str_to_filters(spec, out int errorPos, filters, flags, LibLzmaAllocator.Handle);

        if (error != IntPtr.Zero)
        {
            NativeMemory.Free(filters);
            string message = Marshal.PtrToStringUTF8(error) ?? "invalid filter chain";
            throw new ArgumentException(
                $"Invalid filter chain \"{spec}\" at offset {errorPos}: {message}.", nameof(spec));
        }

        return new XZFilterChain(filters, spec);
    }

    /// <summary>
    /// Lists the filters and options the loaded liblzma supports.
    /// </summary>
    /// <param name="allFilters">
    /// <c>true</c> to include filters that cannot be stored in the <c>.xz</c> format.
    /// </param>
    /// <returns>A human-readable, multi-line listing.</returns>
    /// <exception cref="XZException">liblzma reported an error.</exception>
    public static string ListSupportedFilters(bool allFilters = false)
    {
        uint flags = LZMA_STR_ENCODER | (allFilters ? LZMA_STR_ALL_FILTERS : 0);

        int ret = lzma_str_list_filters(out IntPtr str, LZMA_VLI_UNKNOWN, flags, LibLzmaAllocator.Handle);
        if (ret != LZMA_OK)
        {
            throw new XZException(ret);
        }

        try
        {
            return Marshal.PtrToStringUTF8(str) ?? string.Empty;
        }
        finally
        {
            LibLzmaAllocator.Free(str);
        }
    }

    /// <summary>
    /// Returns the chain as liblzma formats it, which normalizes the specification and
    /// fills in the options left implicit in <see cref="Spec"/>.
    /// </summary>
    /// <param name="decoderOptions">
    /// <c>true</c> to describe the options that matter when decoding rather than encoding.
    /// </param>
    /// <returns>The normalized filter chain string.</returns>
    /// <exception cref="ObjectDisposedException">The chain has been disposed.</exception>
    /// <exception cref="XZException">liblzma reported an error.</exception>
    public string ToNormalizedString(bool decoderOptions = false)
    {
        uint flags = decoderOptions ? LZMA_STR_DECODER : LZMA_STR_ENCODER;

        int ret = lzma_str_from_filters(out IntPtr str, Handle, flags, LibLzmaAllocator.Handle);
        if (ret != LZMA_OK)
        {
            throw new XZException(ret);
        }

        try
        {
            return Marshal.PtrToStringUTF8(str) ?? string.Empty;
        }
        finally
        {
            LibLzmaAllocator.Free(str);
        }
    }

    /// <summary>
    /// Estimates how much memory an encoder using this chain will need.
    /// </summary>
    /// <returns>
    /// Memory usage in bytes, or <see cref="ulong.MaxValue"/> if liblzma considers the
    /// chain invalid for encoding.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The chain has been disposed.</exception>
    public ulong GetEncoderMemoryUsage() => lzma_raw_encoder_memusage(Handle);

    /// <summary>
    /// Estimates how much memory a decoder reading data produced with this chain will need.
    /// </summary>
    /// <returns>
    /// Memory usage in bytes, or <see cref="ulong.MaxValue"/> if liblzma considers the
    /// chain invalid for decoding.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The chain has been disposed.</exception>
    public ulong GetDecoderMemoryUsage() => lzma_raw_decoder_memusage(Handle);

    /// <summary>
    /// Returns <see cref="Spec"/>.
    /// </summary>
    public override string ToString() => Spec;

    /// <summary>
    /// Releases the unmanaged filter options held by this chain.
    /// </summary>
    public void Dispose()
    {
        if (_filters is null)
        {
            return;
        }

        // Frees what the entries point at; the array itself is ours.
        lzma_filters_free(_filters, LibLzmaAllocator.Handle);
        NativeMemory.Free(_filters);
        _filters = null;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged filter options if <see cref="Dispose"/> was not called.
    /// </summary>
    ~XZFilterChain()
    {
        Dispose();
    }
}
