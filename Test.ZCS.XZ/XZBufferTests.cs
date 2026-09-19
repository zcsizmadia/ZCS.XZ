namespace ZCS.XZ.Tests;

public class XZBufferTests
{
    private static byte[] Incompressible(int size)
    {
        var data = new byte[size];
        new Random(4242).NextBytes(data);
        return data;
    }

    private static byte[] StreamCompress(byte[] data, XZCompressOptions? options = null)
    {
        using var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, options ?? new XZCompressOptions(), leaveOpen: true))
        {
            xz.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    private static byte[] StreamDecompress(byte[] compressed)
    {
        using var src = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(src, leaveOpen: true);
        using var ms = new MemoryStream();
        xz.CopyTo(ms);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(123_456)]
    public void RoundTrip_PreservesData(int size)
    {
        byte[] original = Incompressible(size);

        byte[] compressed = XZBuffer.Compress(original);
        byte[] result = XZBuffer.Decompress(compressed);

        Assert.Equal(original, result);
    }

    [Fact]
    public void Compress_OutputIsReadableByTheStreamDecoder()
    {
        // The buffer API must produce an ordinary .xz stream, not a private encoding.
        byte[] original = Incompressible(50_000);

        byte[] compressed = XZBuffer.Compress(original);

        Assert.Equal(original, StreamDecompress(compressed));
    }

    [Fact]
    public void Decompress_ReadsOutputOfTheStreamEncoder()
    {
        byte[] original = Incompressible(50_000);

        byte[] compressed = StreamCompress(original);

        Assert.Equal(original, XZBuffer.Decompress(compressed));
    }

    [Fact]
    public void Compress_HonorsCompressionLevel()
    {
        // Half a MiB of random bytes, repeated. The second copy is only compressible if the
        // dictionary can reach back 512 KiB, which preset 0 (256 KiB) cannot and preset 6
        // (8 MiB) can. A regular pattern would compress to the same size at every level and
        // prove nothing.
        byte[] half = Incompressible(512 * 1024);
        byte[] original = [.. half, .. half];

        byte[] smallDict = XZBuffer.Compress(original, new XZCompressOptions { Level = XZCompressionLevel.None });
        byte[] largeDict = XZBuffer.Compress(original, new XZCompressOptions { Level = XZCompressionLevel.Default });

        Assert.True(largeDict.Length < smallDict.Length * 0.6,
            $"Expected the 8 MiB dictionary ({largeDict.Length}) to roughly halve the 256 KiB result ({smallDict.Length})");
        Assert.Equal(original, XZBuffer.Decompress(largeDict));
        Assert.Equal(original, XZBuffer.Decompress(smallDict));
    }

    [Fact]
    public void Decompress_GrowsBufferForHighlyCompressibleData()
    {
        // 4 MiB of zeros compresses to a few hundred bytes, so the initial output
        // guess (4x the compressed size) is far too small and must grow.
        byte[] original = new byte[4 * 1024 * 1024];

        byte[] compressed = XZBuffer.Compress(original);
        Assert.True(compressed.Length * 4L < original.Length,
            "Test needs a compression ratio that defeats the initial buffer guess");

        byte[] result = XZBuffer.Decompress(compressed);

        Assert.Equal(original.Length, result.Length);
        Assert.Equal(original, result);
    }

    [Fact]
    public void GetMaxCompressedLength_IsEnoughForIncompressibleData()
    {
        byte[] original = Incompressible(100_000);
        int bound = XZBuffer.GetMaxCompressedLength(original.Length);

        var destination = new byte[bound];
        int written = XZBuffer.Compress(original, destination);

        Assert.True(written <= bound, $"Wrote {written} bytes into a {bound} byte bound");
        Assert.Equal(original, XZBuffer.Decompress(destination.AsSpan(0, written).ToArray()));
    }

    [Fact]
    public void GetMaxCompressedLength_NegativeLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XZBuffer.GetMaxCompressedLength(-1));
    }

    [Fact]
    public void Compress_DestinationTooSmall_Throws()
    {
        byte[] original = Incompressible(50_000);
        var destination = new byte[16];

        var ex = Assert.Throws<XZException>(() => XZBuffer.Compress(original, destination));

        Assert.Equal(10, ex.LzmaReturnCode); // LZMA_BUF_ERROR
    }

    [Fact]
    public void Decompress_DestinationTooSmall_Throws()
    {
        byte[] compressed = XZBuffer.Compress(Incompressible(50_000));
        var destination = new byte[16];

        var ex = Assert.Throws<XZException>(() => XZBuffer.Decompress(compressed, destination));

        Assert.Equal(10, ex.LzmaReturnCode); // LZMA_BUF_ERROR
    }

    [Fact]
    public void Decompress_ExactSizedDestination_Succeeds()
    {
        byte[] original = Incompressible(10_000);
        byte[] compressed = XZBuffer.Compress(original);

        var destination = new byte[original.Length];
        int written = XZBuffer.Decompress(compressed, destination);

        Assert.Equal(original.Length, written);
        Assert.Equal(original, destination);
    }

    [Fact]
    public void Decompress_CorruptInput_Throws()
    {
        byte[] compressed = XZBuffer.Compress(Incompressible(10_000));
        compressed[compressed.Length / 2] ^= 0xFF;

        Assert.Throws<XZException>(() => XZBuffer.Decompress(compressed));
    }

    [Fact]
    public void Decompress_NotXzData_Throws()
    {
        Assert.Throws<XZException>(() => XZBuffer.Decompress("not compressed at all"u8));
    }
}
