using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using static ZCS.XZ.LibLzmaNativeMethods;

namespace ZCS.XZ;

/// <summary>
/// A write-only stream that compresses data written to it in the
/// <see href="https://tukaani.org/xz/xz-file-format.txt">.xz file format</see>
/// and writes the compressed output to an underlying stream.
/// </summary>
/// <remarks>
/// <para>
/// This stream wraps the liblzma encoder and supports both single-threaded
/// (<see cref="lzma_easy_encoder"/>) and multithreaded (<see cref="lzma_stream_encoder_mt"/>)
/// compression. The compression level and threading are controlled via <see cref="XZCompressOptions"/>.
/// </para>
/// <para>
/// The caller's input data is passed directly to liblzma using zero-copy pinning
/// (via <c>fixed</c>), avoiding intermediate buffer copies on the write path.
/// </para>
/// <para>
/// The stream must be disposed to finalize the .xz output. Failing to dispose
/// will produce an incomplete (corrupt) .xz stream. A finalizer is provided
/// as a safety net to release native resources, but it cannot finalize the
/// .xz stream footer.
/// </para>
/// <example>
/// <code>
/// using var output = File.Create("data.xz");
/// using (var xz = new XZCompressStream(output, new XZCompressOptions { Level = XZCompressionLevel.Default }))
/// {
///     xz.Write(data);
/// }
/// </code>
/// </example>
/// </remarks>
public sealed class XZCompressStream : Stream
{
    private readonly Stream _innerStream;
    private readonly bool _leaveOpen;
    private readonly byte[] _outputBuffer;
    private LzmaStream _lzmaStream;
    private GCHandle _outputHandle;
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="XZCompressStream"/> with default compression options.
    /// The inner stream will be disposed when this stream is disposed.
    /// </summary>
    /// <param name="innerStream">The writable stream to write compressed output to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not writable.</exception>
    public XZCompressStream(Stream innerStream)
        : this(innerStream, new XZCompressOptions(), leaveOpen: false)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="XZCompressStream"/> with the specified compression options.
    /// The inner stream will be disposed when this stream is disposed.
    /// </summary>
    /// <param name="innerStream">The writable stream to write compressed output to.</param>
    /// <param name="options">Compression options (level, threads, buffer size).</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> or <paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not writable.</exception>
    public XZCompressStream(Stream innerStream, XZCompressOptions options)
        : this(innerStream, options, leaveOpen: false)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="XZCompressStream"/> with the specified compression options
    /// and control over whether the inner stream is left open after disposal.
    /// </summary>
    /// <param name="innerStream">The writable stream to write compressed output to.</param>
    /// <param name="options">Compression options (level, threads, buffer size).</param>
    /// <param name="leaveOpen">
    /// <c>true</c> to leave <paramref name="innerStream"/> open after this stream is disposed;
    /// <c>false</c> to dispose it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="innerStream"/> or <paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="innerStream"/> is not writable.</exception>
    /// <exception cref="XZException">liblzma encoder initialization failed.</exception>
    public XZCompressStream(Stream innerStream, XZCompressOptions options, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(innerStream);
        ArgumentNullException.ThrowIfNull(options);

        if (!innerStream.CanWrite)
        {
            throw new ArgumentException("The inner stream must be writable.", nameof(innerStream));
        }

        _innerStream = innerStream;
        _leaveOpen = leaveOpen;
        _outputBuffer = new byte[options.BufferSize];
        _lzmaStream = new LzmaStream();

        int ret;
        int threadCount = options.GetThreadCount();
        uint preset = options.GetPreset();

        if (threadCount > 1)
        {
            var mt = new LzmaMt
            {
                threads = (uint)threadCount,
                preset = preset,
                check = LZMA_CHECK_CRC64,
            };
            ret = lzma_stream_encoder_mt(ref _lzmaStream, ref mt);
        }
        else
        {
            ret = lzma_easy_encoder(ref _lzmaStream, preset, LZMA_CHECK_CRC64);
        }

        ThrowIfLzmaInitError(ret);

        _outputHandle = GCHandle.Alloc(_outputBuffer, GCHandleType.Pinned);

        // Point output buffer
        _lzmaStream.next_out = _outputHandle.AddrOfPinnedObject();
        _lzmaStream.avail_out = (UIntPtr)_outputBuffer.Length;
    }

