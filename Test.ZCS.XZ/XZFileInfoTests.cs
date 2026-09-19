namespace ZCS.XZ.Tests;

public class XZFileInfoTests
{
    /// <summary>
    /// Deterministic payload where every 16-byte record encodes its own index, so any byte
    /// read at a given offset can be checked against the offset it came from.
    /// </summary>
    internal static byte[] MakePayload(int records)
    {
        var data = new byte[records * 16];
        for (int i = 0; i < records; i++)
        {
            BitConverter.GetBytes(i).CopyTo(data, i * 16);
            BitConverter.GetBytes(i * 2654435761u).CopyTo(data, i * 16 + 4);
            BitConverter.GetBytes((long)i * 6364136223846793005L).CopyTo(data, i * 16 + 8);
        }
        return data;
    }

    internal static byte[] Compress(byte[] data, XZCompressOptions? options = null)
    {
        using var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, options ?? new XZCompressOptions(), leaveOpen: true))
        {
            xz.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    [Fact]
    public void Read_ReportsUncompressedSizeWithoutDecompressing()
    {
        byte[] payload = MakePayload(20_000);
        byte[] compressed = Compress(payload);

        using var src = new MemoryStream(compressed);
        using var info = XZFileInfo.Read(src);

        Assert.Equal(payload.Length, info.UncompressedSize);
        Assert.Equal(compressed.Length, info.CompressedSize);
    }

    [Fact]
    public void Read_ReportsSingleBlockForSingleThreadedOutput()
    {
        byte[] compressed = Compress(MakePayload(20_000));

        using var src = new MemoryStream(compressed);
        using var info = XZFileInfo.Read(src);

        Assert.Equal(1, info.BlockCount);
        Assert.Equal(1, info.StreamCount);
    }

    [Fact]
    public void Read_ReportsMultipleBlocksForThreadedOutput()
    {
        // The multithreaded encoder splits output into several blocks, which is what makes
        // both parallel decoding and cheap seeking possible.
        byte[] compressed = Compress(MakePayload(400_000), new XZCompressOptions
        {
            Level = XZCompressionLevel.Fastest,
            Threads = 4,
        });

        using var src = new MemoryStream(compressed);
        using var info = XZFileInfo.Read(src);

        Assert.True(info.BlockCount > 1, $"Expected more than one block, got {info.BlockCount}");
        Assert.Equal(1, info.StreamCount);
    }

    [Fact]
    public void Read_ReportsTheCheckType()
    {
        byte[] compressed = Compress(MakePayload(1_000));

        using var src = new MemoryStream(compressed);
        using var info = XZFileInfo.Read(src);

        Assert.Equal([LzmaCheck.Crc64], info.Checks);
    }

    [Theory]
    [InlineData("good-1-check-none.xz", LzmaCheck.None)]
    [InlineData("good-1-check-crc32.xz", LzmaCheck.Crc32)]
    [InlineData("good-1-check-crc64.xz", LzmaCheck.Crc64)]
    [InlineData("good-1-check-sha256.xz", LzmaCheck.Sha256)]
    public void Read_ReportsTheCheckTypeOfKnownFiles(string fileName, LzmaCheck expected)
    {
        using var fs = File.OpenRead(Path.Combine("files", fileName));
        using var info = XZFileInfo.Read(fs);

        Assert.Equal([expected], info.Checks);
    }

    [Fact]
    public void Read_UncompressedSizeMatchesActualDecompression()
    {
        // Cross-check the index against the only source of truth that matters.
        using var fs = File.OpenRead(Path.Combine("files", "good-2-lzma2.xz"));
        using var info = XZFileInfo.Read(fs);

        fs.Position = 0;
        using var xz = new XZDecompressStream(fs, leaveOpen: true);
        using var ms = new MemoryStream();
        xz.CopyTo(ms);

        Assert.Equal(ms.Length, info.UncompressedSize);
    }

    [Fact]
    public void Read_EmptyStream_ReportsZeroSize()
    {
        byte[] compressed = Compress([]);

        using var src = new MemoryStream(compressed);
        using var info = XZFileInfo.Read(src);

        Assert.Equal(0, info.UncompressedSize);
        Assert.Equal(0, info.BlockCount);
        Assert.Equal(0, info.CompressionRatio);
    }

    [Fact]
    public void CompressionRatio_IsCompressedOverUncompressed()
    {
        byte[] payload = MakePayload(20_000);
        byte[] compressed = Compress(payload);

        using var src = new MemoryStream(compressed);
        using var info = XZFileInfo.Read(src);

        Assert.Equal((double)compressed.Length / payload.Length, info.CompressionRatio, 6);
    }

    [Fact]
    public void Read_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => XZFileInfo.Read(null!));
    }

    [Fact]
    public void Read_NonSeekableStream_Throws()
    {
        using var inner = new MemoryStream(Compress(MakePayload(100)));
        using var nonSeekable = new NonSeekableStream(inner);

        Assert.Throws<ArgumentException>(() => XZFileInfo.Read(nonSeekable));
    }

    [Fact]
    public void Read_NotXzData_Throws()
    {
        using var src = new MemoryStream(new byte[512]);

        var ex = Assert.Throws<XZException>(() => XZFileInfo.Read(src));

        Assert.Equal(7, ex.LzmaReturnCode); // LZMA_FORMAT_ERROR
    }

    [Fact]
    public void Read_TruncatedFile_Throws()
    {
        byte[] compressed = Compress(MakePayload(5_000));
        using var src = new MemoryStream(compressed.AsSpan(0, compressed.Length / 2).ToArray());

        Assert.Throws<XZException>(() => XZFileInfo.Read(src));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using var src = new MemoryStream(Compress(MakePayload(100)));
        var info = XZFileInfo.Read(src);

        long size = info.UncompressedSize;
        info.Dispose();
        info.Dispose();

        // The cached metadata stays readable after disposal.
        Assert.Equal(size, info.UncompressedSize);
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
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
