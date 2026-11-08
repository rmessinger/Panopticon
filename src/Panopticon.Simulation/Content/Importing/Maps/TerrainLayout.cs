#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>
/// Terrain grid: lattice spacing, whole-map lattice point counts, chunk grid, border, source
/// transform, nodata policy, terrain borders, and chunks. Lattice counts are border-inclusive
/// whole-map point counts; chunks tile the lattice with one shared edge point between neighbours.
/// Chunks and terrain borders are copied at construction.
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
        int heightSectionVersion,
        uint mapArea,
        IReadOnlyList<MapTerrainBorder> terrainBorders,
        IReadOnlyList<Chunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(terrainBorders);
        SampleSpacing = sampleSpacing;
        LatticeWidth = latticeWidth;
        LatticeHeight = latticeHeight;
        ChunksX = chunksX;
        ChunksY = chunksY;
        Border = border;
        GeospatialTransform = geospatialTransform;
        NodataPolicy = nodataPolicy;
        HeightSectionVersion = heightSectionVersion;
        MapArea = mapArea;
        TerrainBorders = terrainBorders.ToArray();
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

    /// <summary>Version of the legacy HeightMapData section this layout was read from.</summary>
    public int HeightSectionVersion { get; init; }

    /// <summary>Whole-map lattice point count carried by the legacy HeightMapData section.</summary>
    public uint MapArea { get; init; }

    /// <summary>Defensive copy of the legacy terrain border rectangles in lattice coordinates.</summary>
    public IReadOnlyList<MapTerrainBorder> TerrainBorders { get; init; }

    public IReadOnlyList<Chunk> Chunks { get; init; }
}
