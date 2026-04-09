using System.Reflection;
using System.Text;

namespace ZCS.XZ.Tests;

/// <summary>
/// Tests targeting uncovered code paths for 100% coverage.
/// </summary>
public class XZExceptionTests
{
    [Fact]
    public void Constructor_SingleArg_SetsReturnCode()
    {
        var ex = new XZException(5);
        Assert.Equal(5, ex.LzmaReturnCode);
        Assert.Contains("LZMA_MEM_ERROR", ex.Message);
    }

    [Fact]
    public void Constructor_TwoArg_SetsReturnCodeAndMessage()
    {
        var ex = new XZException(99, "custom message");
        Assert.Equal(99, ex.LzmaReturnCode);
        Assert.Equal("custom message", ex.Message);
    }

    [Theory]
    [InlineData(5, "LZMA_MEM_ERROR")]
    [InlineData(6, "LZMA_MEMLIMIT_ERROR")]
    [InlineData(7, "LZMA_FORMAT_ERROR")]
    [InlineData(8, "LZMA_OPTIONS_ERROR")]
    [InlineData(9, "LZMA_DATA_ERROR")]
    [InlineData(10, "LZMA_BUF_ERROR")]
    [InlineData(11, "LZMA_PROG_ERROR")]
    [InlineData(999, "Unknown liblzma error")]
    public void GetMessage_AllCodes_ReturnsExpectedMessage(int code, string expectedSubstring)
    {
        var ex = new XZException(code);
        Assert.Contains(expectedSubstring, ex.Message);
    }
}

public class LibLzmaNativeMethodsTests
{
    [Fact]
    public void NativeVersion_ReturnsValidVersion()
    {
        var version = LibLzmaNativeMethods.NativeVersion;
        Assert.True(version.Major >= 5, $"Expected major version >= 5, got {version.Major}");
        Assert.True(version.Minor >= 0);
        Assert.True(version.Build >= 0);
    }

    [Fact]
    public void NativeVersionString_ReturnsNonEmpty()
    {
        var versionString = LibLzmaNativeMethods.NativeVersionString;
        Assert.False(string.IsNullOrEmpty(versionString));
        Assert.Contains(".", versionString);
    }

    [Fact]
    public void NativeVersion_MatchesNativeVersionString()
    {
        var version = LibLzmaNativeMethods.NativeVersion;
        var versionString = LibLzmaNativeMethods.NativeVersionString;
        Assert.StartsWith($"{version.Major}.{version.Minor}.{version.Build}", versionString);
    }

    [Fact]
    public void NativeVersion_MatchesDirectoryBuildPropsVersion()
    {
        var expected = typeof(LibLzmaNativeMethodsTests).Assembly
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .First(a => a.Key == "LibLzmaVersion").Value;

        var nativeVersion = LibLzmaNativeMethods.NativeVersion;
        Assert.Equal(expected, $"{nativeVersion.Major}.{nativeVersion.Minor}.{nativeVersion.Build}");
    }
}

