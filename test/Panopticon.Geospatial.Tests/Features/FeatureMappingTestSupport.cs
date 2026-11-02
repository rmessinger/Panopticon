#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Projection;
using Xunit;

namespace Panopticon.Geospatial.Tests.Features;

/// <summary>
/// Shared fixtures for feature mapping tests: a chunk grid at metersPerGameUnit 1, an
/// analytic bilinear-exact lattice surface in game units, synthetic chunks, and local
/// feature builders.
/// </summary>
internal static class FeatureMappingTestSupport
{
    public static readonly CoordinateSystemId Tm4 = CoordinateSystemId.TransverseMercator(4d, 1d, 0d, 0d);

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    /// <summary>
    /// A lattice surface in game units: the product of one linear factor per axis with
    /// dyadic coefficients, so every lattice value, weight, and partial sum is an exact
    /// double and float32 and expected heights are bit-reproducible.
    /// </summary>
    public static double SurfaceHeight(double latticeX, double latticeY)
    {
        return (10d + (0.5d * latticeX)) * (2d + (0.25d * latticeY));
    }

    public static TerrainChunk FlatChunk(int chunkX, int chunkY, float height = 5f)
    {
        var stored = ChunkLayout.StoredPointsPerChunkAxis;
        var heights = new float[stored * stored];
        Array.Fill(heights, height);

        return new TerrainChunk(chunkX, chunkY, heights);
    }

    public static LocalMapGrid CreateGrid(int chunksX = 2, int chunksY = 2)
    {
        var grid = LocalMapGrid.Create(
            ProjectedSquare(100000d, 6000000d, chunksX * (double)ChunkLayout.ChunkSpanGameUnits, chunksY * (double)ChunkLayout.ChunkSpanGameUnits),
            Tm4,
            CreateManifest(),
            Transformer,
            new GeoDiagnosticBag());
        Assert.NotNull(grid);
        Assert.Equal(chunksX, grid!.ChunksX);
        Assert.Equal(chunksY, grid.ChunksY);

        return grid;
    }

    public static TerrainChunk BuildChunk(int chunkX, int chunkY)
    {
        var stored = ChunkLayout.StoredPointsPerChunkAxis;
        var heights = new float[stored * stored];
        for (var sy = 0; sy < stored; sy++)
        {
            for (var sx = 0; sx < stored; sx++)
            {
                heights[(sy * stored) + sx] = (float)SurfaceHeight(
                    (chunkX * ChunkLayout.IntervalsPerChunk) + sx - 1,
                    (chunkY * ChunkLayout.IntervalsPerChunk) + sy - 1);
            }
        }

        return new TerrainChunk(chunkX, chunkY, heights);
    }

    public static TerrainChunkHeightSampler FullSampler(LocalMapGrid grid)
    {
        return new TerrainChunkHeightSampler(grid, static (cx, cy) => BuildChunk(cx, cy));
    }

    public static ITerrainHeightSampler ConstantSampler(double gameZ)
    {
        return new DelegateHeightSampler((_, _) => (true, gameZ));
    }

    public static ITerrainHeightSampler FailingSampler()
    {
        return new DelegateHeightSampler((_, _) => (false, 0d));
    }

    public static LocalFeature PointPart(
        double gameX,
        double gameY,
        string sourceId = "vectors",
        string featureId = "p1",
        double? altitudeMeters = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var source = SourceFeature(sourceId, featureId, attributes);
        return new LocalFeature(
            new GeoPointGeometry(new GeoPosition(new Vector2d(gameX, gameY), altitudeMeters)),
            source,
            PartId(sourceId, featureId, 0));
    }

    public static LocalFeature LinePart(
        IReadOnlyList<(double X, double Y)> positions,
        string sourceId = "vectors",
        string featureId = "l1",
        IReadOnlyDictionary<string, string>? attributes = null,
        int partIndex = 0)
    {
        var vertices = new GeoPosition[positions.Count];
        for (var i = 0; i < positions.Count; i++)
        {
            vertices[i] = new GeoPosition(new Vector2d(positions[i].X, positions[i].Y));
        }

        return new LocalFeature(new GeoLineString(vertices), SourceFeature(sourceId, featureId, attributes), PartId(sourceId, featureId, partIndex));
    }

    public static LocalFeature PolygonPart(
        double minX,
        double minY,
        double maxX,
        double maxY,
        string sourceId = "vectors",
        string featureId = "poly1",
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var ring = new GeoRing(
        [
            new GeoPosition(new Vector2d(minX, minY)),
            new GeoPosition(new Vector2d(maxX, minY)),
            new GeoPosition(new Vector2d(maxX, maxY)),
            new GeoPosition(new Vector2d(minX, maxY)),
            new GeoPosition(new Vector2d(minX, minY)),
        ]);

        return new LocalFeature(new GeoPolygon(ring), SourceFeature(sourceId, featureId, attributes), PartId(sourceId, featureId, 0));
    }

    public static GeospatialFeature SourceFeature(
        string sourceId,
        string featureId,
        IReadOnlyDictionary<string, string>? attributes)
    {
        return new GeospatialFeature(
            sourceId,
            featureId,
            new GeoPointGeometry(new GeoPosition(new Vector2d(6.125d, 49.75d), 30d)),
            CoordinateSystemId.Wgs84,
            attributes);
    }

    public static string PartId(string sourceId, string featureId, int partIndex)
    {
        return sourceId + "/" + featureId + "#" + partIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static GeospatialImportManifest CreateManifest()
    {
        return new GeospatialImportManifest
        {
            Sources =
            [
                new ManifestSource("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent),
                new ManifestSource("dem", ManifestSourceKind.Geotiff, "dem.tif", ManifestSourceRole.Elevation, "EGM96"),
            ],
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: 4d),
            MetersPerGameUnit = 1d,
            DefaultMaterial = "grass",
            MaxScaleError = 0.001d,
        };
    }

    private static GeoPolygon ProjectedSquare(double west, double south, double width, double height)
    {
        return new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(west, south)),
                new GeoPosition(new Vector2d(west + width, south)),
                new GeoPosition(new Vector2d(west + width, south + height)),
                new GeoPosition(new Vector2d(west, south + height)),
                new GeoPosition(new Vector2d(west, south)),
            ]));
    }

    private sealed class DelegateHeightSampler(Func<double, double, (bool Succeeded, double GameZ)> sample) : ITerrainHeightSampler
    {
        public bool TrySample(double gameX, double gameY, out double gameZ)
        {
            var (succeeded, value) = sample(gameX, gameY);
            gameZ = succeeded ? value : 0d;

            return succeeded;
        }
    }
}
