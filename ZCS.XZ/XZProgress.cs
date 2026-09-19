namespace ZCS.XZ;

/// <summary>
/// A snapshot of how much data a compression or decompression stream has processed.
/// </summary>
/// <param name="BytesIn">Number of input bytes consumed so far.</param>
/// <param name="BytesOut">Number of output bytes produced so far.</param>
/// <remarks>
/// Obtained from <see cref="XZCompressStream.GetProgress"/> or
/// <see cref="XZDecompressStream.GetProgress"/>. Unlike the stream's own byte counters,
/// these values remain accurate when multiple threads are in use, where work is buffered
/// across threads and not yet reflected in the totals.
/// </remarks>
public readonly record struct XZProgress(ulong BytesIn, ulong BytesOut);
