using System.Runtime.InteropServices;
using static ZCS.XZ.LibLzmaNativeMethods;

namespace ZCS.XZ;

/// <summary>
/// A seekable, read-only view over an <c>.xz</c> file, backed by the file's index.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XZDecompressStream"/> decodes forward only, because a compressed stream cannot
/// be entered part-way through. This class reads the index first (see
/// <see cref="XZFileInfo"/>), which records where every block starts, and then decodes
/// individual blocks on demand. That makes <see cref="Length"/> and <see cref="Seek"/> real
/// operations rather than a scan from the beginning.
/// </para>
/// <para>
/// Seeking jumps to the block containing the target offset and decodes forward from that
/// block's start, so the cost is bounded by the block size rather than the file size. A file
/// written as a single block therefore gains little: use
/// <see cref="XZCompressOptions.Threads"/> when writing, since the multithreaded encoder
/// splits output into several blocks.
/// </para>
/// <para>
/// Only the <c>.xz</c> format has an index, so legacy <c>.lzma</c> and <c>.lz</c> input is
/// not supported here; use <see cref="XZDecompressStream"/> for those.
/// </para>
/// <example>
/// <code>
/// using var file = File.OpenRead("data.xz");
/// using var xz = new XZSeekableDecompressStream(file);
///
/// Console.WriteLine($"{xz.Length} bytes uncompressed");
/// xz.Seek(-1024, SeekOrigin.End);
/// xz.ReadExactly(tail);
/// </code>
/// </example>
/// </remarks>
public sealed unsafe class XZSeekableDecompressStream : Stream
{
    private readonly Stream _innerStream;
    private readonly bool _leaveOpen;
    private readonly bool _ownsFileInfo;
    private readonly XZFileInfo _fileInfo;
    private readonly byte[] _inputBuffer;
    private readonly byte[] _outputBuffer;

    private GCHandle _inputHandle;
    private GCHandle _outputHandle;

    private LzmaStream _lzmaStream;
    private LzmaBlock _block;
    private LzmaFilter* _blockFilters;
    private bool _blockOpen;

    /// <summary>Uncompressed offset of the first byte of the open block.</summary>
    private long _blockStart;

    /// <summary>Uncompressed offset one past the last byte of the open block.</summary>
    private long _blockEnd;

    /// <summary>Offset into <see cref="_outputBuffer"/> of the first unconsumed byte.</summary>
    private int _decodedOffset;

    /// <summary>Number of unconsumed decoded bytes in <see cref="_outputBuffer"/>.</summary>
    private int _decodedCount;

    private long _position;
    private bool _blockFinished;
    private bool _disposed;

    /// <summary>
    /// Opens a seekable view over an <c>.xz</c> file, reading its index first.
    /// </summary>
    /// <param name="innerStream">A readable, seekable stream containing a complete <c>.xz</c> file.</param>
    /// <param name="leaveOpen">
    /// <c>true</c> to leave <paramref name="innerStream"/> open when this stream is disposed.
    /// </param>
    /// <param name="bufferSize">Size in bytes of the internal input and output buffers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not readable and seekable.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bufferSize"/> is zero or negative.</exception>
    /// <exception cref="XZException">The input is not a valid <c>.xz</c> file.</exception>
    public XZSeekableDecompressStream(Stream innerStream, bool leaveOpen = false, int bufferSize = 81920)
        : this(innerStream, ReadFileInfo(innerStream), ownsFileInfo: true, leaveOpen, bufferSize)
    {
    }

    /// <summary>
    /// Opens a seekable view using an index that has already been read.
    /// </summary>
    /// <remarks>
    /// Useful when the index was needed beforehand, for example to report the uncompressed
    /// size, so it is not parsed twice. The caller retains ownership of
    /// <paramref name="fileInfo"/> and must keep it alive for the lifetime of this stream.
    /// </remarks>
    /// <param name="innerStream">A readable, seekable stream containing a complete <c>.xz</c> file.</param>
    /// <param name="fileInfo">The index of <paramref name="innerStream"/>.</param>
    /// <param name="leaveOpen">
    /// <c>true</c> to leave <paramref name="innerStream"/> open when this stream is disposed.
    /// </param>
    /// <param name="bufferSize">Size in bytes of the internal input and output buffers.</param>
    public XZSeekableDecompressStream(Stream innerStream, XZFileInfo fileInfo, bool leaveOpen = false, int bufferSize = 81920)
        : this(innerStream, fileInfo, ownsFileInfo: false, leaveOpen, bufferSize)
    {
    }

