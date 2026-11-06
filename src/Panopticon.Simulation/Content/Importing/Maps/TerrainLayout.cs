#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>
/// Terrain grid: lattice spacing, whole-map lattice point counts, chunk grid, border, source
/// transform, nodata policy, and chunks. Lattice counts are border-inclusive whole-map point
/// counts; chunks tile the lattice with one shared edge point between neighbours. Chunks are
/// copied at construction.
/// </summary>
public sealed record TerrainLayout
{
    public TerrainLayout(
        MapQuantity sampleSpacing,
        int latticeWidth,
        int latticeHeight,
        int chunksX,
        int chunksY,
        int border,
        GeospatialTransform geospatialTransform,
        MapNodataPolicy nodataPolicy,
        IReadOnlyList<Chunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        SampleSpacing = sampleSpacing;
        LatticeWidth = latticeWidth;
        LatticeHeight = latticeHeight;
        ChunksX = chunksX;
        ChunksY = chunksY;
        Border = border;
        GeospatialTransform = geospatialTransform;
        NodataPolicy = nodataPolicy;
        Chunks = chunks.ToArray();
    }

    public MapQuantity SampleSpacing { get; init; }

    public int LatticeWidth { get; init; }

    public int LatticeHeight { get; init; }

    public int ChunksX { get; init; }

    public int ChunksY { get; init; }

    public int Border { get; init; }

    public GeospatialTransform GeospatialTransform { get; init; }

    public MapNodataPolicy NodataPolicy { get; init; }

    public IReadOnlyList<Chunk> Chunks { get; init; }
}
