using static ZCS.XZ.LibLzmaNativeMethods;

namespace ZCS.XZ;

/// <summary>
/// Checksum functions exposed by liblzma.
/// </summary>
/// <remarks>
/// These are the same implementations liblzma uses for <c>.xz</c> integrity checks, including
/// its hardware-accelerated paths. <see cref="Crc64(ReadOnlySpan{byte}, ulong)"/> in particular
/// has no equivalent in the base class library.
/// </remarks>
public static class XZChecksum
{
    /// <summary>
    /// Computes a CRC32 checksum using the IEEE 802.3 polynomial.
    /// </summary>
    /// <param name="data">The data to checksum.</param>
    /// <param name="seed">
    /// A running CRC to continue from, allowing a checksum to be built up over several
    /// calls. Pass 0 (the default) to start a new checksum.
    /// </param>
    /// <returns>The CRC32 value.</returns>
    /// <example>
    /// <code>
    /// uint crc = XZChecksum.Crc32("hello"u8);
    /// </code>
    /// </example>
    public static unsafe uint Crc32(ReadOnlySpan<byte> data, uint seed = 0)
    {
        fixed (byte* p = data)
        {
            return lzma_crc32(p, (nuint)data.Length, seed);
        }
    }

    /// <summary>
    /// Computes a CRC64 checksum using the ECMA-182 polynomial, the check type
    /// <see cref="XZCompressStream"/> writes by default.
    /// </summary>
    /// <param name="data">The data to checksum.</param>
    /// <param name="seed">
    /// A running CRC to continue from, allowing a checksum to be built up over several
    /// calls. Pass 0 (the default) to start a new checksum.
    /// </param>
    /// <returns>The CRC64 value.</returns>
    public static unsafe ulong Crc64(ReadOnlySpan<byte> data, ulong seed = 0)
    {
        fixed (byte* p = data)
        {
            return lzma_crc64(p, (nuint)data.Length, seed);
        }
    }
}
