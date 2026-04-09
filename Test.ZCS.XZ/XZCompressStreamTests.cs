using System.Diagnostics;
using System.Text;
using Test.ZCS.XZ;

namespace ZCS.XZ.Tests;

public class XZCompressStreamTests
{
    /// <summary>
    /// Compress data with XZCompressStream, then decompress with xz.exe and verify roundtrip.
    /// </summary>
    [Fact]
    public void Compress_DefaultLevel_CanBeDecompressedByXzExe()
    {
        var original = Encoding.UTF8.GetBytes("Hello, XZ compression world! This is a test payload.");

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true))
            {
                xz.Write(original, 0, original.Length);
            }
            compressed = ms.ToArray();
        }

        // Compressed data should start with the xz magic bytes: FD 37 7A 58 5A 00
        Assert.True(compressed.Length >= 6);
        Assert.Equal(0xFD, compressed[0]);
        Assert.Equal(0x37, compressed[1]);
        Assert.Equal(0x7A, compressed[2]);
        Assert.Equal(0x58, compressed[3]);
        Assert.Equal(0x5A, compressed[4]);
        Assert.Equal(0x00, compressed[5]);

        // Write compressed data to a temp file, decompress with xz.exe
        var tmpXz = Path.GetTempFileName() + ".xz";
        var tmpOut = Path.ChangeExtension(tmpXz, null);
        try
        {
            File.WriteAllBytes(tmpXz, compressed);
            XzExecutable.Run($"--decompress --keep --force \"{tmpXz}\"");

            var decompressed = File.ReadAllBytes(tmpOut);
            Assert.Equal(original, decompressed);
        }
        finally
        {
            File.Delete(tmpXz);
            File.Delete(tmpOut);
        }
    }

    [Theory]
    [InlineData(XZCompressionLevel.None)]
    [InlineData(XZCompressionLevel.Fastest)]
    [InlineData(XZCompressionLevel.Default)]
    [InlineData(XZCompressionLevel.Maximum)]
    public void Compress_VariousLevels_ProducesValidXz(XZCompressionLevel level)
    {
        var original = GenerateTestData(10_000);

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            var opts = new XZCompressOptions { Level = level };
            using (var xz = new XZCompressStream(ms, opts, leaveOpen: true))
            {
                xz.Write(original, 0, original.Length);
            }
            compressed = ms.ToArray();
        }

        var tmpXz = Path.GetTempFileName() + ".xz";
        var tmpOut = Path.ChangeExtension(tmpXz, null);
        try
        {
            File.WriteAllBytes(tmpXz, compressed);
            XzExecutable.Run($"--decompress --keep --force \"{tmpXz}\"");

            var decompressed = File.ReadAllBytes(tmpOut);
            Assert.Equal(original, decompressed);
        }
        finally
        {
            File.Delete(tmpXz);
            File.Delete(tmpOut);
        }
    }

    [Fact]
    public void Compress_ExtremeMode_ProducesValidXz()
    {
        var original = GenerateTestData(10_000);

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            var opts = new XZCompressOptions { Level = XZCompressionLevel.Default, Extreme = true };
            using (var xz = new XZCompressStream(ms, opts, leaveOpen: true))
            {
                xz.Write(original, 0, original.Length);
            }
            compressed = ms.ToArray();
        }

        var tmpXz = Path.GetTempFileName() + ".xz";
        var tmpOut = Path.ChangeExtension(tmpXz, null);
        try
        {
            File.WriteAllBytes(tmpXz, compressed);
            XzExecutable.Run($"--decompress --keep --force \"{tmpXz}\"");

            var decompressed = File.ReadAllBytes(tmpOut);
            Assert.Equal(original, decompressed);
        }
        finally
        {
            File.Delete(tmpXz);
            File.Delete(tmpOut);
        }
    }

    [Fact]
    public void Compress_Multithreaded_ProducesValidXz()
    {
        var original = GenerateTestData(100_000);

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            var opts = new XZCompressOptions { Threads = 2 };
            using (var xz = new XZCompressStream(ms, opts, leaveOpen: true))
            {
                xz.Write(original, 0, original.Length);
            }
            compressed = ms.ToArray();
        }

        var tmpXz = Path.GetTempFileName() + ".xz";
        var tmpOut = Path.ChangeExtension(tmpXz, null);
        try
        {
            File.WriteAllBytes(tmpXz, compressed);
            XzExecutable.Run($"--decompress --keep --force \"{tmpXz}\"");

            var decompressed = File.ReadAllBytes(tmpOut);
            Assert.Equal(original, decompressed);
        }
        finally
        {
            File.Delete(tmpXz);
            File.Delete(tmpOut);
        }
    }

    [Fact]
    public void Compress_MultipleSmallWrites_ProducesValidXz()
    {
        var parts = new[] { "First chunk. ", "Second chunk. ", "Third chunk." };
        var original = Encoding.UTF8.GetBytes(string.Concat(parts));

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true))
            {
                foreach (var part in parts)
                    xz.Write(Encoding.UTF8.GetBytes(part));
            }
            compressed = ms.ToArray();
        }

        var tmpXz = Path.GetTempFileName() + ".xz";
        var tmpOut = Path.ChangeExtension(tmpXz, null);
        try
        {
            File.WriteAllBytes(tmpXz, compressed);
            XzExecutable.Run($"--decompress --keep --force \"{tmpXz}\"");

            var decompressed = File.ReadAllBytes(tmpOut);
            Assert.Equal(original, decompressed);
        }
        finally
        {
            File.Delete(tmpXz);
            File.Delete(tmpOut);
        }
    }

    [Fact]
    public void Compress_LargeData_ProducesValidXz()
    {
        var original = GenerateTestData(1_000_000);

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true))
            {
                xz.Write(original, 0, original.Length);
            }
            compressed = ms.ToArray();
        }

        // Compressed should be smaller than original (for repetitive data)
        Assert.True(compressed.Length < original.Length);

        var tmpXz = Path.GetTempFileName() + ".xz";
        var tmpOut = Path.ChangeExtension(tmpXz, null);
        try
        {
            File.WriteAllBytes(tmpXz, compressed);
            XzExecutable.Run($"--decompress --keep --force \"{tmpXz}\"");

            var decompressed = File.ReadAllBytes(tmpOut);
            Assert.Equal(original, decompressed);
        }
        finally
        {
            File.Delete(tmpXz);
            File.Delete(tmpOut);
        }
    }

    [Fact]
    public void Compress_EmptyData_ProducesValidXz()
    {
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true))
            {
                // Write nothing
            }
            compressed = ms.ToArray();
        }

        var tmpXz = Path.GetTempFileName() + ".xz";
        var tmpOut = Path.ChangeExtension(tmpXz, null);
        try
        {
            File.WriteAllBytes(tmpXz, compressed);
            XzExecutable.Run($"--decompress --keep --force \"{tmpXz}\"");

            var decompressed = File.ReadAllBytes(tmpOut);
            Assert.Empty(decompressed);
        }
        finally
        {
            File.Delete(tmpXz);
            File.Delete(tmpOut);
        }
    }

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
