using System.Diagnostics;
using System.Text;
using Test.ZCS.XZ;

namespace ZCS.XZ.Tests;

public class XZCompressStreamTests
{
    [Fact]
    public void Compress_HigherLevel_ProducesSmallerOrEqualOutput()
    {
        var original = GenerateTestData(100_000);

        byte[] compressedFast;
        using (var ms = new MemoryStream())
        {
            var opts = new XZCompressOptions { Level = XZCompressionLevel.Fastest };
            using (var xz = new XZCompressStream(ms, opts, leaveOpen: true))
                xz.Write(original);
            compressedFast = ms.ToArray();
        }

        byte[] compressedMax;
        using (var ms = new MemoryStream())
        {
            var opts = new XZCompressOptions { Level = XZCompressionLevel.Maximum };
            using (var xz = new XZCompressStream(ms, opts, leaveOpen: true))
                xz.Write(original);
            compressedMax = ms.ToArray();
        }

        Assert.True(compressedMax.Length <= compressedFast.Length,
            $"Max ({compressedMax.Length}) should be <= Fastest ({compressedFast.Length})");
    }

    [Fact]
    public void CompressStream_LeaveOpenTrue_InnerStreamRemainsOpen()
    {
        var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true))
        {
            xz.Write(Encoding.UTF8.GetBytes("test"));
        }

        // Should still be able to use the stream
        Assert.True(ms.CanRead);
        ms.Dispose();
    }

    [Fact]
    public void CompressStream_LeaveOpenFalse_InnerStreamDisposed()
    {
        var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: false))
        {
            xz.Write(Encoding.UTF8.GetBytes("test"));
        }

        Assert.Throws<ObjectDisposedException>(() => ms.ReadByte());
    }

    [Fact]
    public void CompressStream_CanReadIsFalse()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        Assert.False(xz.CanRead);
        Assert.False(xz.CanSeek);
        Assert.True(xz.CanWrite);
    }

    [Fact]
    public void CompressStream_ReadThrowsNotSupported()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => xz.Read(new byte[10], 0, 10));
        Assert.Throws<NotSupportedException>(() => xz.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => xz.SetLength(0));
        Assert.Throws<NotSupportedException>(() => _ = xz.Length);
        Assert.Throws<NotSupportedException>(() => _ = xz.Position);
        Assert.Throws<NotSupportedException>(() => xz.Position = 0);
    }

    private static byte[] GenerateTestData(int size)
    {
        var data = new byte[size];
        var rng = new Random(42);
        rng.NextBytes(data);
        // Make it somewhat compressible by repeating patterns
        for (int i = 0; i < size; i++)
            data[i] = (byte)(data[i] % 26 + 'A');
        return data;
    }
}
