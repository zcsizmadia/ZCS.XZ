namespace ZCS.XZ.Tests;

public class XZMemoryUsageTests
{
    [Fact]
    public void GetMemoryUsage_SingleThreaded_ReturnsPlausibleValue()
    {
        var options = new XZCompressOptions { Level = XZCompressionLevel.Default };

        ulong usage = options.GetMemoryUsage();

        Assert.NotEqual(ulong.MaxValue, usage);
        Assert.True(usage > 1024 * 1024, $"Expected more than 1 MiB, got {usage}");
    }

    [Fact]
    public void GetMemoryUsage_HigherLevelCostsMoreMemory()
    {
        ulong fastest = new XZCompressOptions { Level = XZCompressionLevel.Fastest }.GetMemoryUsage();
        ulong maximum = new XZCompressOptions { Level = XZCompressionLevel.Maximum }.GetMemoryUsage();

        Assert.True(maximum > fastest, $"Expected level 9 ({maximum}) to exceed level 1 ({fastest})");
    }

    [Fact]
    public void GetMemoryUsage_MultithreadedCostsMoreThanSingleThreaded()
    {
        ulong single = new XZCompressOptions { Threads = 1 }.GetMemoryUsage();
        ulong multi = new XZCompressOptions { Threads = 4 }.GetMemoryUsage();

        Assert.True(multi > single, $"Expected 4 threads ({multi}) to exceed 1 thread ({single})");
    }

    [Fact]
    public void GetDecoderMemoryUsage_IsCheaperThanEncoding()
    {
        var options = new XZCompressOptions { Level = XZCompressionLevel.Default };

        ulong encoder = options.GetMemoryUsage();
        ulong decoder = options.GetDecoderMemoryUsage();

        Assert.NotEqual(ulong.MaxValue, decoder);
        Assert.True(decoder > 0);
        Assert.True(decoder < encoder, $"Expected decoding ({decoder}) to cost less than encoding ({encoder})");
    }

    [Fact]
    public void GetDecoderMemoryUsage_IgnoresThreadCount()
    {
        // Decoder memory depends only on the preset the data was compressed with.
        ulong single = new XZCompressOptions { Threads = 1 }.GetDecoderMemoryUsage();
        ulong multi = new XZCompressOptions { Threads = 8 }.GetDecoderMemoryUsage();

        Assert.Equal(single, multi);
    }

    [Fact]
    public void GetDecoderMemoryUsage_IsLargeEnoughToActuallyDecode()
    {
        // The estimate is only useful if a limit set to it actually permits decoding.
        var options = new XZCompressOptions { Level = XZCompressionLevel.Default };
        byte[] original = new byte[256 * 1024];
        new Random(7).NextBytes(original);

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, options, leaveOpen: true))
            {
                xz.Write(original, 0, original.Length);
            }
            compressed = ms.ToArray();
        }

        using var src = new MemoryStream(compressed);
        using var dec = new XZDecompressStream(src, new XZDecompressOptions
        {
            MemoryLimit = options.GetDecoderMemoryUsage(),
        }, leaveOpen: true);
        using var result = new MemoryStream();

        dec.CopyTo(result);

        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public void CpuThreads_IsPositive()
    {
        Assert.True(LibLzmaNativeMethods.CpuThreads > 0);
    }

    [Fact]
    public void PhysicalMemory_IsZeroOrPlausible()
    {
        ulong physmem = LibLzmaNativeMethods.PhysicalMemory;

        // liblzma reports 0 when it cannot detect physical memory on a platform.
        Assert.True(physmem == 0 || physmem > 64UL * 1024 * 1024,
            $"Expected 0 or more than 64 MiB, got {physmem}");
    }

    [Fact]
    public void GetThreadCount_ZeroResolvesToCpuThreads()
    {
        var options = new XZCompressOptions { Threads = 0 };

        // Exercised through the estimator, which uses the resolved thread count
        // to choose between the single-threaded and multithreaded encoder.
        ulong autoUsage = options.GetMemoryUsage();
        ulong explicitUsage = new XZCompressOptions { Threads = LibLzmaNativeMethods.CpuThreads }.GetMemoryUsage();

        Assert.Equal(explicitUsage, autoUsage);
    }
}
