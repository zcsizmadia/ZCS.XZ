using System.Diagnostics;
using System.Text;
using Test.ZCS.XZ;

namespace ZCS.XZ.Tests;

public class XZDecompressStreamTests
{
    /// <summary>
    /// Compress with xz.exe, then decompress with XZDecompressStream and verify roundtrip.
    /// </summary>
    [Fact]
    public void Decompress_XzExeCompressed_ReturnsOriginalData()
    {
        var original = Encoding.UTF8.GetBytes("Hello, XZ decompression world! This is a test payload.");
        var compressed = CompressWithXzExe(original);

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();
        xz.CopyTo(result);

        Assert.Equal(original, result.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(9)]
    public void Decompress_VariousCompressionLevels_ReturnsOriginalData(int level)
    {
        var original = GenerateTestData(10_000);
        var compressed = CompressWithXzExe(original, $"-{level}");

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();
        xz.CopyTo(result);

        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public void Decompress_ExtremeCompression_ReturnsOriginalData()
    {
        var original = GenerateTestData(10_000);
        var compressed = CompressWithXzExe(original, "-6e");

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();
        xz.CopyTo(result);

        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public void Decompress_LargeData_ReturnsOriginalData()
    {
        var original = GenerateTestData(1_000_000);
        var compressed = CompressWithXzExe(original);

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();
        xz.CopyTo(result);

        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public void Decompress_EmptyData_ReturnsEmpty()
    {
        var original = Array.Empty<byte>();
        var compressed = CompressWithXzExe(original);

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();
        xz.CopyTo(result);

        Assert.Empty(result.ToArray());
    }

    [Fact]
    public void Decompress_SmallReads_ReturnsCorrectData()
    {
        var original = Encoding.UTF8.GetBytes("Small read test data for XZ decompression.");
        var compressed = CompressWithXzExe(original);

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();

        // Read in small chunks of 3 bytes at a time
        var buf = new byte[3];
        int read;
        while ((read = xz.Read(buf, 0, buf.Length)) > 0)
            result.Write(buf, 0, read);

        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public void Decompress_ReadAfterEnd_ReturnsZero()
    {
        var original = Encoding.UTF8.GetBytes("Done");
        var compressed = CompressWithXzExe(original);

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);

        // Read all data
        var buf = new byte[1024];
        int total = 0;
        int read;
        while ((read = xz.Read(buf, total, buf.Length - total)) > 0)
            total += read;

        // Subsequent reads should return 0
        Assert.Equal(0, xz.Read(buf, 0, buf.Length));
        Assert.Equal(0, xz.Read(buf, 0, buf.Length));
    }

    [Fact]
    public void Decompress_CorruptData_ThrowsXZException()
    {
        // Random data without a valid xz/lzma magic — auto_decoder should reject it
        var data = new byte[1024];
        new Random(123).NextBytes(data);
        using var ms = new MemoryStream(data);
        using var xz = new XZDecompressStream(ms);

        Assert.Throws<XZException>(() =>
        {
            var buf = new byte[1024];
            // Keep reading until the decoder reports corruption
            int read;
            do
            {
                read = xz.Read(buf, 0, buf.Length);
            } while (read > 0);
        });
    }

    [Fact]
    public void DecompressStream_LeaveOpenTrue_InnerStreamRemainsOpen()
    {
        var original = Encoding.UTF8.GetBytes("test");
        var compressed = CompressWithXzExe(original);
        var ms = new MemoryStream(compressed);

        using (var xz = new XZDecompressStream(ms, leaveOpen: true))
        {
            var buf = new byte[1024];
            _ = xz.Read(buf, 0, buf.Length);
        }

        Assert.True(ms.CanRead);
        ms.Dispose();
    }

    [Fact]
    public void DecompressStream_LeaveOpenFalse_InnerStreamDisposed()
    {
        var original = Encoding.UTF8.GetBytes("test");
        var compressed = CompressWithXzExe(original);
        var ms = new MemoryStream(compressed);

        using (var xz = new XZDecompressStream(ms, leaveOpen: false))
        {
            var buf = new byte[1024];
            _ = xz.Read(buf, 0, buf.Length);
        }

        Assert.Throws<ObjectDisposedException>(() => ms.ReadByte());
    }

    [Fact]
    public void DecompressStream_CanWriteIsFalse()
    {
        var compressed = CompressWithXzExe(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);

        Assert.True(xz.CanRead);
        Assert.False(xz.CanSeek);
        Assert.False(xz.CanWrite);
    }

    [Fact]
    public void DecompressStream_WriteThrowsNotSupported()
    {
        var compressed = CompressWithXzExe(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);

        Assert.Throws<NotSupportedException>(() => xz.Write(new byte[10], 0, 10));
        Assert.Throws<NotSupportedException>(() => xz.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => xz.SetLength(0));
        Assert.Throws<NotSupportedException>(() => _ = xz.Length);
        Assert.Throws<NotSupportedException>(() => _ = xz.Position);
        Assert.Throws<NotSupportedException>(() => xz.Position = 0);
    }

    private static byte[] CompressWithXzExe(byte[] data, string extraArgs = "")
    {
        var tmpIn = Path.GetTempFileName();
        var tmpXz = tmpIn + ".xz";
        try
        {
            File.WriteAllBytes(tmpIn, data);
            XzExecutable.Run($"--compress --keep --force {extraArgs} \"{tmpIn}\"");
            return File.ReadAllBytes(tmpXz);
        }
        finally
        {
            File.Delete(tmpIn);
            File.Delete(tmpXz);
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
