#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Terrain grid and chunk layout metadata in the package manifest.</summary>
public sealed record MapPackageTerrainLayout
{
    [JsonConstructor]
    public MapPackageTerrainLayout(
        MapPackageQuantity? sampleSpacing = null,
        int latticeWidth = 0,
        int latticeHeight = 0,
        int chunksX = 0,
        int chunksY = 0,
        int border = 0,
        MapPackageGeospatialTransform? geospatialTransform = null,
        string? nodataPolicy = null,
        int heightSectionVersion = 0,
        uint mapArea = 0,
        IReadOnlyList<MapPackageTerrainBorder>? terrainBorders = null)
    {
        SampleSpacing = sampleSpacing!;
        LatticeWidth = latticeWidth;
        LatticeHeight = latticeHeight;
        ChunksX = chunksX;
        ChunksY = chunksY;
        Border = border;
        GeospatialTransform = geospatialTransform;
        NodataPolicy = nodataPolicy ?? string.Empty;
        HeightSectionVersion = heightSectionVersion;
        MapArea = mapArea;
        TerrainBorders = terrainBorders?.ToArray() ?? [];
    }

    public MapPackageQuantity SampleSpacing { get; init; }

    public int LatticeWidth { get; init; }

    public int LatticeHeight { get; init; }

    public int ChunksX { get; init; }

    public int ChunksY { get; init; }

    public int Border { get; init; }

    public MapPackageGeospatialTransform? GeospatialTransform { get; init; }

    public string NodataPolicy { get; init; }

    public int HeightSectionVersion { get; init; }

    public uint MapArea { get; init; }

    public IReadOnlyList<MapPackageTerrainBorder> TerrainBorders { get; init; }

    public static MapPackageTerrainLayout FromModel(TerrainLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new MapPackageTerrainLayout(
            MapPackageQuantity.FromMapQuantity(layout.SampleSpacing),
            layout.LatticeWidth,
            layout.LatticeHeight,
            layout.ChunksX,
            layout.ChunksY,
            layout.Border,
            layout.GeospatialTransform is null ? null : MapPackageGeospatialTransform.FromModel(layout.GeospatialTransform),
            layout.NodataPolicy.ToString(),
            layout.HeightSectionVersion,
            layout.MapArea,
            layout.TerrainBorders.Select(MapPackageTerrainBorder.FromModel).ToArray());
    }
}
