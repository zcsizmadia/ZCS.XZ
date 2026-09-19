using static ZCS.XZ.LibLzmaNativeMethods;

namespace ZCS.XZ;

/// <summary>
/// Single-call compression and decompression for data already held in memory.
/// </summary>
/// <remarks>
/// <para>
/// These methods map onto the liblzma single-call buffer API and bypass the
/// <see cref="Stream"/> machinery entirely, which makes them a better fit for small
/// payloads than wrapping a <see cref="MemoryStream"/> in an
/// <see cref="XZCompressStream"/>. The output is an ordinary <c>.xz</c> stream,
/// interchangeable with what <see cref="XZCompressStream"/> produces.
/// </para>
/// <para>
/// Compression here is always single-threaded and always uses the <c>.xz</c> format:
/// the <see cref="XZCompressOptions.Threads"/>, <see cref="XZCompressOptions.BufferSize"/>,
/// and <see cref="XZCompressOptions.Format"/> properties do not apply. Use
/// <see cref="XZCompressStream"/> when those matter.
/// </para>
/// </remarks>
public static class XZBuffer
{
    /// <summary>
    /// Returns the worst-case compressed size for <paramref name="uncompressedLength"/> bytes.
    /// </summary>
    /// <remarks>
    /// Compressing incompressible data produces slightly more bytes than the input.
    /// An output buffer of this size is always large enough.
    /// </remarks>
    /// <param name="uncompressedLength">The uncompressed length in bytes.</param>
    /// <returns>The maximum compressed length in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="uncompressedLength"/> is negative.</exception>
    /// <exception cref="OverflowException">The bound does not fit in an <see cref="int"/>.</exception>
    public static int GetMaxCompressedLength(int uncompressedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedLength);

        nuint bound = lzma_stream_buffer_bound((nuint)uncompressedLength);
        if (bound == 0 || bound > int.MaxValue)
        {
            throw new OverflowException(
                $"The compressed bound for {uncompressedLength} bytes does not fit in an Int32.");
        }

        return (int)bound;
    }

    /// <summary>
    /// Compresses <paramref name="source"/> into a new <c>.xz</c> byte array.
    /// </summary>
    /// <param name="source">The data to compress.</param>
    /// <param name="options">
    /// Compression options. Only <see cref="XZCompressOptions.Level"/> and
    /// <see cref="XZCompressOptions.Extreme"/> apply. <c>null</c> uses the defaults.
    /// </param>
    /// <returns>A complete <c>.xz</c> stream.</returns>
    /// <exception cref="XZException">liblzma reported an error.</exception>
    /// <example>
    /// <code>
    /// byte[] packed = XZBuffer.Compress(File.ReadAllBytes("data.bin"));
    /// </code>
    /// </example>
    public static byte[] Compress(ReadOnlySpan<byte> source, XZCompressOptions? options = null)
    {
        byte[] buffer = new byte[GetMaxCompressedLength(source.Length)];
        int written = Compress(source, buffer, options);

        // The bound is a worst case, so the common case is a short write that needs trimming.
        if (written == buffer.Length)
        {
            return buffer;
        }

        return buffer.AsSpan(0, written).ToArray();
    }

    /// <summary>
    /// Compresses <paramref name="source"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">The data to compress.</param>
    /// <param name="destination">
    /// The output buffer. Size it with <see cref="GetMaxCompressedLength"/> to guarantee it fits.
    /// </param>
    /// <param name="options">
    /// Compression options. Only <see cref="XZCompressOptions.Level"/> and
    /// <see cref="XZCompressOptions.Extreme"/> apply. <c>null</c> uses the defaults.
    /// </param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="XZException">
    /// liblzma reported an error, including <c>LZMA_BUF_ERROR</c> when
    /// <paramref name="destination"/> is too small.
    /// </exception>
    public static unsafe int Compress(ReadOnlySpan<byte> source, Span<byte> destination, XZCompressOptions? options = null)
    {
        options ??= new XZCompressOptions();

        nuint outPos = 0;
        int ret;

        fixed (byte* src = source)
        fixed (byte* dst = destination)
        {
            ret = lzma_easy_buffer_encode(
                options.GetPreset(),
                LZMA_CHECK_CRC64,
                IntPtr.Zero,
                src,
                (nuint)source.Length,
                dst,
                ref outPos,
                (nuint)destination.Length);
        }

        ThrowIfError(ret);
        return (int)outPos;
    }

    /// <summary>
    /// Decompresses a complete <c>.xz</c> stream into a new byte array.
    /// </summary>
    /// <remarks>
    /// The uncompressed size is not known up front, so this grows an output buffer and
    /// retries until the data fits. When the size is already known, prefer
    /// <see cref="Decompress(ReadOnlySpan{byte}, Span{byte}, ulong)"/>, which decodes once.
    /// </remarks>
    /// <param name="source">A complete <c>.xz</c> stream.</param>
    /// <param name="memoryLimit">Decoder memory limit in bytes.</param>
    /// <returns>The decompressed data.</returns>
    /// <exception cref="XZException">liblzma reported an error.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> source, ulong memoryLimit = ulong.MaxValue)
    {
        // .xz rarely exceeds this ratio on real data; on a miss the buffer doubles.
        long capacity = Math.Max(source.Length * 4L, 1024L);

        while (true)
        {
            if (capacity > Array.MaxLength)
            {
                capacity = Array.MaxLength;
            }

            byte[] buffer = new byte[(int)capacity];

            if (TryDecompress(source, buffer, memoryLimit, out int written))
            {
                return written == buffer.Length ? buffer : buffer.AsSpan(0, written).ToArray();
            }

            if (capacity == Array.MaxLength)
            {
                throw new XZException(LZMA_BUF_ERROR,
                    "The decompressed data is too large to fit in a single array.");
            }

            capacity *= 2;
        }
    }

    /// <summary>
    /// Decompresses a complete <c>.xz</c> stream into <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">A complete <c>.xz</c> stream.</param>
    /// <param name="destination">
    /// The output buffer. It must be large enough to hold all decompressed data.
    /// </param>
    /// <param name="memoryLimit">Decoder memory limit in bytes.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="XZException">
    /// liblzma reported an error, including <c>LZMA_BUF_ERROR</c> when
    /// <paramref name="destination"/> is too small.
    /// </exception>
    public static int Decompress(ReadOnlySpan<byte> source, Span<byte> destination, ulong memoryLimit = ulong.MaxValue)
    {
        int ret = Decode(source, destination, memoryLimit, out int written);
        ThrowIfError(ret);
        return written;
    }

    private static bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, ulong memoryLimit, out int written)
    {
        int ret = Decode(source, destination, memoryLimit, out written);

        if (ret == LZMA_BUF_ERROR)
        {
            // Output buffer too small: the caller retries with a larger one.
            return false;
        }

        ThrowIfError(ret);
        return true;
    }

    private static unsafe int Decode(ReadOnlySpan<byte> source, Span<byte> destination, ulong memoryLimit, out int written)
    {
        nuint inPos = 0;
        nuint outPos = 0;
        ulong memlimit = memoryLimit;
        int ret;

        fixed (byte* src = source)
        fixed (byte* dst = destination)
        {
            ret = lzma_stream_buffer_decode(
                ref memlimit,
                LZMA_CONCATENATED,
                IntPtr.Zero,
                src,
                ref inPos,
                (nuint)source.Length,
                dst,
                ref outPos,
                (nuint)destination.Length);
        }

        written = (int)outPos;
        return ret;
    }

    private static void ThrowIfError(int ret)
    {
        if (ret != LZMA_OK)
        {
            throw new XZException(ret);
        }
    }
}
