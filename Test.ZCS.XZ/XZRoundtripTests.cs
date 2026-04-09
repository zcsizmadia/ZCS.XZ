using System.Text;

namespace ZCS.XZ.Tests;

/// <summary>
/// Roundtrip tests: compress with XZCompressStream, decompress with XZDecompressStream.
/// </summary>
public class XZRoundtripTests
{
    [Fact]
    public void Roundtrip_SmallData()
    {
        var original = Encoding.UTF8.GetBytes("Roundtrip test data.");
        var result = CompressAndDecompress(original);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_LargeData()
    {
        var original = GenerateTestData(1_000_000);
        var result = CompressAndDecompress(original);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_VeryLargeData()
    {
        var original = GenerateTestData(10_000_000);
        var result = CompressAndDecompress(original);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_EmptyData()
    {
        var original = Array.Empty<byte>();
        var result = CompressAndDecompress(original);
        Assert.Equal(original, result);
    }

    [Theory]
    [InlineData(XZCompressionLevel.None)]
    [InlineData(XZCompressionLevel.Fastest)]
    [InlineData(XZCompressionLevel.Default)]
    [InlineData(XZCompressionLevel.Maximum)]
    public void Roundtrip_AllLevels(XZCompressionLevel level)
    {
        var original = GenerateTestData(50_000);
        var opts = new XZCompressOptions { Level = level };
        var result = CompressAndDecompress(original, opts);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_ExtremeMode()
    {
        var original = GenerateTestData(50_000);
        var opts = new XZCompressOptions { Level = XZCompressionLevel.Default, Extreme = true };
        var result = CompressAndDecompress(original, opts);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_Multithreaded()
    {
        var original = GenerateTestData(200_000);
        var opts = new XZCompressOptions { Threads = 4 };
        var result = CompressAndDecompress(original, opts);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_AutoDetectThreads()
    {
        var original = GenerateTestData(200_000);
        var opts = new XZCompressOptions { Threads = 0 };
        var result = CompressAndDecompress(original, opts);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_BinaryData()
    {
        // All 256 byte values repeated
        var original = new byte[256 * 100];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 256);

        var result = CompressAndDecompress(original);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_SmallBufferSize()
    {
        var original = GenerateTestData(50_000);
        var opts = new XZCompressOptions { BufferSize = 256 };
        var result = CompressAndDecompress(original, opts);
        Assert.Equal(original, result);
    }

    private static byte[] CompressAndDecompress(byte[] original, XZCompressOptions? opts = null)
    {
        opts ??= new XZCompressOptions();

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var compressor = new XZCompressStream(ms, opts, leaveOpen: true))
            {
                compressor.Write(original, 0, original.Length);
            }
            compressed = ms.ToArray();
        }

        using (var ms = new MemoryStream(compressed))
        using (var decompressor = new XZDecompressStream(ms))
        using (var result = new MemoryStream())
        {
            decompressor.CopyTo(result);
            return result.ToArray();
        }
    }

    private static byte[] GenerateTestData(int size)
    {
        var data = new byte[size];
        var rng = new Random(42);
        rng.NextBytes(data);
        for (int i = 0; i < size; i++)
            data[i] = (byte)(data[i] % 26 + 'A');
        return data;
    }
}
