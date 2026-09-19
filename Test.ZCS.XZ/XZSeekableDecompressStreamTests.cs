namespace ZCS.XZ.Tests;

public class XZSeekableDecompressStreamTests
{
    private static byte[] MakePayload(int records) => XZFileInfoTests.MakePayload(records);

    private static byte[] Compress(byte[] data, XZCompressOptions? options = null)
        => XZFileInfoTests.Compress(data, options);

    /// <summary>
    /// Both a single-block file and a multi-block one, since seeking behaves differently:
    /// a multi-block file can jump to an interior block, a single-block file cannot.
    /// </summary>
    public static TheoryData<int> ThreadCounts() => new() { 1, 4 };

    [Theory]
    [MemberData(nameof(ThreadCounts))]
    public void Length_ComesFromTheIndex(int threads)
    {
        byte[] payload = MakePayload(100_000);
        byte[] compressed = Compress(payload, new XZCompressOptions
        {
            Level = XZCompressionLevel.Fastest,
            Threads = threads,
        });

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        Assert.True(xz.CanSeek);
        Assert.True(xz.CanRead);
        Assert.False(xz.CanWrite);
        Assert.Equal(payload.Length, xz.Length);
        Assert.Equal(0, xz.Position);
    }

    [Theory]
    [MemberData(nameof(ThreadCounts))]
    public void SequentialRead_MatchesTheOriginal(int threads)
    {
        byte[] payload = MakePayload(100_000);
        byte[] compressed = Compress(payload, new XZCompressOptions
        {
            Level = XZCompressionLevel.Fastest,
            Threads = threads,
        });

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);
        using var ms = new MemoryStream();

        xz.CopyTo(ms);

