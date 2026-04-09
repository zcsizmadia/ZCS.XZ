namespace ZCS.XZ.Tests;

public class XZDecompressStreamTests
{
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
}
