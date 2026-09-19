using System.Text;

namespace ZCS.XZ.Tests;

public class XZChecksumTests
{
    // The standard "check" string used by the CRC catalogue for both algorithms.
    private static readonly byte[] CheckVector = Encoding.ASCII.GetBytes("123456789");

    [Fact]
    public void Crc32_MatchesKnownVector()
    {
        // CRC-32/ISO-HDLC of "123456789".
        Assert.Equal(0xCBF43926u, XZChecksum.Crc32(CheckVector));
    }

    [Fact]
    public void Crc64_MatchesKnownVector()
    {
        // CRC-64/XZ (ECMA-182, reflected) of "123456789".
        Assert.Equal(0x995DC9BBDF1939FAul, XZChecksum.Crc64(CheckVector));
    }

    [Fact]
    public void Crc32_EmptyData_IsZero()
    {
        Assert.Equal(0u, XZChecksum.Crc32(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Crc64_EmptyData_IsZero()
    {
        Assert.Equal(0ul, XZChecksum.Crc64(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Crc32_SeedChainsAcrossCalls()
    {
        byte[] data = Encoding.ASCII.GetBytes("the quick brown fox jumps over the lazy dog");

        uint oneShot = XZChecksum.Crc32(data);

        uint chained = XZChecksum.Crc32(data.AsSpan(0, 10));
        chained = XZChecksum.Crc32(data.AsSpan(10), chained);

        Assert.Equal(oneShot, chained);
    }

    [Fact]
    public void Crc64_SeedChainsAcrossCalls()
    {
        byte[] data = Encoding.ASCII.GetBytes("the quick brown fox jumps over the lazy dog");

        ulong oneShot = XZChecksum.Crc64(data);

        ulong chained = XZChecksum.Crc64(data.AsSpan(0, 10));
        chained = XZChecksum.Crc64(data.AsSpan(10), chained);

        Assert.Equal(oneShot, chained);
    }

    [Fact]
    public void Crc32_DetectsSingleBitChange()
    {
        var data = new byte[1024];
        new Random(99).NextBytes(data);

        uint before = XZChecksum.Crc32(data);
        data[512] ^= 0x01;
        uint after = XZChecksum.Crc32(data);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Crc64_DetectsSingleBitChange()
    {
        var data = new byte[1024];
        new Random(99).NextBytes(data);

        ulong before = XZChecksum.Crc64(data);
        data[512] ^= 0x01;
        ulong after = XZChecksum.Crc64(data);

        Assert.NotEqual(before, after);
    }
}
