namespace ZCS.XZ.Tests;

public class XZFormatTests
{
    private static readonly byte[] XzMagic = [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00];

    private static byte[] MakeData(int size)
    {
        var data = new byte[size];
        var rng = new Random(2024);
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 97 == 0 ? rng.Next(256) : i % 13);
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

    private static byte[] Decompress(byte[] compressed)
    {
        using var src = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(src, leaveOpen: true);
        using var ms = new MemoryStream();
        xz.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void LzmaAlone_RoundTrips()
    {
        byte[] original = MakeData(100_000);

        byte[] compressed = Compress(original, new XZCompressOptions { Format = XZFormat.LzmaAlone });

        Assert.Equal(original, Decompress(compressed));
    }

    [Fact]
    public void LzmaAlone_DoesNotWriteXzContainer()
    {
        byte[] compressed = Compress(MakeData(4096), new XZCompressOptions { Format = XZFormat.LzmaAlone });

        Assert.False(compressed.AsSpan(0, XzMagic.Length).SequenceEqual(XzMagic),
            "Expected legacy .lzma output, but the stream starts with the .xz magic bytes");
    }

    [Fact]
    public void Xz_WritesXzContainer()
    {
        byte[] compressed = Compress(MakeData(4096), new XZCompressOptions { Format = XZFormat.Xz });

        Assert.True(compressed.AsSpan(0, XzMagic.Length).SequenceEqual(XzMagic));
    }

    /// <summary>
    /// The .lzma header stores the dictionary size in bytes 1-4, and each preset maps to a
    /// fixed dictionary size. Reading it back proves lzma_lzma_preset applied the requested
    /// preset, which a size comparison cannot show reliably (a higher preset does not always
    /// produce smaller output).
    /// </summary>
    [Theory]
    [InlineData(XZCompressionLevel.None, 256u * 1024)]
    [InlineData(XZCompressionLevel.Fastest, 1u * 1024 * 1024)]
    [InlineData(XZCompressionLevel.Default, 8u * 1024 * 1024)]
    [InlineData(XZCompressionLevel.Maximum, 64u * 1024 * 1024)]
    public void LzmaAlone_HeaderCarriesPresetDictionarySize(XZCompressionLevel level, uint expectedDictSize)
    {
        byte[] compressed = Compress(MakeData(4096), new XZCompressOptions
        {
            Format = XZFormat.LzmaAlone,
            Level = level,
        });

        uint dictSize = BitConverter.ToUInt32(compressed, 1);

        Assert.Equal(expectedDictSize, dictSize);
        Assert.Equal(MakeData(4096), Decompress(compressed));
    }

    [Fact]
    public void LzmaAlone_WithMultipleThreads_Throws()
    {
        // The legacy format has no block structure, so it cannot be encoded in parallel.
        var options = new XZCompressOptions { Format = XZFormat.LzmaAlone, Threads = 4 };

        Assert.Throws<ArgumentException>(() =>
            new XZCompressStream(new MemoryStream(), options, leaveOpen: true));
    }

    [Fact]
    public void LzmaAlone_Flush_DoesNotThrow()
    {
        // lzma_alone_encoder accepts only LZMA_RUN and LZMA_FINISH, so Flush must not
        // try to issue an encoder flush action.
        byte[] original = MakeData(50_000);

        using var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, new XZCompressOptions { Format = XZFormat.LzmaAlone }, leaveOpen: true))
        {
            xz.Write(original, 0, original.Length);
            xz.Flush();
        }

        Assert.Equal(original, Decompress(ms.ToArray()));
    }

    /// <summary>
    /// Regression test: Flush used to issue LZMA_SYNC_FLUSH unconditionally, which the
    /// multithreaded encoder rejects with LZMA_PROG_ERROR.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Flush_WorksForEveryThreadCount(int threads)
    {
        byte[] original = MakeData(200_000);

        using var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, new XZCompressOptions { Threads = threads }, leaveOpen: true))
        {
            xz.Write(original, 0, original.Length / 2);
            xz.Flush();
            xz.Write(original, original.Length / 2, original.Length - original.Length / 2);
        }

        Assert.Equal(original, Decompress(ms.ToArray()));
    }

    /// <summary>
    /// A StreamWriter flushes the stream it wraps on dispose, which is the most common
    /// way the multithreaded Flush defect surfaced in practice.
    /// </summary>
    [Fact]
    public void Flush_ThroughStreamWriter_WorksWithMultipleThreads()
    {
        const string text = "the quick brown fox jumps over the lazy dog";

        using var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, new XZCompressOptions { Threads = 4 }, leaveOpen: true))
        using (var writer = new StreamWriter(xz))
        {
            for (int i = 0; i < 1000; i++)
            {
                writer.WriteLine(text);
            }
        }

        byte[] decompressed = Decompress(ms.ToArray());
        Assert.Contains(text, System.Text.Encoding.UTF8.GetString(decompressed));
    }
}
