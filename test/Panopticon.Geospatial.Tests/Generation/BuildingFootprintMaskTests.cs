#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Tests.Features;
using Xunit;

namespace Panopticon.Geospatial.Tests.Generation;

public class BuildingFootprintMaskTests
{
    private const double OriginEasting = 100000d;

    private const double OriginNorthing = 6000000d;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    [Fact]
    public void ThirtyMeterDualRuleFeatureThatSetsCellsIsNotCounted()
    {
        var grid = FeatureMappingTestSupport.CreateGrid();
        var features = MapRegions(grid, [Square("tower", 100d, 100d, 130d, 130d)], DualRules());

        var masks = GenerateAll(grid, features);

        Assert.Equal(0L, masks.Report.ZeroCellMappedPolygonFeatureCount);

        var chunk = masks.Chunks.Single(c => c.ChunkX == 0 && c.ChunkY == 0);
        var both = TerrainMaskFlags.Impassable | TerrainMaskFlags.Unbuildable;

        // Sample (x, y) sits at game (10x, 10y); the square spans 100..130 on both axes.
        for (var y = 11; y <= 12; y++)
        {
            for (var x = 11; x <= 12; x++)
            {
                Assert.Equal(both, chunk.GetFlags(x, y));
            }
        }

        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(0, 0));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(5, 5));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(9, 11));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(15, 15));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(25, 25));
    }

    [Fact]
    public void TwoMeterDualRuleFeatureCountsOnceAcrossRulesAndChunks()
    {
        var grid = FeatureMappingTestSupport.CreateGrid();
        var features = MapRegions(grid, [Square("speck", 11d, 11d, 13d, 13d)], DualRules());

        var report = GenerateAll(grid, features).Report;

        Assert.Equal(1L, report.ZeroCellMappedPolygonFeatureCount);
    }

    [Fact]
    public void FeatureWhoseSampleCellsLieOnlyInItsHoleIsZeroCell()
    {
        // The outer ring holds only the sample at (20, 20), which the hole removes.
        var grid = FeatureMappingTestSupport.CreateGrid();
        var ring = new GeospatialFeature(
            "vectors",
            "ringed",
            new GeoPolygon(ProjectedRing(12d, 12d, 28d, 28d), [ProjectedRing(15d, 15d, 25d, 25d)]),
            FeatureMappingTestSupport.Tm4);
        var features = MapRegions(grid, [ring], WaterRules());

        var report = GenerateAll(grid, features).Report;

        Assert.Equal(1L, report.ZeroCellMappedPolygonFeatureCount);
    }

    [Fact]
    public void FeatureOutsideTheMapIsDroppedBeforeCounting()
    {
        var grid = FeatureMappingTestSupport.CreateGrid();
        var features = MapRegions(
            grid,
            [Square("offmap", 2600d, 100d, 2700d, 200d), Square("speck", 11d, 11d, 13d, 13d)],
            WaterRules());

        var report = GenerateAll(grid, features).Report;

        Assert.Equal(1L, report.ZeroCellMappedPolygonFeatureCount);
    }

    [Fact]
    public void CoverInALaterChunkKeepsFeatureNonZero()
    {
        // The bridge spans x 1285..1295 and sets only the sample at x 1290, which lies in
        // chunk (1, 0); chunk (0, 0) is generated first and sees it as uncovered.
        var grid = FeatureMappingTestSupport.CreateGrid();
        var features = MapRegions(
            grid,
            [Square("bridge", 1285d, 100d, 1295d, 120d), Square("speck", 11d, 11d, 13d, 13d)],
            WaterRules());

        var report = GenerateAll(grid, features).Report;

        Assert.Equal(1L, report.ZeroCellMappedPolygonFeatureCount);
    }

    [Fact]
    public void MultiGeometryPartsShareOneIdentity()
    {
        // "mixed" is covered by its first part, so only "twins" counts, and its two
        // zero-cell parts count once.
        var grid = FeatureMappingTestSupport.CreateGrid();
        var sources = new[]
        {
            new GeospatialFeature(
                "vectors",
                "mixed",
                new GeoMultiGeometry([ProjectedPolygon(100d, 100d, 130d, 130d), ProjectedPolygon(11d, 11d, 13d, 13d)]),
                FeatureMappingTestSupport.Tm4),
            new GeospatialFeature(
                "vectors",
                "twins",
                new GeoMultiGeometry([ProjectedPolygon(11d, 11d, 13d, 13d), ProjectedPolygon(51d, 51d, 53d, 53d)]),
                FeatureMappingTestSupport.Tm4),
        };
        var features = MapRegions(grid, sources, WaterRules());

        var report = GenerateAll(grid, features).Report;

        Assert.Equal(1L, report.ZeroCellMappedPolygonFeatureCount);
    }

    [Fact]
    public void EmptyMaskReportsZero()
    {
        var grid = FeatureMappingTestSupport.CreateGrid();

        Assert.Equal(0L, new TerrainMaskReport().ZeroCellMappedPolygonFeatureCount);
        Assert.Equal(0L, GenerateAll(grid, null).Report.ZeroCellMappedPolygonFeatureCount);
        Assert.Equal(0L, GenerateAll(grid, []).Report.ZeroCellMappedPolygonFeatureCount);
    }

    private static FeatureMappingRule[] WaterRules()
    {
        return [new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon)];
    }

    private static FeatureMappingRule[] DualRules()
    {
        return
        [
            new("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, Passable: false),
            new("vectors", ManifestFeatureTarget.Buildability, ManifestFeatureGeometry.Polygon, Buildable: false),
        ];
    }

    private static IReadOnlyList<GeneratedFeature> MapRegions(
        LocalMapGrid grid,
        IReadOnlyList<GeospatialFeature> sources,
        IReadOnlyList<FeatureMappingRule> rules)
    {
        var diagnostics = new GeoDiagnosticBag();
        var local = FeatureClipper.Clip(sources, grid, Transformer, diagnostics);
        var mapped = FeatureMapper.Map(local, rules, FeatureMappingTestSupport.ConstantSampler(0d), diagnostics);

        Assert.False(diagnostics.HasErrors);

        return mapped;
    }

    private static GeneratedMasks GenerateAll(LocalMapGrid grid, IReadOnlyList<GeneratedFeature>? features)
    {
        var report = new TerrainMaskReport();
        var diagnostics = new GeoDiagnosticBag();
        var chunks = new List<TerrainMaskChunk>();

        for (var cy = 0; cy < grid.ChunksY; cy++)
        {
            for (var cx = 0; cx < grid.ChunksX; cx++)
            {
                var chunk = TerrainMaskGenerator.Generate(
                    grid,
                    FeatureMappingTestSupport.FlatChunk(cx, cy),
                    features,
                    report: report,
                    diagnostics: diagnostics);

                Assert.NotNull(chunk);
                chunks.Add(chunk!);
            }
        }

        Assert.False(diagnostics.HasErrors);

        return new GeneratedMasks(report, chunks);
    }

    private sealed record GeneratedMasks(TerrainMaskReport Report, IReadOnlyList<TerrainMaskChunk> Chunks);

    private static GeospatialFeature Square(string featureId, double minX, double minY, double maxX, double maxY)
    {
        return new GeospatialFeature("vectors", featureId, ProjectedPolygon(minX, minY, maxX, maxY), FeatureMappingTestSupport.Tm4);
    }

    private static GeoPolygon ProjectedPolygon(double minX, double minY, double maxX, double maxY)
    {
        return new GeoPolygon(ProjectedRing(minX, minY, maxX, maxY));
    }

    private static GeoRing ProjectedRing(double minX, double minY, double maxX, double maxY)
    {
        return new GeoRing(
        [
            new GeoPosition(new Vector2d(OriginEasting + minX, OriginNorthing + minY)),
            new GeoPosition(new Vector2d(OriginEasting + maxX, OriginNorthing + minY)),
            new GeoPosition(new Vector2d(OriginEasting + maxX, OriginNorthing + maxY)),
            new GeoPosition(new Vector2d(OriginEasting + minX, OriginNorthing + maxY)),
            new GeoPosition(new Vector2d(OriginEasting + minX, OriginNorthing + minY)),
        ]);
    }
}
