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
public static class LibLzmaNativeMethods
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
    private static IntPtr ResolveRuntimeDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Only intercept the specific library
        if (libraryName != LibLzma)
        {
            return IntPtr.Zero; // Fallback to default loading logic
        }

        // Determine the Runtime Identifier (RID), architecture and library extension
        string os =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "unix";
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
        string libraryExt =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";

        // Attempt to load the library from the assembly location directory
        string libPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", $"{libraryName}{libraryExt}");
        if (File.Exists(libPath))
        {
            return NativeLibrary.Load(libPath);
        }

        // Attempt to load the library from the application base directory
        libPath = Path.Combine(AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", $"{libraryName}{libraryExt}");
        if (File.Exists(libPath))
        {
            return NativeLibrary.Load(libPath);
        }

        // Attempt using the default search path
        if (NativeLibrary.TryLoad($"{libraryName}{libraryExt}", assembly, searchPath, out var handle))
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
        public ulong reserved_int5;
        public ulong reserved_int6;
        public ulong reserved_int7;
        public ulong reserved_int8;
        public IntPtr reserved_ptr1;
        public IntPtr reserved_ptr2;
        public IntPtr reserved_ptr3;
        public IntPtr reserved_ptr4;
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
    internal static extern int lzma_easy_encoder(
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
    internal static extern int lzma_stream_encoder_mt(
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
    internal static extern int lzma_stream_decoder(
        ref LzmaStream strm,
        ulong memlimit,
        uint flags);

    /// <summary>
    /// Initializes an auto-detecting decoder that handles both .xz and legacy .lzma formats.
    /// </summary>
    /// <param name="strm">The lzma_stream to initialize.</param>
    /// <param name="memlimit">Maximum memory usage in bytes (<see cref="ulong.MaxValue"/> for no limit).</param>
    /// <param name="flags">Decoder flags (e.g., <see cref="LZMA_CONCATENATED"/>).</param>
    /// <returns><see cref="LZMA_OK"/> on success, or an error code.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static extern int lzma_auto_decoder(
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
    internal static extern int lzma_code(
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
    internal static extern void lzma_end(
        ref LzmaStream strm);

    /// <summary>
    /// Returns the runtime version of liblzma as a single integer.
    /// The format is <c>MAJOR * 10_000_000 + MINOR * 10_000 + PATCH * 10</c>.
    /// For example, version 5.8.3 returns <c>50080030</c>.
    /// </summary>
    /// <returns>The encoded version number.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static extern uint lzma_version_number();

    /// <summary>
    /// Returns the runtime version of liblzma as a null-terminated string (e.g., <c>"5.8.3"</c>).
    /// </summary>
    /// <returns>A pointer to the version string.</returns>
    [LibraryImport(LibLzma)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static extern IntPtr lzma_version_string();

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
}