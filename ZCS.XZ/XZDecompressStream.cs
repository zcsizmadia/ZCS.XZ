using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using static ZCS.XZ.LibLzmaNativeMethods;

namespace ZCS.XZ;

/// <summary>
/// A read-only stream that decompresses data in the
/// <see href="https://tukaani.org/xz/xz-file-format.txt">.xz file format</see>
/// (and legacy .lzma format) from an underlying stream.
/// </summary>
/// <remarks>
/// <para>
/// This stream wraps the liblzma auto-decoder, which automatically detects
/// whether the input is in .xz or legacy .lzma format. The <c>LZMA_CONCATENATED</c>
/// flag is enabled to support concatenated .xz streams and to validate trailing data.
/// </para>
/// <para>
/// Decoded output is buffered internally. When the caller's read buffer is smaller
/// than the decoded output from a single <see cref="lzma_code"/> call, excess bytes
/// are retained and served from the buffer on subsequent reads.
/// </para>
/// <para>
/// A finalizer is provided as a safety net to release native resources if
/// <see cref="IDisposable.Dispose"/> is not called.
/// </para>
/// <example>
/// <code>
/// using var input = File.OpenRead("data.xz");
/// using var xz = new XZDecompressStream(input);
/// using var output = new MemoryStream();
/// xz.CopyTo(output);
/// byte[] decompressed = output.ToArray();
/// </code>
/// </example>
/// </remarks>
public sealed class XZDecompressStream : Stream
{
    /// <summary>
    /// Default memory limit for the decoder (no limit).
    /// </summary>
    private const ulong DefaultMemoryLimit = ulong.MaxValue;

    private readonly Stream _innerStream;
    private readonly bool _leaveOpen;
    private readonly byte[] _inputBuffer;
    private readonly byte[] _outputBuffer;
    private LzmaStream _lzmaStream;
    private GCHandle _inputHandle;
    private GCHandle _outputHandle;
    private bool _disposed;
    private bool _endOfStream;

    /// <summary>Offset into <see cref="_outputBuffer"/> where unconsumed decoded bytes start.</summary>
    private int _decodedOffset;

    /// <summary>Number of unconsumed decoded bytes remaining in <see cref="_outputBuffer"/>.</summary>
    private int _decodedCount;

    /// <summary>
    /// Initializes a new <see cref="XZDecompressStream"/> with default settings.
    /// The inner stream will be disposed when this stream is disposed.
    /// </summary>
    /// <param name="innerStream">The readable stream containing .xz or .lzma compressed data.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not readable.</exception>
    public XZDecompressStream(Stream innerStream)
        : this(innerStream, DefaultMemoryLimit, leaveOpen: false)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="XZDecompressStream"/> with control over inner stream disposal.
    /// </summary>
    /// <param name="innerStream">The readable stream containing .xz or .lzma compressed data.</param>
    /// <param name="leaveOpen">
    /// <c>true</c> to leave <paramref name="innerStream"/> open after this stream is disposed;
    /// <c>false</c> to dispose it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not readable.</exception>
    public XZDecompressStream(Stream innerStream, bool leaveOpen)
        : this(innerStream, DefaultMemoryLimit, leaveOpen)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="XZDecompressStream"/> with a memory limit for the decoder.
    /// </summary>
    /// <param name="innerStream">The readable stream containing .xz or .lzma compressed data.</param>
    /// <param name="memoryLimit">
    /// Maximum memory (in bytes) the decoder is allowed to use.
    /// Use <see cref="ulong.MaxValue"/> for no limit.
    /// </param>
    /// <param name="leaveOpen">
    /// <c>true</c> to leave <paramref name="innerStream"/> open after this stream is disposed;
    /// <c>false</c> to dispose it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not readable.</exception>
    /// <exception cref="XZException">liblzma decoder initialization failed.</exception>
    public XZDecompressStream(Stream innerStream, ulong memoryLimit, bool leaveOpen)
        : this(innerStream, memoryLimit, bufferSize: 81920, leaveOpen)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="XZDecompressStream"/> with a custom internal buffer size.
    /// </summary>
    /// <param name="innerStream">The readable stream containing .xz or .lzma compressed data.</param>
    /// <param name="bufferSize">Size (in bytes) of the internal input and output buffers.</param>
    /// <param name="leaveOpen">
    /// <c>true</c> to leave <paramref name="innerStream"/> open after this stream is disposed;
    /// <c>false</c> to dispose it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bufferSize"/> is zero or negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not readable.</exception>
    public XZDecompressStream(Stream innerStream, int bufferSize, bool leaveOpen)
        : this(innerStream, DefaultMemoryLimit, bufferSize, leaveOpen)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="XZDecompressStream"/> with full control over all parameters.
    /// </summary>
    /// <param name="innerStream">The readable stream containing .xz or .lzma compressed data.</param>
    /// <param name="memoryLimit">
    /// Maximum memory (in bytes) the decoder is allowed to use.
    /// Use <see cref="ulong.MaxValue"/> for no limit.
    /// </param>
    /// <param name="bufferSize">Size (in bytes) of the internal input and output buffers.</param>
    /// <param name="leaveOpen">
    /// <c>true</c> to leave <paramref name="innerStream"/> open after this stream is disposed;
    /// <c>false</c> to dispose it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bufferSize"/> is zero or negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not readable.</exception>
    /// <exception cref="XZException">liblzma decoder initialization failed.</exception>
    public XZDecompressStream(Stream innerStream, ulong memoryLimit, int bufferSize, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(innerStream);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bufferSize, 0);

