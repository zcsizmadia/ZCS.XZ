using System.IO;

namespace ZCS.XZ;

public class XZDecompressStream : Stream
{
    private NativeMethods.LzmaStream _lzmaStream;
    private Stream _innerStream;
    private byte[] _buffer = new byte[65536];

    public XZDecompressStream(Stream stream)
    {
        _innerStream = stream;
        _lzmaStream = new NativeMethods.LzmaStream();

        // Decompression (lzma_stream_decoder is generally single-threaded)
        NativeMethods.lzma_stream_decoder(ref _lzmaStream, ulong.MaxValue, 0);
    }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => 0; // Implement Read for decompression

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
            NativeMethods.lzma_end(ref _lzmaStream);
        }
        base.Dispose(disposing);
    }

    // Boilerplate Stream overrides omitted for brevity...
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => 0;
    public override long Position { get; set; }
    public override void Flush() => _innerStream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}