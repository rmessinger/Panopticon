#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Terrain grid: lattice spacing, chunk grid, border, source transform, nodata policy, and chunks. Chunks are copied at construction.</summary>
public sealed record TerrainLayout
{
    public TerrainLayout(
        MapQuantity sampleSpacing,
        int chunksX,
        int chunksY,
        int border,
        GeospatialTransform geospatialTransform,
        MapNodataPolicy nodataPolicy,
        IReadOnlyList<Chunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        SampleSpacing = sampleSpacing;
        ChunksX = chunksX;
        ChunksY = chunksY;
        Border = border;
        GeospatialTransform = geospatialTransform;
        NodataPolicy = nodataPolicy;
        Chunks = chunks.ToArray();
    }

    public MapQuantity SampleSpacing { get; init; }

    public int ChunksX { get; init; }

    public int ChunksY { get; init; }

    public int Border { get; init; }

    public GeospatialTransform GeospatialTransform { get; init; }

    public MapNodataPolicy NodataPolicy { get; init; }

    public IReadOnlyList<Chunk> Chunks { get; init; }
}