        if (!innerStream.CanRead)
        {
            throw new ArgumentException("The inner stream must be readable.", nameof(innerStream));
        }

        _innerStream = innerStream;
        _leaveOpen = leaveOpen;
        _inputBuffer = new byte[bufferSize];
        _outputBuffer = new byte[bufferSize];
        _lzmaStream = new LzmaStream();

        // lzma_auto_decoder handles both .xz and legacy .lzma streams
        // LZMA_CONCATENATED enables decoding of concatenated .xz/.lz files
        // and requires LZMA_FINISH to get LZMA_STREAM_END, ensuring trailing data is validated
        int ret = lzma_auto_decoder(ref _lzmaStream, memoryLimit, LZMA_CONCATENATED);
        ThrowIfLzmaInitError(ret);

        _inputHandle = GCHandle.Alloc(_inputBuffer, GCHandleType.Pinned);
        _outputHandle = GCHandle.Alloc(_outputBuffer, GCHandleType.Pinned);
    }

    /// <summary>
    /// Releases native liblzma resources if <see cref="IDisposable.Dispose"/> was not called.
    /// </summary>
    ~XZDecompressStream()
    {
        Dispose(false);
    }

    /// <summary>Gets a value indicating whether the stream supports reading. Returns <c>true</c> until the stream is disposed.</summary>
    public override bool CanRead => !_disposed;

    /// <summary>Gets a value indicating whether the stream supports seeking. Always returns <c>false</c>.</summary>
    public override bool CanSeek => false;

    /// <summary>Gets a value indicating whether the stream supports writing. Always returns <c>false</c>.</summary>
    public override bool CanWrite => false;

    /// <summary>Not supported. Always throws <see cref="NotSupportedException"/>.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override long Length => throw new NotSupportedException();

    /// <summary>Not supported. Always throws <see cref="NotSupportedException"/>.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Not supported. This is a read-only stream. Always throws <see cref="NotSupportedException"/>.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    /// <summary>Not supported. This stream does not support seeking. Always throws <see cref="NotSupportedException"/>.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    /// <summary>Not supported. This stream does not support setting the length. Always throws <see cref="NotSupportedException"/>.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void SetLength(long value)
        => throw new NotSupportedException();

    /// <summary>
    /// This method is a no-op for the decompression stream because decoded output
    /// is written to the caller's buffer, not to an internal buffer that requires flushing.
    /// </summary>
    public override void Flush() { }

    /// <summary>
    /// Reads and decompresses a sequence of bytes from the underlying stream.
    /// </summary>
    /// <param name="buffer">The buffer to write decompressed data into.</param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>
    /// The number of bytes actually decompressed and written to <paramref name="buffer"/>.
    /// Returns 0 when the end of the compressed stream has been reached.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> or <paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">The sum of <paramref name="offset"/> and <paramref name="count"/> exceeds the buffer length.</exception>
    /// <exception cref="XZException">liblzma reported a decompression error.</exception>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("The sum of offset and count is greater than the buffer length.");
        }

        return ReadInternal(buffer.AsSpan(offset, count));
    }

    /// <summary>
    /// Reads and decompresses a sequence of bytes from the underlying stream into a <see cref="Span{T}"/>.
    /// </summary>
    /// <param name="buffer">The span to write decompressed data into.</param>
    /// <returns>
    /// The number of bytes actually decompressed and written to <paramref name="buffer"/>.
    /// Returns 0 when the end of the compressed stream has been reached.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="XZException">liblzma reported a decompression error.</exception>
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ReadInternal(buffer);
    }

    /// <summary>
    /// Core decompression loop shared by both <see cref="Read(byte[], int, int)"/> and
    /// <see cref="Read(Span{byte})"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method first drains any previously decoded bytes that were buffered because
    /// the caller's buffer was too small to accept the full decoder output. Then it
    /// enters a loop that reads compressed data from the inner stream, feeds it to
    /// <see cref="lzma_code"/>, and copies decoded output to the caller's buffer.
    /// </para>
    /// <para>
    /// When the inner stream returns 0 bytes (EOF), the action switches from
    /// <see cref="LZMA_RUN"/> to <see cref="LZMA_FINISH"/> so the decoder can
    /// finalize and validate the stream footer.
    /// </para>
    /// </remarks>
    /// <param name="buffer">The destination span to fill with decompressed bytes.</param>
    /// <returns>The total number of decompressed bytes written to <paramref name="buffer"/>.</returns>
    private int ReadInternal(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        int totalRead = 0;

        // First, drain any already-decoded bytes still in the output buffer
        // from a previous lzma_code call whose output exceeded the caller's buffer.
        if (_decodedCount > 0)
        {
            int toCopy = Math.Min(_decodedCount, buffer.Length);
            _outputBuffer.AsSpan(_decodedOffset, toCopy).CopyTo(buffer);
            _decodedOffset += toCopy;
            _decodedCount -= toCopy;
            totalRead += toCopy;

            if (totalRead == buffer.Length)
            {
                return totalRead;
            }

            buffer = buffer.Slice(toCopy);
        }

        while (buffer.Length > 0 && !_endOfStream)
        {
            // If the decoder consumed all input, read more from the inner stream
            if ((ulong)_lzmaStream.avail_in == 0)
            {
                int bytesRead = _innerStream.Read(_inputBuffer, 0, _inputBuffer.Length);
                if (bytesRead == 0)
                {
                    // No more input — signal finish
                    _lzmaStream.next_in = _inputHandle.AddrOfPinnedObject();
                    _lzmaStream.avail_in = UIntPtr.Zero;
                }
                else
                {
                    _lzmaStream.next_in = _inputHandle.AddrOfPinnedObject();
                    _lzmaStream.avail_in = (UIntPtr)bytesRead;
                }
            }

            _lzmaStream.next_out = _outputHandle.AddrOfPinnedObject();
            _lzmaStream.avail_out = (UIntPtr)_outputBuffer.Length;

            int action = ((ulong)_lzmaStream.avail_in == 0) ? LZMA_FINISH : LZMA_RUN;
            int ret = lzma_code(ref _lzmaStream, action);
            ThrowIfLzmaError(ret);

            int decoded = _outputBuffer.Length - (int)(ulong)_lzmaStream.avail_out;
            if (decoded > 0)
            {
                int toCopy = Math.Min(decoded, buffer.Length);
                _outputBuffer.AsSpan(0, toCopy).CopyTo(buffer);
                totalRead += toCopy;
                buffer = buffer.Slice(toCopy);

                // Buffer remaining decoded bytes
                if (toCopy < decoded)
                {
                    _decodedOffset = toCopy;
                    _decodedCount = decoded - toCopy;
                }
            }

            if (ret == LZMA_STREAM_END)
            {
                _endOfStream = true;
                break;
            }
        }

        return totalRead;
    }

    /// <summary>
    /// Releases native liblzma resources and, when <paramref name="disposing"/> is <c>true</c>,
    /// also releases managed resources (optionally closing the inner stream).
    /// </summary>
    /// <param name="disposing">
    /// <c>true</c> when called from <see cref="IDisposable.Dispose"/>;
    /// <c>false</c> when called from the finalizer.
    /// </param>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;
        CleanupNativeResources();

        if (disposing)
        {
            GC.SuppressFinalize(this);
            DisposeManagedResources();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Throws an <see cref="XZException"/> if the liblzma initialization return code
    /// indicates an error (anything other than <see cref="LZMA_OK"/>).
    /// </summary>
    /// <param name="ret">The return code from a liblzma initialization function.</param>
    /// <exception cref="XZException"><paramref name="ret"/> is not <see cref="LZMA_OK"/>.</exception>
    [ExcludeFromCodeCoverage]
    private static void ThrowIfLzmaInitError(int ret)
    {
        if (ret != LZMA_OK)
        {
            throw new XZException(ret);
        }
    }

    /// <summary>
    /// Throws an <see cref="XZException"/> if the liblzma coding return code indicates
    /// an error. Both <see cref="LZMA_OK"/> and <see cref="LZMA_STREAM_END"/> are
    /// treated as non-error conditions.
    /// </summary>
    /// <param name="ret">The return code from <see cref="lzma_code"/>.</param>
    /// <exception cref="XZException"><paramref name="ret"/> is neither <see cref="LZMA_OK"/> nor <see cref="LZMA_STREAM_END"/>.</exception>
    [ExcludeFromCodeCoverage]
    private static void ThrowIfLzmaError(int ret)
    {
        if (ret != LZMA_OK && ret != LZMA_STREAM_END)
        {
            throw new XZException(ret);
        }
    }

    /// <summary>
    /// Releases all native resources: calls <see cref="lzma_end"/> to free the decoder state
    /// and unpins the GCHandle-pinned input and output buffers.
    /// </summary>
    [ExcludeFromCodeCoverage]
    private void CleanupNativeResources()
    {
        lzma_end(ref _lzmaStream);

        if (_inputHandle.IsAllocated)
        {
            _inputHandle.Free();
        }

        if (_outputHandle.IsAllocated)
        {
            _outputHandle.Free();
        }
    }

    /// <summary>
    /// Disposes managed resources. If <see cref="_leaveOpen"/> is <c>false</c>,
    /// the inner stream is disposed.
    /// </summary>
    [ExcludeFromCodeCoverage]
    private void DisposeManagedResources()
    {
        if (!_leaveOpen)
        {
            _innerStream.Dispose();
        }
    }
}
