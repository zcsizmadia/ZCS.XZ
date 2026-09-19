using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZCS.XZ;

/// <summary>
/// Provides P/Invoke declarations and constants for the liblzma native library.
/// This class handles automatic native library resolution across platforms
/// (Windows, Linux, macOS) and architectures (x64, arm64, etc.).
/// </summary>
public static partial class LibLzmaNativeMethods
{
    /// <summary>
    /// The name of the native liblzma library
    /// </summary>
    private const string LibLzma = "liblzma";

    /// <summary>
    /// Registers a custom DLL import resolver to locate the liblzma native library
    /// from the runtimes/{rid}/native directory structure at startup.
    /// </summary>
    static LibLzmaNativeMethods()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ResolveRuntimeDll);
    }

    /// <summary>
    /// Custom DLL import resolver that locates the liblzma native library
    /// from the runtimes/{os}-{arch}/native directory structure.
    /// </summary>
    /// <param name="libraryName">The name of the native library to resolve.</param>
    /// <param name="assembly">The assembly that triggered the load.</param>
    /// <param name="searchPath">The DLL import search path hint.</param>
    /// <returns>A handle to the loaded native library, or <see cref="IntPtr.Zero"/> to fall back to default loading.</returns>
    [ExcludeFromCodeCoverage]
    public static IntPtr ResolveRuntimeDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Only intercept the specific library
        if (libraryName != LibLzma)
        {
            return IntPtr.Zero; // Fallback to default loading logic
        }

        string os;
        string libraryNameExt;
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            os = "win";
            libraryNameExt = $"{LibLzma}.dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            os = "osx";
            libraryNameExt = $"{LibLzma}.dylib";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (RuntimeInformation.RuntimeIdentifier.Contains("musl"))
            {
                os = "linux-musl";
            }
            else
            {
                os = "linux";
            }
            libraryNameExt = $"{LibLzma}.so";
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported OS platform");
        }

        // Attempt to load the library from the assembly location directory
        string libPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", $"{libraryNameExt}");
        if (File.Exists(libPath))
        {
            return NativeLibrary.Load(libPath);
        }

        // Attempt to load the library from the application base directory
        libPath = Path.Combine(AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", $"{libraryNameExt}");
        if (File.Exists(libPath))
        {
            return NativeLibrary.Load(libPath);
        }

        // Attempt using the default search path
        if (NativeLibrary.TryLoad($"{libraryNameExt}", assembly, searchPath, out var handle))
        {
            return handle;
        }

        return IntPtr.Zero; // Let the system try its default search paths
    }

    // ──────────────────────────────────────────────
    // lzma_ret return codes from lzma/base.h
    // ──────────────────────────────────────────────

    /// <summary>Operation completed successfully.</summary>
    internal const int LZMA_OK = 0;

    /// <summary>End of stream was reached.</summary>
    internal const int LZMA_STREAM_END = 1;

    /// <summary>Input stream has no integrity check.</summary>
    internal const int LZMA_NO_CHECK = 2;

    /// <summary>Cannot calculate the integrity check.</summary>
    internal const int LZMA_UNSUPPORTED_CHECK = 3;

    /// <summary>Integrity check type is now available.</summary>
    internal const int LZMA_GET_CHECK = 4;

    /// <summary>Cannot allocate memory.</summary>
    internal const int LZMA_MEM_ERROR = 5;

    /// <summary>Memory usage limit was reached.</summary>
    internal const int LZMA_MEMLIMIT_ERROR = 6;

    /// <summary>File format not recognized.</summary>
    internal const int LZMA_FORMAT_ERROR = 7;

    /// <summary>Invalid or unsupported options.</summary>
    internal const int LZMA_OPTIONS_ERROR = 8;

    /// <summary>Data is corrupt.</summary>
    internal const int LZMA_DATA_ERROR = 9;

    /// <summary>No progress is possible (e.g., input needed but not provided).</summary>
    internal const int LZMA_BUF_ERROR = 10;

    /// <summary>Programming error.</summary>
    internal const int LZMA_PROG_ERROR = 11;

    // ──────────────────────────────────────────────
    // lzma_action values from lzma/base.h
    // ──────────────────────────────────────────────

    /// <summary>Continue coding (encode or decode more data).</summary>
    internal const int LZMA_RUN = 0;

    /// <summary>Make all buffered data available at output.</summary>
    internal const int LZMA_SYNC_FLUSH = 1;

    /// <summary>Finish encoding of the current block.</summary>
    internal const int LZMA_FULL_FLUSH = 2;

    /// <summary>Finish the coding operation.</summary>
    internal const int LZMA_FINISH = 3;

    /// <summary>A full barrier for multithreaded encoding.</summary>
    internal const int LZMA_FULL_BARRIER = 4;

    // ──────────────────────────────────────────────
    // lzma_check values from lzma/check.h
    // ──────────────────────────────────────────────

    /// <summary>No integrity check. The only check type the legacy .lzma format supports.</summary>
    internal const int LZMA_CHECK_NONE = 0;

    /// <summary>CRC64 integrity check using the ECMA-182 polynomial.</summary>
    internal const int LZMA_CHECK_CRC64 = 4;

    // ──────────────────────────────────────────────
    // Decoder flags from lzma/container.h
    // ──────────────────────────────────────────────

    /// <summary>
    /// Flag for <see cref="lzma_auto_decoder"/> to decode concatenated streams.
    /// When set, the decoder will validate trailing data and process
    /// multiple concatenated .xz or .lzma members in a single stream.
    /// </summary>
    internal const uint LZMA_CONCATENATED = 0x08;

    // ──────────────────────────────────────────────
    // Preset flags from lzma/container.h
    // ──────────────────────────────────────────────

    /// <summary>
    /// Extreme compression mode flag. When OR'd with a preset level,
    /// enables a slower but marginally better compression ratio.
    /// </summary>
    internal const uint LZMA_PRESET_EXTREME = 1u << 31;

    // ──────────────────────────────────────────────
    // Native structures
    // ──────────────────────────────────────────────

    /// <summary>
    /// Managed representation of the native lzma_stream structure.
    /// This struct is passed by reference to all liblzma coding functions
    /// and tracks input/output buffer pointers, byte counts, and internal state.
    /// Must be kept in sync with the layout defined in lzma/base.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LzmaStream
    {
        /// <summary>Pointer to the next input byte.</summary>
        public IntPtr next_in;

        /// <summary>Number of available input bytes at <see cref="next_in"/>.</summary>
        public UIntPtr avail_in;

        /// <summary>Total number of input bytes read so far.</summary>
        public ulong total_in;

        /// <summary>Pointer to the next output position.</summary>
        public IntPtr next_out;

        /// <summary>Number of available output bytes at <see cref="next_out"/>.</summary>
        public UIntPtr avail_out;

        /// <summary>Total number of output bytes written so far.</summary>
        public ulong total_out;

        /// <summary>Custom memory allocator (unused, set to <see cref="IntPtr.Zero"/>).</summary>
        public IntPtr allocator;

        /// <summary>Pointer to the internal encoder/decoder state (opaque).</summary>
        public IntPtr internal_state;

        // Reserved pointers and integers for future use by liblzma.
        // These must be present to maintain the correct struct layout.
        public IntPtr reserved_ptr1;
        public IntPtr reserved_ptr2;
        public IntPtr reserved_ptr3;
        public IntPtr reserved_ptr4;
        public ulong reserved_int1;
        public ulong reserved_int2;
        public UIntPtr reserved_int3;
        public UIntPtr reserved_int4;
        public uint reserved_enum1;
        public uint reserved_enum2;
    }

    /// <summary>
    /// Managed representation of the native lzma_mt (multithreading options) structure.
    /// Used with <see cref="lzma_stream_encoder_mt"/> to configure parallel compression.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LzmaMt
    {
        /// <summary>Flags (currently unused, set to 0).</summary>
        public uint flags;

        /// <summary>Number of worker threads.</summary>
        public uint threads;

        /// <summary>Encoder block size (0 = automatic).</summary>
        public ulong block_size;

        /// <summary>Timeout in milliseconds for flushing (0 = disabled).</summary>
        public uint timeout;

        /// <summary>Compression preset level (0–9, optionally OR'd with <see cref="LZMA_PRESET_EXTREME"/>).</summary>
        public uint preset;

        /// <summary>Pointer to custom filter chain (IntPtr.Zero = use preset).</summary>
        public IntPtr filters;

        /// <summary>Integrity check type (e.g., <see cref="LZMA_CHECK_CRC64"/>).</summary>
        public int check;

        // Reserved fields for future use by liblzma.
        public uint reserved_enum1;
        public uint reserved_enum2;
        public uint reserved_enum3;
        public uint reserved_int1;
        public uint reserved_int2;
        public uint reserved_int3;
        public uint reserved_int4;

        /// <summary>
        /// Decoder only: soft memory limit that reduces the worker thread count.
        /// When exceeded, liblzma lowers the number of threads instead of failing,
        /// so this never causes <see cref="LZMA_MEMLIMIT_ERROR"/>.
        /// liblzma clamps this to a minimum of 1; a value of 1 disables threading.
        /// Ignored by the encoder.
        /// </summary>
        public ulong memlimit_threading;

        /// <summary>
        /// Decoder only: hard memory limit. If decoding needs more than this even in
        /// single-threaded mode, <see cref="lzma_code"/> returns <see cref="LZMA_MEMLIMIT_ERROR"/>.
        /// Ignored by the encoder.
        /// </summary>
        public ulong memlimit_stop;

        public ulong reserved_int7;
        public ulong reserved_int8;
        public IntPtr reserved_ptr1;
        public IntPtr reserved_ptr2;
        public IntPtr reserved_ptr3;
        public IntPtr reserved_ptr4;
    }

    /// <summary>
    /// Maximum number of filters in a chain, from lzma/filter.h. A filter array must have
    /// room for one more element than this, for the terminator.
    /// </summary>
    internal const int LZMA_FILTERS_MAX = 4;

    /// <summary>
    /// Sentinel value marking the end of a filter array, from lzma/vli.h.
    /// </summary>
    internal const ulong LZMA_VLI_UNKNOWN = ulong.MaxValue;

    /// <summary>Include filters that are not supported in the .xz format.</summary>
    internal const uint LZMA_STR_ALL_FILTERS = 0x01;

    /// <summary>Produce a string describing the encoder-side options.</summary>
    internal const uint LZMA_STR_ENCODER = 0x10;

    /// <summary>Produce a string describing the decoder-side options.</summary>
    internal const uint LZMA_STR_DECODER = 0x20;

    /// <summary>
    /// Managed representation of the native lzma_filter structure from lzma/filter.h.
    /// An array of these describes a filter chain, terminated by an entry whose
    /// <see cref="id"/> is <see cref="LZMA_VLI_UNKNOWN"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LzmaFilter
    {
        /// <summary>The filter ID, or <see cref="LZMA_VLI_UNKNOWN"/> to terminate the array.</summary>
        public ulong id;

        /// <summary>Pointer to the filter-specific options struct, allocated by liblzma.</summary>
        public IntPtr options;
    }

    /// <summary>
    /// Managed representation of the native lzma_options_lzma structure from lzma/lzma12.h.
    /// Used with <see cref="lzma_alone_encoder"/> to configure legacy .lzma encoding.
    /// Populate it with <see cref="lzma_lzma_preset"/> rather than by hand.
    /// Must be kept in sync with the layout defined in lzma/lzma12.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LzmaOptionsLzma
    {
        /// <summary>Dictionary size in bytes.</summary>
        public uint dict_size;

        /// <summary>Pointer to a preset dictionary (unused, set to <see cref="IntPtr.Zero"/>).</summary>
        public IntPtr preset_dict;

        /// <summary>Size of the preset dictionary.</summary>
        public uint preset_dict_size;

        /// <summary>Number of literal context bits.</summary>
        public uint lc;

        /// <summary>Number of literal position bits.</summary>
        public uint lp;

        /// <summary>Number of position bits.</summary>
        public uint pb;

        /// <summary>Compression mode (lzma_mode).</summary>
        public uint mode;

        /// <summary>Nice length of a match.</summary>
        public uint nice_len;

        /// <summary>Match finder ID (lzma_match_finder).</summary>
        public uint mf;

        /// <summary>Match finder cycles.</summary>
        public uint depth;

        /// <summary>Extended flags.</summary>
        public uint ext_flags;

        /// <summary>Low 32 bits of the extended uncompressed size.</summary>
        public uint ext_size_low;

        /// <summary>High 32 bits of the extended uncompressed size.</summary>
        public uint ext_size_high;

        // Reserved fields for future use by liblzma.
        public uint reserved_int4;
        public uint reserved_int5;
        public uint reserved_int6;
        public uint reserved_int7;
        public uint reserved_int8;
        public uint reserved_enum1;
        public uint reserved_enum2;
        public uint reserved_enum3;
        public uint reserved_enum4;
        public IntPtr reserved_ptr1;
        public IntPtr reserved_ptr2;
    }

    // ──────────────────────────────────────────────
    // P/Invoke declarations
    // ──────────────────────────────────────────────

    /// <summary>
    /// Initializes a single-threaded .xz encoder with the given preset and integrity check.
    /// </summary>
    /// <param name="strm">The lzma_stream to initialize.</param>
    /// <param name="preset">Compression preset (0–9, optionally OR'd with extreme flag).</param>
    /// <param name="check">Integrity check type.</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_easy_encoder(
        ref LzmaStream strm,
        uint preset,
        int check);

    /// <summary>
    /// Initializes a multithreaded .xz encoder with the given options.
    /// </summary>
    /// <param name="strm">The lzma_stream to initialize.</param>
    /// <param name="options">Multithreading and compression options.</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_stream_encoder_mt(
        ref LzmaStream strm,
        ref LzmaMt options);

    /// <summary>
    /// Initializes a .xz stream decoder with the given memory limit and flags.
    /// </summary>
    /// <param name="strm">The lzma_stream to initialize.</param>
    /// <param name="memlimit">Maximum memory usage in bytes (<see cref="ulong.MaxValue"/> for no limit).</param>
    /// <param name="flags">Decoder flags (e.g., <see cref="LZMA_CONCATENATED"/>).</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_stream_decoder(
        ref LzmaStream strm,
        ulong memlimit,
        uint flags);

    /// <summary>
    /// Initializes a multithreaded .xz stream decoder.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="lzma_auto_decoder"/>, this decoder handles the .xz format only —
    /// it does not auto-detect legacy .lzma or .lz input. Parallelism additionally requires
    /// Block Headers carrying compressed and uncompressed sizes (as written by
    /// <see cref="lzma_stream_encoder_mt"/>); other streams decode single-threaded.
    /// Only the <c>threads</c>, <c>flags</c>, <c>timeout</c>, <c>memlimit_threading</c>,
    /// and <c>memlimit_stop</c> members of <paramref name="options"/> are read.
    /// </remarks>
    /// <param name="strm">The lzma_stream to initialize.</param>
    /// <param name="options">Multithreading and memory-limit options.</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_stream_decoder_mt(
        ref LzmaStream strm,
        ref LzmaMt options);

    /// <summary>
    /// Initializes an auto-detecting decoder that handles both .xz and legacy .lzma formats.
    /// </summary>
    /// <param name="strm">The lzma_stream to initialize.</param>
    /// <param name="memlimit">Maximum memory usage in bytes (<see cref="ulong.MaxValue"/> for no limit).</param>
    /// <param name="flags">Decoder flags (e.g., <see cref="LZMA_CONCATENATED"/>).</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_auto_decoder(
        ref LzmaStream strm,
        ulong memlimit,
        uint flags);

    /// <summary>
    /// Encodes or decodes data. Call repeatedly with <see cref="LZMA_RUN"/> while there is
    /// input to process, then with <see cref="LZMA_FINISH"/> to complete the operation.
    /// </summary>
    /// <param name="strm">The lzma_stream containing input/output buffer state.</param>
    /// <param name="action">The action to perform (e.g., <see cref="LZMA_RUN"/>, <see cref="LZMA_FINISH"/>).</param>
    /// <returns><see cref="LZMA_OK"/> if progress was made, <see cref="LZMA_STREAM_END"/> when finished, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_code(
        ref LzmaStream strm,
        int action);

    /// <summary>
    /// Frees all resources associated with the lzma_stream. Must be called once
    /// when the encoder/decoder is no longer needed to avoid memory leaks.
    /// Safe to call on a zeroed or already-freed stream.
    /// </summary>
    /// <param name="strm">The lzma_stream to free.</param>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void lzma_end(
        ref LzmaStream strm);

    /// <summary>
    /// Initializes a single-threaded .xz encoder using an explicit filter chain.
    /// </summary>
    /// <remarks>
    /// This is the filter-chain counterpart to <see cref="lzma_easy_encoder"/>, which can
    /// only express a preset. liblzma copies the chain during initialization, so the array
    /// need only stay valid for the duration of this call.
    /// </remarks>
    /// <param name="strm">The lzma_stream to initialize.</param>
    /// <param name="filters">The filter chain, terminated by <see cref="LZMA_VLI_UNKNOWN"/>.</param>
    /// <param name="check">Integrity check type.</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int lzma_stream_encoder(
        ref LzmaStream strm,
        LzmaFilter* filters,
        int check);

    /// <summary>
    /// Parses a human-readable filter chain string, such as <c>"x86 lzma2:preset=9e"</c>.
    /// </summary>
    /// <param name="str">The filter chain specification.</param>
    /// <param name="error_pos">On failure, the offset in <paramref name="str"/> of the problem.</param>
    /// <param name="filters">
    /// An array of at least <see cref="LZMA_FILTERS_MAX"/> + 1 elements to populate. The
    /// options each entry points at are allocated with <paramref name="allocator"/> and must
    /// be released with <see cref="lzma_filters_free"/>.
    /// </param>
    /// <param name="flags">Parsing flags, such as <see cref="LZMA_STR_ALL_FILTERS"/>.</param>
    /// <param name="allocator">Allocator to use, or <see cref="IntPtr.Zero"/> for malloc.</param>
    /// <returns>
    /// <see cref="IntPtr.Zero"/> on success. On failure, a pointer to a statically allocated
    /// error message, which must <em>not</em> be freed. Note this function reports errors by
    /// returning a string rather than an <c>lzma_ret</c>.
    /// </returns>
    [LibraryImport(LibLzma, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial IntPtr lzma_str_to_filters(
        string str,
        out int error_pos,
        LzmaFilter* filters,
        uint flags,
        IntPtr allocator);

    /// <summary>
    /// Converts a filter chain back into its human-readable string form.
    /// </summary>
    /// <param name="str">
    /// Receives a pointer to a newly allocated null-terminated string, which must be
    /// released with the same allocator that produced it.
    /// </param>
    /// <param name="filters">The filter chain to describe.</param>
    /// <param name="flags">Formatting flags, such as <see cref="LZMA_STR_ENCODER"/>.</param>
    /// <param name="allocator">Allocator to use, or <see cref="IntPtr.Zero"/> for malloc.</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int lzma_str_from_filters(
        out IntPtr str,
        LzmaFilter* filters,
        uint flags,
        IntPtr allocator);

    /// <summary>
    /// Produces a human-readable listing of the supported filters and their options.
    /// </summary>
    /// <param name="str">
    /// Receives a pointer to a newly allocated null-terminated string, which must be
    /// released with the same allocator that produced it.
    /// </param>
    /// <param name="filter_id">A specific filter ID, or <see cref="LZMA_VLI_UNKNOWN"/> for all.</param>
    /// <param name="flags">Formatting flags.</param>
    /// <param name="allocator">Allocator to use, or <see cref="IntPtr.Zero"/> for malloc.</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_str_list_filters(
        out IntPtr str,
        ulong filter_id,
        uint flags,
        IntPtr allocator);

    /// <summary>
    /// Frees the options structs referenced by a filter chain.
    /// </summary>
    /// <remarks>
    /// This releases what each entry points at and resets the ids; the array itself belongs
    /// to the caller. Passing a different allocator than the one that populated the chain is
    /// undefined behavior.
    /// </remarks>
    /// <param name="filters">The filter chain to release.</param>
    /// <param name="allocator">The allocator originally used, or <see cref="IntPtr.Zero"/>.</param>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial void lzma_filters_free(
        LzmaFilter* filters,
        IntPtr allocator);

    /// <summary>
    /// Returns the approximate memory usage of an encoder using the given filter chain.
    /// </summary>
    /// <param name="filters">The filter chain.</param>
    /// <returns>Memory usage in bytes, or <see cref="ulong.MaxValue"/> if the chain is invalid.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial ulong lzma_raw_encoder_memusage(LzmaFilter* filters);

    /// <summary>
    /// Returns the approximate memory usage of a decoder using the given filter chain.
    /// </summary>
    /// <param name="filters">The filter chain.</param>
    /// <returns>Memory usage in bytes, or <see cref="ulong.MaxValue"/> if the chain is invalid.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial ulong lzma_raw_decoder_memusage(LzmaFilter* filters);

    /// <summary>
    /// Initializes an encoder for the legacy .lzma (LZMA_Alone) format.
    /// </summary>
    /// <remarks>
    /// The .lzma format carries no integrity check and supports neither multithreading
    /// nor <see cref="LZMA_SYNC_FLUSH"/>; the encoder accepts only <see cref="LZMA_RUN"/>
    /// and <see cref="LZMA_FINISH"/>.
    /// </remarks>
    /// <param name="strm">The lzma_stream to initialize.</param>
    /// <param name="options">LZMA1 options, normally filled by <see cref="lzma_lzma_preset"/>.</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_alone_encoder(
        ref LzmaStream strm,
        ref LzmaOptionsLzma options);

    /// <summary>
    /// Fills <paramref name="options"/> with the settings for the given preset.
    /// </summary>
    /// <param name="options">The options struct to populate.</param>
    /// <param name="preset">Compression preset (0–9, optionally OR'd with the extreme flag).</param>
    /// <returns>
    /// <c>true</c> if the preset is <em>not</em> supported (failure), <c>false</c> on success.
    /// Note the inverted sense, which matches liblzma.
    /// </returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool lzma_lzma_preset(
        ref LzmaOptionsLzma options,
        uint preset);

    /// <summary>
    /// Reports the progress of an encoder or decoder.
    /// </summary>
    /// <remarks>
    /// Unlike <c>total_in</c>/<c>total_out</c> on the stream, this is accurate for the
    /// multithreaded coders, where work is buffered across threads.
    /// </remarks>
    /// <param name="strm">The lzma_stream to query.</param>
    /// <param name="progress_in">Receives the number of input bytes processed.</param>
    /// <param name="progress_out">Receives the number of output bytes processed.</param>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void lzma_get_progress(
        ref LzmaStream strm,
        out ulong progress_in,
        out ulong progress_out);

    /// <summary>
    /// Compresses a buffer into a complete .xz stream in a single call.
    /// </summary>
    /// <param name="preset">Compression preset.</param>
    /// <param name="check">Integrity check type.</param>
    /// <param name="allocator">Custom allocator (unused, pass <see cref="IntPtr.Zero"/>).</param>
    /// <param name="in">Pointer to the input buffer.</param>
    /// <param name="in_size">Number of input bytes.</param>
    /// <param name="out">Pointer to the output buffer.</param>
    /// <param name="out_pos">On input the write offset, on output the number of bytes written.</param>
    /// <param name="out_size">Capacity of the output buffer.</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int lzma_easy_buffer_encode(
        uint preset,
        int check,
        IntPtr allocator,
        byte* @in,
        nuint in_size,
        byte* @out,
        ref nuint out_pos,
        nuint out_size);

    /// <summary>
    /// Decompresses a complete .xz stream from a buffer in a single call.
    /// </summary>
    /// <param name="memlimit">On input the memory limit, updated by liblzma as needed.</param>
    /// <param name="flags">Decoder flags.</param>
    /// <param name="allocator">Custom allocator (unused, pass <see cref="IntPtr.Zero"/>).</param>
    /// <param name="in">Pointer to the input buffer.</param>
    /// <param name="in_pos">On input the read offset, on output the number of bytes consumed.</param>
    /// <param name="in_size">Number of input bytes available.</param>
    /// <param name="out">Pointer to the output buffer.</param>
    /// <param name="out_pos">On input the write offset, on output the number of bytes written.</param>
    /// <param name="out_size">Capacity of the output buffer.</param>
    /// <returns>
    /// <see cref="LZMA_OK"/> on success, <see cref="LZMA_BUF_ERROR"/> if the output buffer
    /// is too small, or another error code.
    /// </returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int lzma_stream_buffer_decode(
        ref ulong memlimit,
        uint flags,
        IntPtr allocator,
        byte* @in,
        ref nuint in_pos,
        nuint in_size,
        byte* @out,
        ref nuint out_pos,
        nuint out_size);

    /// <summary>
    /// Returns the worst-case .xz output size for the given uncompressed size.
    /// </summary>
    /// <param name="uncompressed_size">The uncompressed size in bytes.</param>
    /// <returns>The maximum compressed size, or 0 if the result would overflow.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint lzma_stream_buffer_bound(nuint uncompressed_size);

    /// <summary>
    /// Computes a CRC32 checksum using the IEEE 802.3 polynomial.
    /// </summary>
    /// <param name="buf">Pointer to the data.</param>
    /// <param name="size">Number of bytes.</param>
    /// <param name="crc">The running CRC to continue from.</param>
    /// <returns>The updated CRC32 value.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial uint lzma_crc32(
        byte* buf,
        nuint size,
        uint crc);

    /// <summary>
    /// Computes a CRC64 checksum using the ECMA-182 polynomial.
    /// </summary>
    /// <param name="buf">Pointer to the data.</param>
    /// <param name="size">Number of bytes.</param>
    /// <param name="crc">The running CRC to continue from.</param>
    /// <returns>The updated CRC64 value.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial ulong lzma_crc64(
        byte* buf,
        nuint size,
        ulong crc);

    /// <summary>
    /// Returns the integrity check type of the .xz stream currently being decoded.
    /// </summary>
    /// <remarks>
    /// The result is only meaningful after the stream header has been decoded, i.e. after
    /// at least one <see cref="lzma_code"/> call has consumed the header. Before that,
    /// liblzma reports <c>LZMA_CHECK_NONE</c>.
    /// </remarks>
    /// <param name="strm">The lzma_stream being decoded.</param>
    /// <returns>The lzma_check value for the stream.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_get_check(
        ref LzmaStream strm);

    /// <summary>
    /// Returns the approximate memory usage, in bytes, of a single-threaded encoder
    /// initialized with the given preset.
    /// </summary>
    /// <param name="preset">Compression preset (0–9, optionally OR'd with the extreme flag).</param>
    /// <returns>Memory usage in bytes, or <see cref="ulong.MaxValue"/> if the preset is invalid.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong lzma_easy_encoder_memusage(uint preset);

    /// <summary>
    /// Returns the approximate memory usage, in bytes, needed to <em>decode</em> a stream
    /// that was produced with the given preset.
    /// </summary>
    /// <param name="preset">Compression preset the stream was created with.</param>
    /// <returns>Memory usage in bytes, or <see cref="ulong.MaxValue"/> if the preset is invalid.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong lzma_easy_decoder_memusage(uint preset);

    /// <summary>
    /// Returns the approximate memory usage, in bytes, of a multithreaded encoder
    /// configured with the given options.
    /// </summary>
    /// <param name="options">Multithreading and compression options.</param>
    /// <returns>Memory usage in bytes, or <see cref="ulong.MaxValue"/> if the options are invalid.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong lzma_stream_encoder_mt_memusage(
        ref LzmaMt options);

    /// <summary>
    /// Returns the current memory usage limit of a decoder.
    /// </summary>
    /// <param name="strm">The lzma_stream being decoded.</param>
    /// <returns>The limit in bytes.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong lzma_memlimit_get(
        ref LzmaStream strm);

    /// <summary>
    /// Changes the memory usage limit of a running decoder.
    /// </summary>
    /// <param name="strm">The lzma_stream being decoded.</param>
    /// <param name="memlimit">The new limit in bytes. Zero is treated as 1 byte.</param>
    /// <returns>
    /// <see cref="LZMA_OK"/> on success, or <see cref="LZMA_MEMLIMIT_ERROR"/> if the new
    /// limit is below what the decoder has already allocated.
    /// </returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int lzma_memlimit_set(
        ref LzmaStream strm,
        ulong memlimit);

    /// <summary>
    /// Returns the number of hardware threads liblzma detects, or 0 if detection failed.
    /// </summary>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint lzma_cputhreads();

    /// <summary>
    /// Returns the total amount of physical memory in bytes, or 0 if detection failed.
    /// </summary>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong lzma_physmem();

    /// <summary>
    /// Returns the runtime version of liblzma as a single integer.
    /// The format is <c>MAJOR * 10_000_000 + MINOR * 10_000 + PATCH * 10</c>.
    /// For example, version 5.8.3 returns <c>50080030</c>.
    /// </summary>
    /// <returns>The encoded version number.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint lzma_version_number();

    /// <summary>
    /// Returns the runtime version of liblzma as a null-terminated string (e.g., <c>"5.8.3"</c>).
    /// </summary>
    /// <returns>A pointer to the version string.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr lzma_version_string();

    // ──────────────────────────────────────────────
    // Public version API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Gets the runtime version of the loaded liblzma native library as a <see cref="Version"/> object.
    /// </summary>
    /// <example>
    /// <code>
    /// Version ver = LibLzmaNativeMethods.NativeVersion;
    /// Console.WriteLine(ver); // e.g., "5.8.3"
    /// </code>
    /// </example>
    public static Version NativeVersion
    {
        get
        {
            uint v = lzma_version_number();
            int major = (int)(v / 10_000_000);
            int minor = (int)(v / 10_000 % 1_000);
            int patch = (int)(v / 10 % 1_000);
            return new Version(major, minor, patch);
        }
    }

    /// <summary>
    /// Gets the runtime version of the loaded liblzma native library as a string (e.g., <c>"5.8.3"</c>).
    /// </summary>
    public static string NativeVersionString => Marshal.PtrToStringAnsi(lzma_version_string())!;

    /// <summary>
    /// Gets the number of hardware threads liblzma detects on this machine.
    /// </summary>
    /// <remarks>
    /// This is the same detection <c>xz</c> itself uses to pick a default thread count.
    /// Returns <see cref="Environment.ProcessorCount"/> if liblzma cannot detect the
    /// thread count on the current platform (in which case it reports 0).
    /// </remarks>
    public static int CpuThreads
    {
        get
        {
            uint threads = lzma_cputhreads();
            return threads == 0 ? Environment.ProcessorCount : (int)threads;
        }
    }

    /// <summary>
    /// Gets the total amount of physical memory on this machine, in bytes,
    /// or 0 if liblzma cannot detect it on the current platform.
    /// </summary>
    public static ulong PhysicalMemory => lzma_physmem();
}