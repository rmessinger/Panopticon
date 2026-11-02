using System;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Per-sample terrain mask bits (B4b deep dive section 5.1). No other bits are defined;
/// buffers holding anything else are rejected.
/// </summary>
[Flags]
public enum TerrainMaskFlags : byte
{
    None = 0,

    /// <summary>Slope, region rules, or the playable-area exterior marked the sample impassable.</summary>
    Impassable = 1,

    /// <summary>Slope, region rules, or the playable-area exterior marked the sample unbuildable.</summary>
    Unbuildable = 2,
}
