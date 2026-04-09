namespace ZCS.XZ;

/// <summary>
/// Specifies the XZ/LZMA2 compression level (0–9).
/// Higher levels produce smaller output but require more CPU time and memory.
/// </summary>
/// <remarks>
/// These values correspond directly to the liblzma preset levels (0–9)
/// passed to <c>lzma_easy_encoder</c> or <c>lzma_stream_encoder_mt</c>.
/// The memory requirements increase significantly at levels 7–9.
/// </remarks>
public enum XZCompressionLevel
{
    /// <summary>No compression — data is stored as-is. Fastest but largest output.</summary>
    None = 0,

    /// <summary>Level 1 — fastest compression with the lowest compression ratio.</summary>
    Fastest = 1,

    /// <summary>Level 2 — slightly better compression ratio than <see cref="Fastest"/>.</summary>
    Level2 = 2,

    /// <summary>Level 3 — balanced toward speed.</summary>
    Level3 = 3,

    /// <summary>Level 4 — moderate compression.</summary>
    Level4 = 4,

    /// <summary>Level 5 — balanced between speed and compression ratio.</summary>
    Level5 = 5,

    /// <summary>
    /// Level 6 — the default compression level. Provides a good balance
    /// between compression ratio, speed, and memory usage for most workloads.
    /// </summary>
    Default = 6,

    /// <summary>Level 7 — higher compression ratio at the cost of more CPU and memory.</summary>
    Level7 = 7,

    /// <summary>Level 8 — very high compression. Significantly higher memory usage.</summary>
    Level8 = 8,

    /// <summary>Level 9 — maximum compression ratio. Slowest and most memory-intensive.</summary>
    Level9 = 9,

    /// <summary>Alias for <see cref="Level9"/> — the highest available compression level.</summary>
    Maximum = Level9,
}