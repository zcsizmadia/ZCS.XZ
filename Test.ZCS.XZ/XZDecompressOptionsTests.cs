namespace ZCS.XZ.Tests;

public class XZDecompressOptionsTests
{
    /// <summary>
    /// Builds compressible but not trivially uniform data, large enough that the
    /// multithreaded encoder splits it into more than one Block.
    /// </summary>
    private static byte[] MakePayload(int size)
    {
        var data = new byte[size];
        var rng = new Random(12345);
        for (int i = 0; i < size; i++)
        {
            // Long runs keep it compressible; the rng keeps it from being a single match.
            data[i] = (byte)((i / 64) % 7 == 0 ? rng.Next(256) : i % 251);
        }
        return data;
    }

    private static byte[] Compress(byte[] data, XZCompressOptions options)
    {
        using var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, options, leaveOpen: true))
        {
            xz.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    private static byte[] Decompress(byte[] compressed, XZDecompressOptions options)
    {
        using var src = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(src, options, leaveOpen: true);
        using var ms = new MemoryStream();
        xz.CopyTo(ms);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void ThreadedDecode_RoundTripsMultiBlockStream(int threads)
    {
        // 8 MiB at the fastest preset yields several Blocks from the mt encoder,
        // which is what the mt decoder needs in order to work in parallel at all.
        byte[] original = MakePayload(8 * 1024 * 1024);
        byte[] compressed = Compress(original, new XZCompressOptions
        {
            Level = XZCompressionLevel.Fastest,
            Threads = threads,
        });

        byte[] result = Decompress(compressed, new XZDecompressOptions { Threads = threads });

        Assert.Equal(original, result);
    }

    [Fact]
    public void ThreadedDecode_AutoThreadCount_RoundTrips()
    {
        byte[] original = MakePayload(1024 * 1024);
        byte[] compressed = Compress(original, new XZCompressOptions { Threads = 2 });

        byte[] result = Decompress(compressed, new XZDecompressOptions { Threads = 0 });

        Assert.Equal(original, result);
    }

    [Fact]
    public void ThreadedDecode_SingleBlockStream_StillRoundTrips()
    {
        // A stream the mt decoder cannot parallelize must still decode correctly.
        byte[] original = MakePayload(4096);
        byte[] compressed = Compress(original, new XZCompressOptions { Threads = 1 });

        byte[] result = Decompress(compressed, new XZDecompressOptions { Threads = 4 });

        Assert.Equal(original, result);
    }

    [Fact]
    public void SingleThreadedDecode_DecodesLegacyLzma()
    {
        // The default (auto-detecting) path handles legacy .lzma.
        byte[] compressed = File.ReadAllBytes(Path.Combine("files", "good-known_size-with_eopm.lzma"));

        byte[] result = Decompress(compressed, new XZDecompressOptions { Threads = 1 });

        Assert.NotEmpty(result);
    }

    [Fact]
    public void ThreadedDecode_RejectsLegacyLzma()
    {
        // lzma_stream_decoder_mt is .xz only - it cannot auto-detect .lzma.
        // This asymmetry is deliberate and documented on XZDecompressOptions.Threads.
        byte[] compressed = File.ReadAllBytes(Path.Combine("files", "good-known_size-with_eopm.lzma"));

        var ex = Assert.Throws<XZException>(() =>
            Decompress(compressed, new XZDecompressOptions { Threads = 4 }));

        Assert.Equal(7, ex.LzmaReturnCode); // LZMA_FORMAT_ERROR
    }

    [Theory]
    [InlineData("good-1-check-none.xz", LzmaCheck.None)]
    [InlineData("good-1-check-crc32.xz", LzmaCheck.Crc32)]
    [InlineData("good-1-check-crc64.xz", LzmaCheck.Crc64)]
    [InlineData("good-1-check-sha256.xz", LzmaCheck.Sha256)]
    public void Check_ReportsStreamCheckType(string fileName, LzmaCheck expected)
    {
        using var fs = File.OpenRead(Path.Combine("files", fileName));
        using var xz = new XZDecompressStream(fs);
        using var ms = new MemoryStream();

        xz.CopyTo(ms);

        Assert.Equal(expected, xz.Check);
    }

    [Fact]
    public void Check_ThrowsAfterDispose()
    {
        var xz = new XZDecompressStream(new MemoryStream());
        xz.Dispose();

        Assert.Throws<ObjectDisposedException>(() => xz.Check);
    }

    [Fact]
    public void MemoryLimit_ReturnsLimitSetAtConstruction()
    {
        const ulong limit = 100UL * 1024 * 1024;
        using var xz = new XZDecompressStream(new MemoryStream(), new XZDecompressOptions { MemoryLimit = limit }, leaveOpen: true);

        Assert.Equal(limit, xz.MemoryLimit);
    }

    [Fact]
    public void MemoryLimit_CanBeRaisedToRecoverFromMemlimitError()
    {
        byte[] original = MakePayload(64 * 1024);
        byte[] compressed = Compress(original, new XZCompressOptions { Level = XZCompressionLevel.Maximum });

        using var src = new MemoryStream(compressed);
        // 1 KiB is far below what a level-9 dictionary needs, so the first read fails.
        using var xz = new XZDecompressStream(src, new XZDecompressOptions { MemoryLimit = 1024 }, leaveOpen: true);

        var ex = Assert.Throws<XZException>(() => xz.CopyTo(new MemoryStream()));
        Assert.Equal(6, ex.LzmaReturnCode); // LZMA_MEMLIMIT_ERROR

        // Raising the limit lets the same stream continue rather than forcing a rebuild.
        xz.MemoryLimit = ulong.MaxValue;
        Assert.Equal(ulong.MaxValue, xz.MemoryLimit);

        using var ms = new MemoryStream();
        xz.CopyTo(ms);
        Assert.Equal(original, ms.ToArray());
    }

    [Fact]
    public void MemoryLimit_ThrowsAfterDispose()
    {
        var xz = new XZDecompressStream(new MemoryStream());
        xz.Dispose();

        Assert.Throws<ObjectDisposedException>(() => xz.MemoryLimit);
        Assert.Throws<ObjectDisposedException>(() => xz.MemoryLimit = 1024);
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new XZDecompressStream(new MemoryStream(), (XZDecompressOptions)null!, leaveOpen: true));
    }

    [Fact]
    public void Constructor_InvalidBufferSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new XZDecompressStream(new MemoryStream(), new XZDecompressOptions { BufferSize = 0 }, leaveOpen: true));
    }

    [Fact]
    public void ThreadedDecode_LowThreadingMemoryLimit_StillDecodes()
    {
        // memlimit_threading is a soft limit: exceeding it must reduce the thread
        // count rather than fail. A 1 KiB limit forces the single-threaded fallback.
        byte[] original = MakePayload(1024 * 1024);
        byte[] compressed = Compress(original, new XZCompressOptions { Threads = 2 });

        byte[] result = Decompress(compressed, new XZDecompressOptions
        {
            Threads = 4,
            MemoryLimitThreading = 1024,
        });

        Assert.Equal(original, result);
    }
}
