#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Terrain grid: lattice spacing, chunk grid, border, source transform, nodata policy, and chunks.</summary>
public sealed record TerrainLayout(
    MapQuantity SampleSpacing,
    int ChunksX,
    int ChunksY,
    int Border,
    GeospatialTransform GeospatialTransform,
    MapNodataPolicy NodataPolicy,
    IReadOnlyList<Chunk> Chunks);