public class XZCompressStreamEdgeCaseTests
{
    [Fact]
    public void Constructor_SingleArg_Works()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms);
        xz.Write(Encoding.UTF8.GetBytes("hello"));
    }

    [Fact]
    public void Constructor_TwoArgs_Works()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions());
        xz.Write(Encoding.UTF8.GetBytes("hello"));
    }

    [Fact]
    public void Constructor_NullStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new XZCompressStream(null!));
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNull()
    {
        using var ms = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => new XZCompressStream(ms, null!));
    }

    [Fact]
    public void Constructor_NonWritableStream_ThrowsArgument()
    {
        using var ms = new MemoryStream(new byte[10], writable: false);
        Assert.Throws<ArgumentException>(() => new XZCompressStream(ms));
    }

    [Fact]
    public void Write_NullBuffer_ThrowsArgumentNull()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        Assert.Throws<ArgumentNullException>(() => xz.Write(null!, 0, 0));
    }

    [Fact]
    public void Write_NegativeOffset_ThrowsArgumentOutOfRange()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => xz.Write(new byte[10], -1, 1));
    }

    [Fact]
    public void Write_NegativeCount_ThrowsArgumentOutOfRange()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => xz.Write(new byte[10], 0, -1));
    }

    [Fact]
    public void Write_OffsetPlusCountExceedsLength_ThrowsArgument()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        Assert.Throws<ArgumentException>(() => xz.Write(new byte[10], 5, 10));
    }

    [Fact]
    public void Write_ZeroCount_DoesNothing()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        xz.Write(new byte[10], 0, 0);
        // No exception, no output (stream empty until Finish)
    }

    [Fact]
    public void Write_SpanOverload_Works()
    {
        byte[] compressed;
        var data = Encoding.UTF8.GetBytes("span test data");
        using (var ms = new MemoryStream())
        {
            using (var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true))
            {
                xz.Write(data.AsSpan());
            }
            compressed = ms.ToArray();
        }

        // Verify by decompressing
        using var cms = new MemoryStream(compressed);
        using var dxz = new XZDecompressStream(cms);
        using var result = new MemoryStream();
        dxz.CopyTo(result);
        Assert.Equal(data, result.ToArray());
    }

    [Fact]
    public void Flush_Works()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        xz.Write(Encoding.UTF8.GetBytes("flush test"));
        xz.Flush();
        // After flush, some compressed data should be written
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void Write_AfterDispose_ThrowsObjectDisposed()
    {
        using var ms = new MemoryStream();
        var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        xz.Dispose();
        Assert.Throws<ObjectDisposedException>(() => xz.Write(new byte[1], 0, 1));
    }

    [Fact]
    public void WriteSpan_AfterDispose_ThrowsObjectDisposed()
    {
        using var ms = new MemoryStream();
        var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        xz.Dispose();
        Assert.Throws<ObjectDisposedException>(() => xz.Write(new byte[1].AsSpan()));
    }

    [Fact]
    public void Flush_AfterDispose_ThrowsObjectDisposed()
    {
        using var ms = new MemoryStream();
        var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        xz.Dispose();
        Assert.Throws<ObjectDisposedException>(() => xz.Flush());
    }

    [Fact]
    public void CanWrite_AfterDispose_ReturnsFalse()
    {
        using var ms = new MemoryStream();
        var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        Assert.True(xz.CanWrite);
        xz.Dispose();
        Assert.False(xz.CanWrite);
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        using var ms = new MemoryStream();
        var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        xz.Write(Encoding.UTF8.GetBytes("data"));
        xz.Dispose();
        xz.Dispose(); // Should not throw
    }

    [Fact]
    public void SetPosition_ThrowsNotSupported()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => xz.Position = 0);
    }

    [Fact]
    public void Write_EmptySpan_DoesNothing()
    {
        using var ms = new MemoryStream();
        using var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true);
        xz.Write(ReadOnlySpan<byte>.Empty);
    }
}

public class XZDecompressStreamEdgeCaseTests
{
    [Fact]
    public void Constructor_SingleArg_Works()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("test"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();
        xz.CopyTo(result);
        Assert.Equal("test", Encoding.UTF8.GetString(result.ToArray()));
    }