    private XZSeekableDecompressStream(Stream innerStream, XZFileInfo fileInfo, bool ownsFileInfo, bool leaveOpen, int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(innerStream);
        ArgumentNullException.ThrowIfNull(fileInfo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bufferSize, 0);

        if (!innerStream.CanRead || !innerStream.CanSeek)
        {
            throw new ArgumentException(
                "The inner stream must be readable and seekable.", nameof(innerStream));
        }

        _innerStream = innerStream;
        _fileInfo = fileInfo;
        _ownsFileInfo = ownsFileInfo;
        _leaveOpen = leaveOpen;

        _inputBuffer = new byte[bufferSize];
        _outputBuffer = new byte[bufferSize];
        _inputHandle = GCHandle.Alloc(_inputBuffer, GCHandleType.Pinned);
        _outputHandle = GCHandle.Alloc(_outputBuffer, GCHandleType.Pinned);
    }

    private static XZFileInfo ReadFileInfo(Stream innerStream)
    {
        ArgumentNullException.ThrowIfNull(innerStream);
        return XZFileInfo.Read(innerStream);
    }

    /// <summary>Gets the index this stream was opened with.</summary>
    public XZFileInfo FileInfo => _fileInfo;

    /// <inheritdoc />
    public override bool CanRead => !_disposed;