        Assert.Equal(payload, ms.ToArray());
        Assert.Equal(payload.Length, xz.Position);
    }

    [Theory]
    [MemberData(nameof(ThreadCounts))]
    public void Seek_ReturnsTheCorrectBytesAtEveryOffset(int threads)
    {
        byte[] payload = MakePayload(400_000);
        byte[] compressed = Compress(payload, new XZCompressOptions
        {
            Level = XZCompressionLevel.Fastest,
            Threads = threads,
        });

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        long[] offsets =
        [
            0, 1, 16, 1024, 65_536,
            payload.Length / 4, payload.Length / 3, payload.Length / 2,
            payload.Length - 1024, payload.Length - 64, payload.Length - 1,
        ];

        var buffer = new byte[64];

        foreach (long offset in offsets)
        {
            Assert.Equal(offset, xz.Seek(offset, SeekOrigin.Begin));

            int want = (int)Math.Min(buffer.Length, payload.Length - offset);
            xz.ReadExactly(buffer, 0, want);

            Assert.True(
                buffer.AsSpan(0, want).SequenceEqual(payload.AsSpan((int)offset, want)),
                $"Data mismatch at offset {offset}");
            Assert.Equal(offset + want, xz.Position);
        }
    }

    [Fact]
    public void Seek_BackwardsRereadsCorrectly()
    {
        byte[] payload = MakePayload(200_000);
        byte[] compressed = Compress(payload, new XZCompressOptions { Threads = 4 });

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        var buffer = new byte[128];

        // Forward to near the end, then back to the very start.
        xz.Seek(payload.Length - 256, SeekOrigin.Begin);
        xz.ReadExactly(buffer, 0, 128);

        xz.Seek(0, SeekOrigin.Begin);
        xz.ReadExactly(buffer, 0, 128);

        Assert.True(buffer.AsSpan(0, 128).SequenceEqual(payload.AsSpan(0, 128)));
    }

    [Fact]
    public void Seek_HonoursAllOrigins()
    {
        byte[] payload = MakePayload(50_000);
        byte[] compressed = Compress(payload);

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        var buffer = new byte[32];

        Assert.Equal(payload.Length - 32, xz.Seek(-32, SeekOrigin.End));
        xz.ReadExactly(buffer, 0, 32);
        Assert.True(buffer.AsSpan().SequenceEqual(payload.AsSpan(payload.Length - 32, 32)));

        xz.Seek(1000, SeekOrigin.Begin);
        Assert.Equal(1500, xz.Seek(500, SeekOrigin.Current));
        xz.ReadExactly(buffer, 0, 32);
        Assert.True(buffer.AsSpan().SequenceEqual(payload.AsSpan(1500, 32)));
    }

    [Fact]
    public void Position_SetterSeeks()
    {
        byte[] payload = MakePayload(10_000);
        byte[] compressed = Compress(payload);

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        xz.Position = 4096;
        var buffer = new byte[16];
        xz.ReadExactly(buffer, 0, 16);

        Assert.True(buffer.AsSpan().SequenceEqual(payload.AsSpan(4096, 16)));
    }

    [Fact]
    public void Read_AtEndOfStream_ReturnsZero()
    {
        byte[] payload = MakePayload(1_000);
        using var src = new MemoryStream(Compress(payload));
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        xz.Seek(payload.Length, SeekOrigin.Begin);

        Assert.Equal(0, xz.Read(new byte[16], 0, 16));
    }

    [Fact]
    public void Read_PastEndOfStream_ReturnsZero()
    {
        byte[] payload = MakePayload(1_000);
        using var src = new MemoryStream(Compress(payload));
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        xz.Seek(payload.Length + 5_000, SeekOrigin.Begin);

        Assert.Equal(0, xz.Read(new byte[16], 0, 16));
    }

    [Fact]
    public void EmptyFile_HasZeroLengthAndReadsNothing()
    {
        using var src = new MemoryStream(Compress([]));
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        Assert.Equal(0, xz.Length);
        Assert.Equal(0, xz.Read(new byte[8], 0, 8));
    }

    [Fact]
    public void Seek_NegativePosition_Throws()
    {
        using var src = new MemoryStream(Compress(MakePayload(100)));
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => xz.Seek(-1, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_InvalidOrigin_Throws()
    {
        using var src = new MemoryStream(Compress(MakePayload(100)));
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        Assert.Throws<ArgumentException>(() => xz.Seek(0, (SeekOrigin)99));
    }

    [Fact]
    public void Write_And_SetLength_AreNotSupported()
    {
        using var src = new MemoryStream(Compress(MakePayload(100)));
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        Assert.Throws<NotSupportedException>(() => xz.Write(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => xz.SetLength(10));
    }

    [Fact]
    public void Constructor_NonSeekableInner_Throws()
    {
        using var inner = new MemoryStream(Compress(MakePayload(100)));
        using var wrapper = new ForwardOnlyStream(inner);

        Assert.Throws<ArgumentException>(() => new XZSeekableDecompressStream(wrapper));
    }

    [Fact]
    public void Constructor_WithSharedFileInfo_DoesNotDisposeIt()
    {
        byte[] payload = MakePayload(5_000);
        using var src = new MemoryStream(Compress(payload));
        using var info = XZFileInfo.Read(src);

        src.Position = 0;
        using (var xz = new XZSeekableDecompressStream(src, info, leaveOpen: true))
        {
            Assert.Equal(payload.Length, xz.Length);
            Assert.Same(info, xz.FileInfo);
        }

        // The caller still owns the index, so it must remain usable.
        Assert.Equal(payload.Length, info.UncompressedSize);
        using var again = new XZSeekableDecompressStream(src, info, leaveOpen: true);
        Assert.Equal(payload.Length, again.Length);
    }

    [Fact]
    public void Dispose_LeaveOpenFalse_ClosesInnerStream()
    {
        var src = new MemoryStream(Compress(MakePayload(100)));

        using (var xz = new XZSeekableDecompressStream(src))
        {
            Assert.True(xz.CanRead);
        }

        Assert.False(src.CanRead);
    }

    [Fact]
    public void Operations_AfterDispose_Throw()
    {
        var src = new MemoryStream(Compress(MakePayload(100)));
        var xz = new XZSeekableDecompressStream(src, leaveOpen: true);
        xz.Dispose();

        Assert.Throws<ObjectDisposedException>(() => xz.Length);
        Assert.Throws<ObjectDisposedException>(() => xz.Position);
        Assert.Throws<ObjectDisposedException>(() => xz.Seek(0, SeekOrigin.Begin));
        Assert.Throws<ObjectDisposedException>(() => xz.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void FilteredStream_IsSeekable()
    {
        // Seeking decodes a block header directly, so it must honour the block's own
        // filter chain rather than assuming plain LZMA2.
        byte[] payload = MakePayload(100_000);

        using var chain = XZFilterChain.Parse("delta:dist=4 lzma2:preset=1");
        byte[] compressed = Compress(payload, new XZCompressOptions { Filters = chain });

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);

        var buffer = new byte[64];
        xz.Seek(50_000, SeekOrigin.Begin);
        xz.ReadExactly(buffer, 0, 64);

        Assert.True(buffer.AsSpan().SequenceEqual(payload.AsSpan(50_000, 64)));
    }

    [Theory]
    [InlineData(LzmaCheck.None)]
    [InlineData(LzmaCheck.Crc32)]
    [InlineData(LzmaCheck.Sha256)]
    public void SeekWorksForEveryCheckType(LzmaCheck check)
    {
        // The block decoder is told the check type read from the stream header, which
        // determines how many check bytes follow the compressed data. Passing a larger
        // check than the file uses makes the decoder overrun into the next structure and
        // fail, which this catches. Passing a smaller one (for example always None) only
        // skips verification without changing the decoded bytes, so no test here detects
        // that; it is why the value is read from the stream flags rather than assumed.
        string fileName = check switch
        {
            LzmaCheck.None => "good-1-check-none.xz",
            LzmaCheck.Crc32 => "good-1-check-crc32.xz",
            _ => "good-1-check-sha256.xz",
        };

        byte[] compressed = File.ReadAllBytes(Path.Combine("files", fileName));

        byte[] expected;
        using (var plain = new MemoryStream(compressed))
        using (var dec = new XZDecompressStream(plain, leaveOpen: true))
        using (var ms = new MemoryStream())
        {
            dec.CopyTo(ms);
            expected = ms.ToArray();
        }

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);
        using var result = new MemoryStream();

        Assert.Equal(expected.Length, xz.Length);
        xz.CopyTo(result);
        Assert.Equal(expected, result.ToArray());
    }

    /// <summary>
    /// Corrupt input must not be returned as if it were valid.
    /// </summary>
    /// <remarks>
    /// This is caught by the LZMA2 decoder rather than by the block's integrity check —
    /// a flipped bit almost always makes the compressed stream itself invalid. It therefore
    /// does not pin the check type passed to the block decoder; see the note on
    /// <see cref="SeekWorksForEveryCheckType"/>.
    /// </remarks>
    [Fact]
    public void CorruptedData_Throws()
    {
        byte[] payload = MakePayload(20_000);
        byte[] compressed = Compress(payload);

        // Flip a bit inside the compressed payload, past the 12-byte stream header and
        // block header, leaving the index and footer intact so the seek still works.
        compressed[compressed.Length / 2] ^= 0x01;

        using var src = new MemoryStream(compressed);
        using var xz = new XZSeekableDecompressStream(src, leaveOpen: true);
        using var sink = new MemoryStream();

        Assert.Throws<XZException>(() => xz.CopyTo(sink));
    }

    /// <summary>
    /// Files whose compressed data is intact but whose stored integrity check is wrong.
    /// Nothing but the check itself can detect these, so they pin both that the correct
    /// check type is passed to the block decoder and that a fully-read block is driven to
    /// completion so liblzma actually verifies it.
    /// </summary>
    [Theory]
    [InlineData("bad-1-check-crc32.xz")]
    [InlineData("bad-1-check-crc64.xz")]
    [InlineData("bad-1-check-sha256.xz")]
    public void BadIntegrityCheck_IsDetected(string fileName)
    {
        using var fs = File.OpenRead(Path.Combine("files", fileName));
        using var xz = new XZSeekableDecompressStream(fs, leaveOpen: true);
        using var sink = new MemoryStream();

        Assert.Throws<XZException>(() => xz.CopyTo(sink));
    }

    /// <summary>
    /// The same corrupt-check files, read one byte at a time.
    /// </summary>
    /// <remarks>
    /// With a large buffer the decoder happens to run past the end of the data and validate
    /// the check on its own. With a one-byte buffer the last read satisfies the requested
    /// length exactly, and nothing would drive the decoder to LZMA_STREAM_END unless the
    /// stream explicitly finishes a fully-consumed block. This is the case that catches a
    /// silently unverified check on larger files.
    /// </remarks>
    [Theory]
    [InlineData("bad-1-check-crc32.xz")]
    [InlineData("bad-1-check-crc64.xz")]
    [InlineData("bad-1-check-sha256.xz")]
    public void BadIntegrityCheck_IsDetectedWhenDataEndsOnABufferBoundary(string fileName)
    {
        using var fs = File.OpenRead(Path.Combine("files", fileName));
        using var xz = new XZSeekableDecompressStream(fs, leaveOpen: true, bufferSize: 1);

        Assert.Throws<XZException>(() =>
        {
            var one = new byte[1];
            while (xz.Read(one, 0, 1) > 0)
            {
            }
        });
    }

    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