    [Fact]
    public void Constructor_WithBufferSize_Works()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("buffer size test"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, bufferSize: 4096, leaveOpen: true);
        using var result = new MemoryStream();
        xz.CopyTo(result);
        Assert.Equal("buffer size test", Encoding.UTF8.GetString(result.ToArray()));
    }

    [Fact]
    public void Constructor_NullStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new XZDecompressStream(null!));
    }

    [Fact]
    public void Constructor_NonReadableStream_ThrowsArgument()
    {
        // FileStream opened for write-only would work, but let's use a simpler approach
        using var writeOnly = new WriteOnlyStream();
        Assert.Throws<ArgumentException>(() => new XZDecompressStream(writeOnly));
    }

    [Fact]
    public void Read_NullBuffer_ThrowsArgumentNull()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<ArgumentNullException>(() => xz.Read(null!, 0, 0));
    }

    [Fact]
    public void Read_NegativeOffset_ThrowsArgumentOutOfRange()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => xz.Read(new byte[10], -1, 1));
    }

    [Fact]
    public void Read_NegativeCount_ThrowsArgumentOutOfRange()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => xz.Read(new byte[10], 0, -1));
    }

    [Fact]
    public void Read_OffsetPlusCountExceedsLength_ThrowsArgument()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<ArgumentException>(() => xz.Read(new byte[10], 5, 10));
    }

    [Fact]
    public void Read_ZeroCount_ReturnsZero()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Equal(0, xz.Read(new byte[10], 0, 0));
    }

    [Fact]
    public void Read_SpanOverload_Works()
    {
        var original = Encoding.UTF8.GetBytes("span read test");
        var compressed = CompressData(original);
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);

        var buf = new byte[1024];
        int totalRead = 0;
        int read;
        while ((read = xz.Read(buf.AsSpan(totalRead))) > 0)
            totalRead += read;

        Assert.Equal(original, buf.AsSpan(0, totalRead).ToArray());
    }

    [Fact]
    public void Read_AfterDispose_ThrowsObjectDisposed()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        var xz = new XZDecompressStream(ms, leaveOpen: true);
        xz.Dispose();
        Assert.Throws<ObjectDisposedException>(() => xz.Read(new byte[10], 0, 10));
    }

    [Fact]
    public void ReadSpan_AfterDispose_ThrowsObjectDisposed()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        var xz = new XZDecompressStream(ms, leaveOpen: true);
        xz.Dispose();
        Assert.Throws<ObjectDisposedException>(() => xz.Read(new byte[10].AsSpan()));
    }

    [Fact]
    public void CanRead_AfterDispose_ReturnsFalse()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.True(xz.CanRead);
        xz.Dispose();
        Assert.False(xz.CanRead);
    }

    [Fact]
    public void Flush_DoesNotThrow()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        xz.Flush(); // No-op, should not throw
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        var xz = new XZDecompressStream(ms, leaveOpen: true);
        xz.Dispose();
        xz.Dispose(); // Should not throw
    }

    [Fact]
    public void SetPosition_ThrowsNotSupported()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => xz.Position = 0);
    }

    [Fact]
    public void GetPosition_ThrowsNotSupported()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => _ = xz.Position);
    }

    [Fact]
    public void CanWrite_ReturnsFalse()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.False(xz.CanWrite);
    }

    [Fact]
    public void Length_ThrowsNotSupported()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => _ = xz.Length);
    }

    [Fact]
    public void Write_ThrowsNotSupported()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => xz.Write(new byte[1], 0, 1));
    }

    [Fact]
    public void Seek_ThrowsNotSupported()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => xz.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void SetLength_ThrowsNotSupported()
    {
        var compressed = CompressData(Encoding.UTF8.GetBytes("x"));
        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms, leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => xz.SetLength(0));
    }

    [Fact]
    public void Read_SmallBuffer_DrainBufferedBytes()
    {
        // Compress data larger than 1 byte so the decoder produces multiple output bytes
        // then read with a 1-byte buffer to exercise the _decodedCount buffering path
        var original = Encoding.UTF8.GetBytes("This is enough data to have buffered decoded bytes remaining.");
        var compressed = CompressData(original);

        using var ms = new MemoryStream(compressed);
        using var xz = new XZDecompressStream(ms);
        using var result = new MemoryStream();

        // Read 1 byte at a time to ensure we exercise the _decodedCount > 0 drain path
        var buf = new byte[1];
        int read;
        while ((read = xz.Read(buf, 0, 1)) > 0)
            result.Write(buf, 0, read);

        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public void Decompress_ChunkedInnerStream_ExercisesInnerStreamExhausted()
    {
        // Compress enough data that the output is large, then feed compressed data
        // through a ChunkedReadStream that returns only small chunks per Read call.
        // This forces the decoder to exhaust its input mid-decode and hit the bytesRead==0 path.
        var original = GenerateTestData(200_000);
        var compressed = CompressData(original);

        using var chunked = new ChunkedReadStream(compressed, chunkSize: 64);
        using var xz = new XZDecompressStream(chunked);
        using var result = new MemoryStream();
        xz.CopyTo(result);

        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public void Decompress_TruncatedData_ThrowsXZExceptionAfterReadingAll()
    {
        // Compress valid data, then truncate the compressed output (remove the xz footer).
        // The decoder must exhaust the inner stream (bytesRead == 0) before discovering
        // the stream is incomplete, exercising that code path.
        var original = GenerateTestData(50_000);
        var compressed = CompressData(original);

        // Truncate by removing more than enough for the footer (footer = 12 bytes, index variable)
        var truncated = new byte[compressed.Length - 24];
        Array.Copy(compressed, truncated, truncated.Length);

        using var ms = new MemoryStream(truncated);
        using var xz = new XZDecompressStream(ms);

        Assert.Throws<XZException>(() =>
        {
            var buf = new byte[1024];
            int read;
            do
            {
                read = xz.Read(buf, 0, buf.Length);
            } while (read > 0);
        });
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

    private static byte[] CompressData(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var xz = new XZCompressStream(ms, new XZCompressOptions(), leaveOpen: true))
        {
            xz.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// A stream that is write-only (CanRead = false) for testing constructor validation.
    /// </summary>
    private sealed class WriteOnlyStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    /// <summary>
    /// A read stream that returns data in small fixed-size chunks to simulate slow or partial reads.
    /// </summary>
    private sealed class ChunkedReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private int _position;

        public ChunkedReadStream(byte[] data, int chunkSize)
        {
            _data = data;
            _chunkSize = chunkSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int remaining = _data.Length - _position;
            if (remaining <= 0) return 0;
            int toRead = Math.Min(Math.Min(count, _chunkSize), remaining);
            Array.Copy(_data, _position, buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