    /// <inheritdoc />
    public override bool CanSeek => !_disposed;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <summary>Gets the total uncompressed length, taken from the index.</summary>
    public override long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _fileInfo.UncompressedSize;
        }
    }

    /// <summary>Gets or sets the uncompressed position.</summary>
    public override long Position
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _position;
        }
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>
    /// Moves to an uncompressed offset.
    /// </summary>
    /// <remarks>
    /// The target block is located through the index, so this does not scan the file.
    /// Decoding then restarts at that block's first byte and runs forward to the requested
    /// offset, so the work is proportional to the block size, not the file size.
    /// </remarks>
    /// <param name="offset">The offset relative to <paramref name="origin"/>.</param>
    /// <param name="origin">The reference point.</param>
    /// <returns>The new position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The resulting position is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="origin"/> is not a valid value.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentException($"Invalid seek origin: {origin}.", nameof(origin)),
        };

        ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(offset));

        if (target != _position)
        {
            // Decoders cannot rewind, so drop the current block and let the next read
            // reopen whichever block now holds the position.
            CloseBlock();
            _position = target;
        }

        return _position;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("The buffer is too small for the requested range.");
        }

        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int total = 0;

        while (!buffer.IsEmpty && _position < Length)
        {
            if (_decodedCount == 0 && !FillDecoded())
            {
                break;
            }

            int toCopy = Math.Min(_decodedCount, buffer.Length);
            _outputBuffer.AsSpan(_decodedOffset, toCopy).CopyTo(buffer);

            _decodedOffset += toCopy;
            _decodedCount -= toCopy;
            _position += toCopy;
            buffer = buffer[toCopy..];
            total += toCopy;
        }

        // Reaching the end of the data exits the loop above without another FillDecoded
        // call, so the final block would otherwise never have its check verified.
        if (_position >= Length)
        {
            FinishBlockIfConsumed();
        }

        return total;
    }

    /// <summary>
    /// Decodes more data for the current position, opening the containing block if needed.
    /// </summary>
    /// <returns><c>false</c> at end of data.</returns>
    private bool FillDecoded()
    {
        while (true)
        {
            if (!_blockOpen || _position >= _blockEnd)
            {
                // Reaching the end of a block's data does not by itself verify its integrity
                // check: the check bytes sit after the compressed data, and the decoder only
                // validates them on the way to LZMA_STREAM_END.
                FinishBlockIfConsumed();

                if (_position >= Length || !OpenBlockAt(_position))
                {
                    return false;
                }

                // Opening a block skips forward to the requested offset and usually leaves
                // the bytes that follow it already decoded. Decoding again here would
                // overwrite them and silently drop data.
                if (_decodedCount > 0)
                {
                    return true;
                }
            }

            int decoded = DecodeFromBlock();
            if (decoded > 0)
            {
                return true;
            }

            if (!_blockFinished)
            {
                // The block produced nothing and is not finished: the file is truncated.
                throw new XZException(LZMA_DATA_ERROR,
                    "Unexpected end of block data while decoding.");
            }

            if (_position >= Length)
            {
                return false;
            }

            // Block exhausted; loop round to open the next one.
            CloseBlock();
        }
    }

    /// <summary>
    /// Runs the block decoder until it produces output or reaches the end of the block.
    /// </summary>
    /// <returns>The number of bytes placed in the output buffer.</returns>
    private int DecodeFromBlock()
    {
        // Decoding writes from the start of the output buffer, so never do it while bytes
        // from a previous pass are still unconsumed.
        if (_decodedCount > 0)
        {
            return _decodedCount;
        }

        while (true)
        {
            if (_blockFinished)
            {
                return 0;
            }

            if (_lzmaStream.avail_in == UIntPtr.Zero)
            {
                int read = _innerStream.Read(_inputBuffer, 0, _inputBuffer.Length);
                if (read == 0)
                {
                    return 0;
                }

                _lzmaStream.next_in = _inputHandle.AddrOfPinnedObject();
                _lzmaStream.avail_in = (UIntPtr)read;
            }

            _lzmaStream.next_out = _outputHandle.AddrOfPinnedObject();
            _lzmaStream.avail_out = (UIntPtr)_outputBuffer.Length;

            int ret = lzma_code(ref _lzmaStream, LZMA_RUN);

            if (ret != LZMA_OK && ret != LZMA_STREAM_END)
            {
                throw new XZException(ret);
            }

            int produced = _outputBuffer.Length - (int)(ulong)_lzmaStream.avail_out;

            if (ret == LZMA_STREAM_END)
            {
                _blockFinished = true;
            }

            if (produced > 0)
            {
                _decodedOffset = 0;
                _decodedCount = produced;
                return produced;
            }

            if (_blockFinished)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Locates the block containing an uncompressed offset, starts a decoder on it, and
    /// discards the bytes before that offset.
    /// </summary>
    /// <param name="target">The uncompressed offset to position at.</param>
    /// <returns><c>false</c> when the offset is at or past the end of the data.</returns>
    private bool OpenBlockAt(long target)
    {
        CloseBlock();

        var iter = default(LzmaIndexIter);
        lzma_index_iter_init(ref iter, _fileInfo.IndexHandle);

        // liblzma inverts the usual sense: true means "not found", i.e. past the end.
        if (lzma_index_iter_locate(ref iter, (ulong)target))
        {
            return false;
        }

        _blockStart = checked((long)iter.block_uncompressed_file_offset);
        _blockEnd = _blockStart + checked((long)iter.block_uncompressed_size);

        int check = iter.stream_flags is null ? LZMA_CHECK_NONE : iter.stream_flags->check;

        _innerStream.Seek(checked((long)iter.block_compressed_file_offset), SeekOrigin.Begin);

        StartBlockDecoder(check);

        // The decoder can only start at the block boundary, so drop everything before
        // the requested offset.
        SkipWithinBlock(target - _blockStart);

        return true;
    }

    /// <summary>
    /// Reads and decodes the block header at the inner stream's current position and
    /// initializes a block decoder for it.
    /// </summary>
    /// <param name="check">The integrity check type from the stream header.</param>
    private void StartBlockDecoder(int check)
    {
        int first = _innerStream.ReadByte();
        if (first <= 0)
        {
            // 0 indicates the index indicator rather than a block header.
            throw new XZException(LZMA_DATA_ERROR, "Expected an .xz block header.");
        }

        uint headerSize = BlockHeaderSizeDecode((byte)first);
        var header = new byte[headerSize];
        header[0] = (byte)first;
        _innerStream.ReadExactly(header, 1, (int)headerSize - 1);

        _blockFilters = (LzmaFilter*)NativeMemory.AllocZeroed(
            (nuint)((LZMA_FILTERS_MAX + 1) * sizeof(LzmaFilter)));

        _block = default;
        _block.version = 0;
        _block.header_size = headerSize;
        _block.check = check;
        _block.filters = _blockFilters;

        int ret;
        fixed (byte* p = header)
        {
            ret = lzma_block_header_decode(ref _block, LibLzmaAllocator.Handle, p);
        }

        if (ret != LZMA_OK)
        {
            ReleaseBlockFilters();
            throw new XZException(ret);
        }

        _lzmaStream = new LzmaStream();
        ret = lzma_block_decoder(ref _lzmaStream, ref _block);
        if (ret != LZMA_OK)
        {
            ReleaseBlockFilters();
            throw new XZException(ret);
        }

        _blockOpen = true;
        _blockFinished = false;
        _decodedOffset = 0;
        _decodedCount = 0;
    }

    /// <summary>
    /// Decodes and discards <paramref name="count"/> bytes from the start of the open block.
    /// </summary>
    private void SkipWithinBlock(long count)
    {
        while (count > 0)
        {
            if (_decodedCount == 0 && DecodeFromBlock() == 0)
            {
                throw new XZException(LZMA_DATA_ERROR,
                    "Unexpected end of block data while seeking.");
            }

            int drop = (int)Math.Min(count, _decodedCount);
            _decodedOffset += drop;
            _decodedCount -= drop;
            count -= drop;
        }
    }

    /// <summary>
    /// Drives the open block to completion when its data has been fully read, so liblzma
    /// consumes and verifies the trailing integrity check.
    /// </summary>
    /// <remarks>
    /// This only applies when a block was read through to its end. Seeking away from the
    /// middle of a block leaves nothing to verify, so random access does not validate the
    /// check for the blocks it skips over.
    /// </remarks>
    private void FinishBlockIfConsumed()
    {
        if (!_blockOpen || _blockFinished || _decodedCount > 0 || _position < _blockEnd)
        {
            return;
        }

        while (!_blockFinished)
        {
            if (_lzmaStream.avail_in == UIntPtr.Zero)
            {
                int read = _innerStream.Read(_inputBuffer, 0, _inputBuffer.Length);
                if (read == 0)
                {
                    throw new XZException(LZMA_DATA_ERROR,
                        "Unexpected end of file while verifying a block integrity check.");
                }

                _lzmaStream.next_in = _inputHandle.AddrOfPinnedObject();
                _lzmaStream.avail_in = (UIntPtr)read;
            }

            _lzmaStream.next_out = _outputHandle.AddrOfPinnedObject();
            _lzmaStream.avail_out = (UIntPtr)_outputBuffer.Length;

            int ret = lzma_code(ref _lzmaStream, LZMA_RUN);

            if (ret == LZMA_STREAM_END)
            {
                _blockFinished = true;
                return;
            }

            if (ret != LZMA_OK)
            {
                throw new XZException(ret);
            }

            // The block is already fully consumed, so nothing more should come out.
            if (_outputBuffer.Length - (int)(ulong)_lzmaStream.avail_out > 0)
            {
                throw new XZException(LZMA_DATA_ERROR,
                    "Block produced more data than its index entry declared.");
            }
        }
    }

    private void CloseBlock()
    {
        if (_blockOpen)
        {
            lzma_end(ref _lzmaStream);
            ReleaseBlockFilters();
            _blockOpen = false;
        }

        _blockFinished = false;
        _decodedOffset = 0;
        _decodedCount = 0;
        _blockStart = 0;
        _blockEnd = 0;
    }

    private void ReleaseBlockFilters()
    {
        if (_blockFilters is not null)
        {
            lzma_filters_free(_blockFilters, LibLzmaAllocator.Handle);
            NativeMemory.Free(_blockFilters);
            _blockFilters = null;
        }
    }

    /// <summary>This stream is read-only; flushing is a no-op.</summary>
    public override void Flush() { }

    /// <summary>Not supported. This is a read-only stream.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    /// <summary>Not supported. This is a read-only stream.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void SetLength(long value)
        => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;
        CloseBlock();

        if (_inputHandle.IsAllocated)
        {
            _inputHandle.Free();
        }

        if (_outputHandle.IsAllocated)
        {
            _outputHandle.Free();
        }

        if (disposing)
        {
            GC.SuppressFinalize(this);

            if (_ownsFileInfo)
            {
                _fileInfo.Dispose();
            }

            if (!_leaveOpen)
            {
                _innerStream.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Releases native resources if <see cref="IDisposable.Dispose"/> was not called.
    /// </summary>
    ~XZSeekableDecompressStream()
    {
        Dispose(false);
    }
}
