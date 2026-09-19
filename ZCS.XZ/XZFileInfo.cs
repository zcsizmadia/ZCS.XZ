using System.Runtime.InteropServices;
using static ZCS.XZ.LibLzmaNativeMethods;

namespace ZCS.XZ;

/// <summary>
/// The index of an <c>.xz</c> file: its uncompressed size, block layout, and integrity
/// check types, read without decompressing the contents.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>.xz</c> file ends with an index recording the size and position of each block.
/// Reading it is the equivalent of <c>xz --list</c>, and answers "how large is this when
/// unpacked" in a few seeks rather than by decompressing the whole file.
/// </para>
/// <para>
/// The parsed index also drives random access — see <see cref="XZSeekableDecompressStream"/>,
/// which uses it to jump straight to the block holding a given offset.
/// </para>
/// <para>
/// The source stream must be seekable, because the decoder reads the footer first and then
/// works backwards. Instances hold unmanaged memory and must be disposed.
/// </para>
/// <example>
/// <code>
/// using var file = File.OpenRead("data.xz");
/// using var info = XZFileInfo.Read(file);
/// Console.WriteLine($"{info.UncompressedSize} bytes in {info.BlockCount} blocks");
/// </code>
/// </example>
/// </remarks>
public sealed class XZFileInfo : IDisposable
{
    private IntPtr _index;

    private XZFileInfo(IntPtr index)
    {
        _index = index;

        UncompressedSize = checked((long)lzma_index_uncompressed_size(index));
        CompressedSize = checked((long)lzma_index_file_size(index));
        BlockCount = checked((long)lzma_index_block_count(index));
        StreamCount = checked((long)lzma_index_stream_count(index));
        Checks = DecodeChecks(lzma_index_checks(index));
    }

    /// <summary>Gets the total size of the data after decompression, in bytes.</summary>
    public long UncompressedSize { get; }

    /// <summary>
    /// Gets the total size of the file the index describes, in bytes, including stream
    /// headers, footers, and padding.
    /// </summary>
    public long CompressedSize { get; }

    /// <summary>
    /// Gets the number of blocks across all streams. Only a file with more than one block
    /// can be decoded, or seeked within, in parallel.
    /// </summary>
    public long BlockCount { get; }

    /// <summary>Gets the number of concatenated streams in the file.</summary>
    public long StreamCount { get; }

    /// <summary>
    /// Gets the integrity check types used in the file, in ascending order. A file with
    /// concatenated streams may use more than one.
    /// </summary>
    public IReadOnlyList<LzmaCheck> Checks { get; }

    /// <summary>
    /// Gets the ratio of compressed to uncompressed size, or 0 when the file is empty.
    /// </summary>
    public double CompressionRatio
        => UncompressedSize == 0 ? 0 : (double)CompressedSize / UncompressedSize;

    /// <summary>
    /// Gets the parsed native index, for use by <see cref="XZSeekableDecompressStream"/>.
    /// </summary>
    internal IntPtr IndexHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_index == IntPtr.Zero, this);
            return _index;
        }
    }

    /// <summary>
    /// Reads the index of an <c>.xz</c> file.
    /// </summary>
    /// <param name="stream">
    /// A readable, seekable stream positioned anywhere; the whole stream is treated as the
    /// file. Its position is not preserved.
    /// </param>
    /// <param name="memoryLimit">Memory limit for parsing the index, in bytes.</param>
    /// <returns>The parsed index. Dispose it when finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable and seekable.</exception>
    /// <exception cref="XZException">The file is not a valid <c>.xz</c> file.</exception>
    public static XZFileInfo Read(Stream stream, ulong memoryLimit = ulong.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "The stream must be readable and seekable to read an .xz index.", nameof(stream));
        }

        return new XZFileInfo(ParseIndex(stream, memoryLimit));
    }

    private static unsafe IntPtr ParseIndex(Stream stream, ulong memoryLimit)
    {
        var lzmaStream = new LzmaStream();
        ulong fileSize = (ulong)stream.Length;

        // liblzma writes the index through this pointer when decoding finishes, so the slot
        // must outlive the initialization call and never move.
        IntPtr* indexSlot = (IntPtr*)NativeMemory.AllocZeroed((nuint)sizeof(IntPtr));

        int ret = lzma_file_info_decoder(ref lzmaStream, indexSlot, memoryLimit, fileSize);
        if (ret != LZMA_OK)
        {
            NativeMemory.Free(indexSlot);
            throw new XZException(ret);
        }

        var buffer = new byte[8192];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            // The decoder starts by asking to seek to the footer, so honour its requested
            // position rather than streaming linearly from the start.
            stream.Seek(0, SeekOrigin.Begin);

            while (true)
            {
                if (lzmaStream.avail_in == UIntPtr.Zero)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    lzmaStream.next_in = handle.AddrOfPinnedObject();
                    lzmaStream.avail_in = (UIntPtr)read;

                    if (read == 0)
                    {
                        // Ran out of input before the index was complete.
                        throw new XZException(LZMA_DATA_ERROR,
                            "Unexpected end of file while reading the .xz index.");
                    }
                }

                // The file info decoder produces no output.
                lzmaStream.next_out = IntPtr.Zero;
                lzmaStream.avail_out = UIntPtr.Zero;

                ret = lzma_code(ref lzmaStream, LZMA_RUN);

                if (ret == LZMA_STREAM_END)
                {
                    return *indexSlot;
                }

                if (ret == LZMA_SEEK_NEEDED)
                {
                    stream.Seek((long)lzmaStream.seek_pos, SeekOrigin.Begin);
                    lzmaStream.avail_in = UIntPtr.Zero;
                    continue;
                }

                if (ret != LZMA_OK)
                {
                    throw new XZException(ret);
                }
            }
        }
        catch
        {
            // The index is only populated on success, but free it if it was.
            if (*indexSlot != IntPtr.Zero)
            {
                lzma_index_end(*indexSlot, IntPtr.Zero);
            }

            throw;
        }
        finally
        {
            lzma_end(ref lzmaStream);
            NativeMemory.Free(indexSlot);

            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    private static IReadOnlyList<LzmaCheck> DecodeChecks(uint mask)
    {
        var checks = new List<LzmaCheck>();

        for (int id = 0; id <= 15; id++)
        {
            if ((mask & (1u << id)) != 0)
            {
                checks.Add((LzmaCheck)id);
            }
        }

        return checks;
    }

    /// <summary>
    /// Releases the parsed index.
    /// </summary>
    public void Dispose()
    {
        if (_index == IntPtr.Zero)
        {
            return;
        }

        lzma_index_end(_index, IntPtr.Zero);
        _index = IntPtr.Zero;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the parsed index if <see cref="Dispose"/> was not called.
    /// </summary>
    ~XZFileInfo()
    {
        Dispose();
    }
}