    /// <summary>
    /// Finalizer that ensures native resources (liblzma state, pinned buffers) are released
    /// if <see cref="Dispose()" /> was not called. The .xz stream will NOT be properly
    /// finalized by the finalizer — always dispose the stream explicitly.
    /// </summary>
    ~XZCompressStream()
    {
        Dispose(false);
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => !_disposed;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value)
        => throw new NotSupportedException();

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> or <paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">The sum of <paramref name="offset"/> and <paramref name="count"/> exceeds the buffer length.</exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("The sum of offset and count is greater than the buffer length.");
        }

        if (count == 0)
        {
            return;
        }

        WriteInternal(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WriteInternal(buffer);
    }

    /// <summary>
    /// Compresses the contents of <paramref name="buffer"/> using zero-copy pinning.
    /// The caller's span is pinned in place with <c>fixed</c> and its pointer is passed
    /// directly to liblzma, avoiding any intermediate buffer copies.
    /// </summary>
    /// <param name="buffer">The data to compress.</param>
    private unsafe void WriteInternal(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        fixed (byte* inputPtr = buffer)
        {
            _lzmaStream.next_in = (IntPtr)inputPtr;
            _lzmaStream.avail_in = (UIntPtr)buffer.Length;

            while ((ulong)_lzmaStream.avail_in > 0)
            {
                _lzmaStream.next_out = _outputHandle.AddrOfPinnedObject();
                _lzmaStream.avail_out = (UIntPtr)_outputBuffer.Length;

                int ret = lzma_code(ref _lzmaStream, LZMA_RUN);
                ThrowIfLzmaError(ret);

                int written = _outputBuffer.Length - (int)(ulong)_lzmaStream.avail_out;
                if (written > 0)
                {
                    _innerStream.Write(_outputBuffer, 0, written);
                }
            }
        }
    }

    /// <summary>
    /// Flushes any buffered compressed data to the underlying stream using <c>LZMA_SYNC_FLUSH</c>.
    /// This ensures all input written so far is available in the output, at the cost of a
    /// slightly reduced compression ratio.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FlushEncoder(LZMA_SYNC_FLUSH);
        _innerStream.Flush();
    }

    /// <summary>
    /// Signals the end of input to the encoder and writes the .xz stream footer.
    /// Called automatically during <see cref="Dispose(bool)"/>.
    /// </summary>
    private void Finish()
    {
        FlushEncoder(LZMA_FINISH);
    }

    /// <summary>
    /// Drains all pending output from the encoder for the given action.
    /// For <see cref="LZMA_SYNC_FLUSH"/>, flushes buffered data.
    /// For <see cref="LZMA_FINISH"/>, writes the stream footer and loops until <see cref="LZMA_STREAM_END"/>.
    /// </summary>
    /// <param name="action">The lzma_action to pass to <see cref="lzma_code"/>.</param>
    private void FlushEncoder(int action)
    {
        _lzmaStream.next_in = IntPtr.Zero;
        _lzmaStream.avail_in = UIntPtr.Zero;

        int ret;
        do
        {
            _lzmaStream.next_out = _outputHandle.AddrOfPinnedObject();
            _lzmaStream.avail_out = (UIntPtr)_outputBuffer.Length;

            ret = lzma_code(ref _lzmaStream, action);
            ThrowIfLzmaError(ret, allowStreamEnd: true);

            int written = _outputBuffer.Length - (int)(ulong)_lzmaStream.avail_out;
            if (written > 0)
            {
                _innerStream.Write(_outputBuffer, 0, written);
            }

        } while (ret != LZMA_STREAM_END && action == LZMA_FINISH
              || (ulong)_lzmaStream.avail_out == 0);
    }

    /// <summary>
    /// Releases all resources used by this stream. When <paramref name="disposing"/> is <c>true</c>,
    /// finalizes the .xz stream (writes footer), frees native resources, and optionally
    /// disposes the inner stream. When <c>false</c> (finalizer path), only native resources
    /// are released.
    /// </summary>
    /// <param name="disposing"><c>true</c> if called from <see cref="IDisposable.Dispose"/>; <c>false</c> if called from the finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        try
        {
            if (disposing)
            {
                Finish();
            }
        }
        finally
        {
            _disposed = true;
            CleanupNativeResources();

            if (disposing)
            {
                GC.SuppressFinalize(this);
                DisposeManagedResources();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Throws an <see cref="XZException"/> if the liblzma return code indicates an initialization failure.
    /// </summary>
    [ExcludeFromCodeCoverage]
    private static void ThrowIfLzmaInitError(int ret)
    {
        if (ret != LZMA_OK)
        {
            throw new XZException(ret);
        }
    }

    /// <summary>
    /// Throws an <see cref="XZException"/> if the liblzma return code indicates an error.
    /// </summary>
    /// <param name="ret">The return code from <see cref="lzma_code"/>.</param>
    /// <param name="allowStreamEnd">If <c>true</c>, <see cref="LZMA_STREAM_END"/> is treated as success.</param>
    [ExcludeFromCodeCoverage]
    private static void ThrowIfLzmaError(int ret, bool allowStreamEnd = false)
    {
        if (ret == LZMA_OK)
        {
            return;
        }

        if (allowStreamEnd && ret == LZMA_STREAM_END)
        {
            return;
        }
        throw new XZException(ret);
    }

    /// <summary>
    /// Frees native liblzma state and unpins the GCHandle for the output buffer.
    /// Safe to call multiple times.
    /// </summary>
    [ExcludeFromCodeCoverage]
    private void CleanupNativeResources()
    {
        lzma_end(ref _lzmaStream);

        if (_outputHandle.IsAllocated)
        {
            _outputHandle.Free();
        }
    }

    /// <summary>
    /// Disposes the inner stream if <see cref="_leaveOpen"/> is <c>false</c>.
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
