#nullable enable

using System;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Per-cell bits of <see cref="MaskLayer"/>; bit positions equal the Geospatial project's TerrainMaskFlags.</summary>
[Flags]
public enum MapMaskFlags : byte
{
    None = 0,

    /// <summary>Blocks movement.</summary>
    Impassable = 1,

    /// <summary>Blocks building.</summary>
    Unbuildable = 2,
}
