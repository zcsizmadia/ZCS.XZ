namespace ZCS.XZ.Tests;

public class XZProgressTests
{
    private static byte[] MakeData(int size)
    {
        var data = new byte[size];
        var rng = new Random(555);
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(i % 53 == 0 ? rng.Next(256) : i % 17);
        }
        return data;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void CompressStream_GetProgress_TracksInput(int threads)
    {
        byte[] data = MakeData(1024 * 1024);

        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions { Threads = threads }, leaveOpen: true);

        // BytesOut is not necessarily 0 here: the multithreaded encoder emits the 12-byte
        // stream header as soon as it is initialized.
        Assert.Equal(0ul, xz.GetProgress().BytesIn);

        xz.Write(data, 0, data.Length);
        xz.Flush();

        XZProgress progress = xz.GetProgress();
        Assert.Equal((ulong)data.Length, progress.BytesIn);
        Assert.True(progress.BytesOut > 0, "Expected some compressed output to be reported");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void DecompressStream_GetProgress_TracksOutput(int threads)
    {
        byte[] original = MakeData(1024 * 1024);

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, new XZCompressOptions { Threads = threads }, leaveOpen: true))
            {
                xz.Write(original, 0, original.Length);
            }
            compressed = ms.ToArray();
        }

        using var src = new MemoryStream(compressed);
        using var dec = new XZDecompressStream(src, new XZDecompressOptions { Threads = threads }, leaveOpen: true);
        using var result = new MemoryStream();

        dec.CopyTo(result);

        XZProgress progress = dec.GetProgress();
        Assert.Equal((ulong)original.Length, progress.BytesOut);
        Assert.True(progress.BytesIn > 0, "Expected some compressed input to be reported");
    }

    [Fact]
    public void CompressStream_GetProgress_ThrowsAfterDispose()
    {
        var xz = new XZCompressStream(new MemoryStream());
        xz.Dispose();

        Assert.Throws<ObjectDisposedException>(() => xz.GetProgress());
    }

    [Fact]
    public void DecompressStream_GetProgress_ThrowsAfterDispose()
    {
        var xz = new XZDecompressStream(new MemoryStream());
        xz.Dispose();

        Assert.Throws<ObjectDisposedException>(() => xz.GetProgress());
    }

    [Fact]
    public void XZProgress_EqualityIsByValue()
    {
        Assert.Equal(new XZProgress(10, 20), new XZProgress(10, 20));
        Assert.NotEqual(new XZProgress(10, 20), new XZProgress(10, 21));
    }
}
