namespace ZCS.XZ;

/// <summary>
/// Integrity check type
/// </summary>
public enum LzmaCheck
{
    /// <summary>
    /// No Check is calculated.
    ///
    /// Size of the Check field: 0 bytes
    /// </summary>
    None = 0,
    /// <summary>
    /// CRC32 using the polynomial from the IEEE 802.3 standard
    ///
    /// Size of the Check field: 4 bytes
    /// </summary>
    Crc32 = 1,
    /// <summary>
    /// CRC64 using the polynomial from the ECMA-182 standard
    ///
    /// Size of the Check field: 8 bytes
    /// </summary>
    Crc64 = 4,
    /// <summary>
    /// SHA-256
    ///
    /// Size of the Check field: 32 bytes
    /// </summary>
    Sha256 = 10,
}