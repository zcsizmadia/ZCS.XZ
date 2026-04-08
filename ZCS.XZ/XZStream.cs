using System.IO;

namespace ZCS.XZ;

public class XZStream : Stream
{
    private NativeMethods.LzmaStream _lzmaStream;
    private Stream _innerStream;
    private bool _isCompression;
    private byte[] _buffer = new byte[65536];

    public XZStream(Stream stream, bool compress, uint preset = 6, int threads = -1)
    {
        _innerStream = stream;
        _isCompression = compress;
        _lzmaStream = new NativeMethods.LzmaStream();

        if (compress)
        {
            // If threads is -1, use all available logical processors
            uint threadCount = threads <= 0 ? (uint)Environment.ProcessorCount : (uint)threads;
            
            var mtOptions = new NativeMethods.LzmaMt
            {
                threads = threadCount,
                preset = preset,
                check = 2, // LZMA_CHECK_CRC64
                block_size = 0,
                timeout = 0,
                flags = 0
            };

            int ret = NativeMethods.lzma_stream_encoder_mt(ref _lzmaStream, ref mtOptions);
            if (ret != 0) throw new Exception($"Failed to init MT encoder: {ret}");
        }
        else
        {
            // Decompression (lzma_stream_decoder is generally single-threaded)
            NativeMethods.lzma_stream_decoder(ref _lzmaStream, ulong.MaxValue, 0);
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_isCompression) throw new NotSupportedException();
        
        unsafe
        {
            fixed (byte* pIn = &buffer[offset])
            {
                _lzmaStream.next_in = (IntPtr)pIn;
                _lzmaStream.avail_in = (UIntPtr)count;

                while (_lzmaStream.avail_in != UIntPtr.Zero)
                {
                    ProcessAction(0); // LZMA_RUN
                }
            }
        }
    }

    private void ProcessAction(int action)
    {
        byte[] outBuf = new byte[65536];
        unsafe
        {
            fixed (byte* pOut = outBuf)
            {
                _lzmaStream.next_out = (IntPtr)pOut;
                _lzmaStream.avail_out = (UIntPtr)outBuf.Length;

                NativeMethods.lzma_code(ref _lzmaStream, action);

                int produced = outBuf.Length - (int)_lzmaStream.avail_out;
                if (produced > 0) _innerStream.Write(outBuf, 0, produced);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_isCompression) ProcessAction(4); // LZMA_FINISH
            NativeMethods.lzma_end(ref _lzmaStream);
        }
        base.Dispose(disposing);
    }

    // Boilerplate Stream overrides omitted for brevity...
    public override bool CanRead => !_isCompression;
    public override bool CanWrite => _isCompression;
    public override bool CanSeek => false;
    public override long Length => 0;
    public override long Position { get; set; }
    public override void Flush() => _innerStream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => 0; // Implement Read for decompression
}