#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;
using Panopticon.Geospatial.Tests.Sampling;
using Xunit;

namespace Panopticon.Geospatial.Tests.Generation;

public class TerrainMaskGeneratorTests
{
    private const double OriginEasting = 100000d;

    private const double OriginNorthing = 6000000d;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private static readonly CoordinateSystemId Tm4 = CoordinateSystemId.TransverseMercator(4d, 1d, 0d, 0d);

    private static readonly TerrainMaskFlags Both = TerrainMaskFlags.Impassable | TerrainMaskFlags.Unbuildable;

    [Fact]
    public void ATenDegreePlaneCarriesNoFlags()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var chunk = TerrainMaskGenerator.Generate(grid!, PlaneTerrain(10d));

        Assert.NotNull(chunk);
        AssertAllCells(chunk!, TerrainMaskFlags.None);
    }

    [Fact]
    public void AThirtyDegreePlaneIsUnbuildableButPassable()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var chunk = TerrainMaskGenerator.Generate(grid!, PlaneTerrain(30d));

        Assert.NotNull(chunk);
        AssertAllCells(chunk!, TerrainMaskFlags.Unbuildable);
    }

    [Fact]
    public void ASteeperThanFortyFiveDegreePlaneIsFullyImpassable()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var chunk = TerrainMaskGenerator.Generate(grid!, PlaneTerrain(60d));

        Assert.NotNull(chunk);
        AssertAllCells(chunk!, Both);
    }

    [Fact]
    public void ChangingTheExaggerationChangesTheSlopeMaskViaTheTerrainGenerator()
    {
        // The plane tan-slope is sqrt(0.25^2 + 0.5^2): 29.2 degrees at exaggeration 1
        // (above unbuildable 15, below impassable 45) and 48.2 degrees at 2 (above
        // both). The chunks come from the actual TerrainChunkGenerator, so the heights
        // already carry the vertical conversion the mask generator must not reapply: a
        // second application would push the calm mask past 45 degrees too.
        var calm = MaskFromGeneratedTerrain(new VerticalPolicy(50d, 1d));
        var steep = MaskFromGeneratedTerrain(new VerticalPolicy(50d, 2d));

        AssertAllCells(calm, TerrainMaskFlags.Unbuildable);
        AssertAllCells(steep, Both);
    }

    [Fact]
    public void WaterRegionsSetBothBitsInsideTheirPolygonOnly()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, Square(100, 100, 300, 300), ruleIndex: 0)],
            diagnostics: diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);

        // The square spans game units 100..300: lattice 10..30 inclusive on the outer
        // boundary, one step outside it no flag is set.
        Assert.Equal(Both, chunk!.GetFlags(10, 10));
        Assert.Equal(Both, chunk.GetFlags(20, 20));
        Assert.Equal(Both, chunk.GetFlags(30, 30));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(31, 20));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(20, 9));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(0, 0));
    }

    [Fact]
    public void WaterHolesStayExcludedWithUnambiguousBoundaries()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var water = Region(
            GeneratedFeatureKind.Water,
            Square(100, 100, 500, 500, hole: (200, 200, 400, 400)),
            ruleIndex: 0);

        var chunk = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), [water]);

        Assert.NotNull(chunk);

        // Outer ring: boundary at lattice 10 and 50 included. Hole 200..400: its
        // boundary at lattice 20 and 40 and its interior stay excluded.
        Assert.Equal(Both, chunk!.GetFlags(10, 25));
        Assert.Equal(Both, chunk.GetFlags(15, 15));
        Assert.Equal(Both, chunk.GetFlags(50, 50));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(20, 25));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(40, 25));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(25, 25));
        Assert.Equal(Both, chunk.GetFlags(41, 25));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(9, 25));
    }

    [Fact]
    public void AHoleTouchingTheOuterBoundaryExcludesTheTouchSample()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // A diamond hole whose left tip (100, 250) sits exactly on the outer box's left
        // edge: the touch sample is on both boundaries and the hole must win.
        var geometry = new GeoPolygon(
            RingOf((100, 100), (500, 100), (500, 500), (100, 500)),
            [RingOf((100, 250), (200, 350), (300, 250), (200, 150))]);
        var water = Region(GeneratedFeatureKind.Water, geometry, ruleIndex: 0);

        var chunk = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), [water]);

        Assert.NotNull(chunk);

        Assert.Equal(TerrainMaskFlags.None, chunk!.GetFlags(10, 25));
        Assert.Equal(Both, chunk.GetFlags(10, 20));
        Assert.Equal(Both, chunk.GetFlags(10, 30));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(25, 25));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(30, 25));
        Assert.Equal(Both, chunk.GetFlags(31, 25));
    }

    [Fact]
    public void PassabilityRegionsChangeOnlyTheImpassableBit()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // A 30 degree plane: every cell starts unbuildable from slope alone.
        var features = new List<GeneratedFeature>
        {
            Region(GeneratedFeatureKind.Passability, Square(100, 100, 300, 300), ruleIndex: 0, passable: true),
            Region(GeneratedFeatureKind.Passability, Square(400, 100, 600, 300), ruleIndex: 1, passable: false),
        };

        var chunk = TerrainMaskGenerator.Generate(grid!, PlaneTerrain(30d), features);

        Assert.NotNull(chunk);

        // passable=true clears only Impassable: the slope's Unbuildable survives.
        Assert.Equal(TerrainMaskFlags.Unbuildable, chunk!.GetFlags(20, 20));
        Assert.Equal(TerrainMaskFlags.Unbuildable, chunk.GetFlags(10, 10));

        // passable=false sets Impassable beside the slope's Unbuildable.
        Assert.Equal(Both, chunk.GetFlags(50, 20));
        Assert.Equal(Both, chunk.GetFlags(60, 30));

        // Untouched slope between the regions.
        Assert.Equal(TerrainMaskFlags.Unbuildable, chunk.GetFlags(80, 20));
    }

    [Fact]
    public void BuildabilityRegionsChangeOnlyTheUnbuildableBit()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var features = new List<GeneratedFeature>
        {
            Region(GeneratedFeatureKind.Water, Square(100, 100, 300, 300), ruleIndex: 0),
            Region(GeneratedFeatureKind.Buildability, Square(100, 100, 300, 300), ruleIndex: 1, buildable: true),
            Region(GeneratedFeatureKind.Buildability, Square(400, 100, 600, 300), ruleIndex: 2, buildable: false),
        };

        var chunk = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), features);

        Assert.NotNull(chunk);

        // Water set both bits; buildable=true cleared only Unbuildable.
        Assert.Equal(TerrainMaskFlags.Impassable, chunk!.GetFlags(20, 20));
        Assert.Equal(TerrainMaskFlags.Impassable, chunk.GetFlags(30, 30));

        // buildable=false set Unbuildable on flat terrain.
        Assert.Equal(TerrainMaskFlags.Unbuildable, chunk.GetFlags(50, 20));

        // Outside every region: flat terrain keeps no flags.
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(80, 20));
    }

    [Fact]
    public void OverridesFollowGlobalRuleOrderAcrossFeatures()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // The input order opposes the rule order: the water feature (rule 5) is listed
        // first, the passable region (rule 2) second. Global rule order must win over
        // input order: water applies last inside the overlap and re-sets Impassable the
        // passable rule had cleared under an input-order evaluation.
        var features = new List<GeneratedFeature>
        {
            Region(GeneratedFeatureKind.Water, Square(100, 100, 500, 500), ruleIndex: 5),
            Region(GeneratedFeatureKind.Passability, Square(100, 100, 300, 300), ruleIndex: 2, passable: true),
        };

        var chunk = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), features);

        Assert.NotNull(chunk);
        Assert.Equal(Both, chunk!.GetFlags(20, 20));
        Assert.Equal(Both, chunk.GetFlags(30, 30));

        // Water only, beyond the passable region.
        Assert.Equal(Both, chunk.GetFlags(40, 40));

        // No rule reached beyond the water polygon.
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(9, 20));
    }

    [Fact]
    public void EqualRuleIndexesKeepStableInputOrder()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var polygon = Square(100, 100, 300, 300);

        var setThenClear = new List<GeneratedFeature>
        {
            Region(GeneratedFeatureKind.Passability, polygon, ruleIndex: 3, passable: false),
            Region(GeneratedFeatureKind.Passability, polygon, ruleIndex: 3, passable: true),
        };

        var clearThenSet = new List<GeneratedFeature>
        {
            Region(GeneratedFeatureKind.Passability, polygon, ruleIndex: 3, passable: true),
            Region(GeneratedFeatureKind.Passability, polygon, ruleIndex: 3, passable: false),
        };

        var cleared = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), setThenClear);
        var set = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), clearThenSet);

        Assert.NotNull(cleared);
        Assert.NotNull(set);

        // Same rule index: the input order decides which region applied last.
        Assert.Equal(TerrainMaskFlags.None, cleared!.GetFlags(20, 20));
        Assert.Equal(TerrainMaskFlags.Impassable, set!.GetFlags(20, 20));
    }

    [Fact]
    public void NonRegionFeaturesAreIgnored()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        var line = new GeoLineString(
        [
            new GeoPosition(new Vector2d(100, 100)),
            new GeoPosition(new Vector2d(200, 200)),
        ]);
        var point = new GeoPointGeometry(new GeoPosition(new Vector2d(50, 50)));
        var features = new List<GeneratedFeature>
        {
            new(GeneratedFeatureKind.Road, line, "vectors", "road", "vectors/road#0", 0),
            new(GeneratedFeatureKind.Annotation, Square(100, 100, 300, 300), "vectors", "note", "vectors/note#0", -1),
            new(GeneratedFeatureKind.Waypoint, point, "vectors", "wp", "vectors/wp#0", 1, position: new GamePosition(50, 50, 0)),
        };

        var chunk = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), features, diagnostics: diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);
        AssertAllCells(chunk!, TerrainMaskFlags.None);
    }

    [Fact]
    public void PlayableExteriorForcesBothBitsLastOverRegionRules()
    {
        // A 300 x 300 m extent rounds outward to one full chunk: the playable area
        // covers game 0..300 while the chunk spans 0..1280. A passable region over the
        // whole chunk clears slope's Impassable, then the playable exterior sets both
        // bits back outside the playable polygon.
        var grid = CreateGrid(widthMeters: 300d, heightMeters: 300d);
        Assert.NotNull(grid);

        var features = new List<GeneratedFeature>
        {
            Region(GeneratedFeatureKind.Passability, Square(0, 0, 1280, 1280), ruleIndex: 0, passable: true),
        };

        var chunk = TerrainMaskGenerator.Generate(grid!, PlaneTerrain(30d), features);

        Assert.NotNull(chunk);

        // Inside the playable area the region rule survived: Impassable cleared, the
        // slope's Unbuildable kept. The playable boundary itself counts as playable.
        Assert.Equal(TerrainMaskFlags.Unbuildable, chunk!.GetFlags(20, 20));
        Assert.Equal(TerrainMaskFlags.Unbuildable, chunk.GetFlags(30, 20));
        Assert.Equal(TerrainMaskFlags.Unbuildable, chunk.GetFlags(0, 0));

        // Outside the playable area both bits are forced, even though the passable
        // region had just cleared Impassable there.
        Assert.Equal(Both, chunk.GetFlags(31, 20));
        Assert.Equal(Both, chunk.GetFlags(128, 128));
    }

    [Fact]
    public void PlayableAreaHolesForceBothBitsIncludingTheirBoundary()
    {
        var grid = CreateGrid(playableHole: (200, 200, 400, 400));
        Assert.NotNull(grid);

        var chunk = TerrainMaskGenerator.Generate(grid!, FlatTerrain());

        Assert.NotNull(chunk);

        // The hole and its boundary are unplayable; the outer boundary is playable.
        Assert.Equal(Both, chunk!.GetFlags(25, 25));
        Assert.Equal(Both, chunk.GetFlags(20, 25));
        Assert.Equal(Both, chunk.GetFlags(40, 25));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(41, 25));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(0, 0));
        Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(5, 5));
    }

    [Fact]
    public void PlayableRingVertexOrderAndWindingDoNotChangeTheMask()
    {
        // The same playable polygon with the outer ring's first vertex rotated to every
        // corner and the winding reversed: the mask must depend on the covered area, not
        // on ring traversal, and holes must stay excluded in every variant.
        var baselineHash = string.Empty;

        foreach (var (startVertex, reverseWinding) in new[] { (0, false), (1, false), (2, false), (3, false), (0, true), (2, true) })
        {
            var grid = CreateGrid(RotatedExtent(1280d, 1280d, (200, 200, 400, 400), startVertex, reverseWinding));
            Assert.NotNull(grid);

            var chunk = TerrainMaskGenerator.Generate(grid!, FlatTerrain());

            Assert.NotNull(chunk);

            // Interior playable; hole interior and hole boundary unplayable; the outer
            // boundary itself playable.
            Assert.Equal(TerrainMaskFlags.None, chunk!.GetFlags(5, 5));
            Assert.Equal(Both, chunk.GetFlags(25, 25));
            Assert.Equal(Both, chunk.GetFlags(20, 25));
            Assert.Equal(Both, chunk.GetFlags(40, 25));
            Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(41, 25));
            Assert.Equal(TerrainMaskFlags.None, chunk.GetFlags(0, 0));

            if (baselineHash.Length == 0)
            {
                baselineHash = chunk.ContentSha256Hex;
            }
            else
            {
                Assert.Equal(baselineHash, chunk.ContentSha256Hex);
            }
        }
    }

    [Fact]
    public void CollinearRegionRingsAreRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        var collinear = new GeoPolygon(RingOf((100, 100), (200, 100), (300, 100)));
        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, collinear, ruleIndex: 0)],
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, diagnostic.Code);
        Assert.Contains("bounds no area", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BowtieRegionRingsAreRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        // A bowtie crosses itself and its signed area collapses to zero.
        var bowtie = new GeoPolygon(RingOf((100, 100), (300, 100), (100, 300), (300, 300)));
        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, bowtie, ruleIndex: 0)],
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, diagnostic.Code);
        Assert.Contains("bounds no area", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfIntersectingRegionRingsAreRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        // The tail crosses the bottom edge, but the net signed area stays non-zero, so
        // only topology validation catches it.
        var crossed = new GeoPolygon(RingOf((100, 100), (140, 100), (140, 140), (100, 140), (102, 99)));
        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, crossed, ruleIndex: 0)],
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, diagnostic.Code);
        Assert.Contains("invalid topology", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HolesOutsideTheOuterRingAreRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, Square(100, 100, 300, 300, hole: (400, 400, 500, 500)), ruleIndex: 0)],
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, diagnostic.Code);
        Assert.Contains("invalid topology", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AdjacentChunksShareIdenticalCoreEdgeMasks()
    {
        var grid = CreateGrid(widthMeters: 2560d, heightMeters: 1280d);
        Assert.NotNull(grid);
        Assert.Equal(2, grid!.ChunksX);

        var terrainGenerator = new TerrainChunkGenerator(SamplingTestSupport.IdentityTransformer());
        var elevation = PlaneElevation(2, 1);
        var water = Region(GeneratedFeatureKind.Water, Square(1200, 100, 1400, 300), ruleIndex: 0);

        var westTerrain = terrainGenerator.Generate(grid, elevation, new SamplingOptions(), 0, 0);
        var eastTerrain = terrainGenerator.Generate(grid, elevation, new SamplingOptions(), 1, 0);
        Assert.NotNull(westTerrain);
        Assert.NotNull(eastTerrain);

        var west = TerrainMaskGenerator.Generate(grid, westTerrain!, [water]);
        var east = TerrainMaskGenerator.Generate(grid, eastTerrain!, [water]);

        Assert.NotNull(west);
        Assert.NotNull(east);

        // Each chunk derives the seam from its own apron copy; the shared edge cells
        // must still evaluate bit-identically.
        for (var sy = 0; sy < ChunkLayout.PointsPerChunkAxis; sy++)
        {
            Assert.Equal(west!.GetFlags(128, sy), east!.GetFlags(0, sy));
        }

        // The straddling water polygon covers the seam at row 20; the bare slope shows
        // at row 0: 29.2 degrees, unbuildable only.
        Assert.Equal(Both, west.GetFlags(128, 20));
        Assert.Equal(Both, east.GetFlags(0, 20));
        Assert.Equal(TerrainMaskFlags.Unbuildable, west.GetFlags(128, 0));
        Assert.Equal(TerrainMaskFlags.Unbuildable, east.GetFlags(0, 0));
    }

    [Fact]
    public void RegionsBeyondTheChunkPaintNothing()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, Square(5000, 5000, 6000, 6000), ruleIndex: 0)],
            diagnostics: diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);
        AssertAllCells(chunk!, TerrainMaskFlags.None);
    }

    [Fact]
    public void ZeroThresholdsFlagEverySlopedSampleAndFlatSamplesStayClear()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var sloped = TerrainMaskGenerator.Generate(grid!, PlaneTerrain(10d), thresholds: new SlopeThresholds(0d, 0d));
        var flat = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), thresholds: new SlopeThresholds(0d, 0d));

        Assert.NotNull(sloped);
        Assert.NotNull(flat);

        // Strictly greater than zero: every sloped sample flags, exactly flat samples
        // (0 > 0 is false) stay clear.
        AssertAllCells(sloped!, Both);
        AssertAllCells(flat!, TerrainMaskFlags.None);
    }

    [Fact]
    public void NinetyDegreeThresholdsFlagNothing()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var chunk = TerrainMaskGenerator.Generate(grid!, PlaneTerrain(60d), thresholds: new SlopeThresholds(90d, 90d));

        Assert.NotNull(chunk);
        AssertAllCells(chunk!, TerrainMaskFlags.None);
    }

    [Fact]
    public void InvalidThresholdsAreRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var invalid = new[]
        {
            new SlopeThresholds(double.NaN, 15d),
            new SlopeThresholds(45d, double.NaN),
            new SlopeThresholds(double.NegativeInfinity, 15d),
            new SlopeThresholds(45d, double.PositiveInfinity),
            new SlopeThresholds(-0.001d, 15d),
            new SlopeThresholds(45d, 90.0001d),
        };

        foreach (var thresholds in invalid)
        {
            var diagnostics = new GeoDiagnosticBag();
            var chunk = TerrainMaskGenerator.Generate(grid!, FlatTerrain(), thresholds: thresholds, diagnostics: diagnostics);

            Assert.Null(chunk);
            var diagnostic = Assert.Single(diagnostics.Items);
            Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskThresholdInvalid, diagnostic.Code);
            Assert.Equal(GeoStage.Generate, diagnostic.Stage);
            Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
        }
    }

    [Fact]
    public void ATerrainChunkOutsideTheGridIsRejected()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        Assert.Throws<ArgumentNullException>(() => TerrainMaskGenerator.Generate(null!, FlatTerrain()));
        Assert.Throws<ArgumentNullException>(() => TerrainMaskGenerator.Generate(grid!, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMaskGenerator.Generate(grid!, new TerrainChunk(1, 0, FlatHeights())));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMaskGenerator.Generate(grid!, new TerrainChunk(0, 1, FlatHeights())));
    }

    [Fact]
    public void RegionsWithoutARuleIndexAreRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, Square(100, 100, 300, 300), ruleIndex: -1)],
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, diagnostic.Code);
        Assert.Contains("rule index", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPolygonRegionGeometryIsRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var line = new GeoLineString(
        [
            new GeoPosition(new Vector2d(100, 100)),
            new GeoPosition(new Vector2d(200, 200)),
        ]);
        var lineDiagnostics = new GeoDiagnosticBag();
        var lineChunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [new GeneratedFeature(GeneratedFeatureKind.Water, line, "vectors", "river", "vectors/river#0", 0)],
            diagnostics: lineDiagnostics);

        Assert.Null(lineChunk);
        var lineDiagnostic = Assert.Single(lineDiagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, lineDiagnostic.Code);
        Assert.Contains("GeoLineString", lineDiagnostic.Message, StringComparison.Ordinal);

        var point = new GeoPointGeometry(new GeoPosition(new Vector2d(100, 100)));
        var pointDiagnostics = new GeoDiagnosticBag();
        var pointChunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [new GeneratedFeature(
                GeneratedFeatureKind.Water,
                point,
                "vectors",
                "spring",
                "vectors/spring#0",
                0,
                position: new GamePosition(100, 100, 0))],
            diagnostics: pointDiagnostics);

        Assert.Null(pointChunk);
        var pointDiagnostic = Assert.Single(pointDiagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, pointDiagnostic.Code);
        Assert.Contains("GeoPointGeometry", pointDiagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeeplyNestedMultiGeometryIsRejectedWithoutRecursion()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // The mapper emits flattened parts; a nested multi is unsupported input and
        // must fail typed instead of recursing over the nesting.
        GeoGeometry geometry = new GeoMultiGeometry([]);
        for (var i = 0; i < 100_000; i++)
        {
            geometry = new GeoMultiGeometry([geometry]);
        }

        var diagnostics = new GeoDiagnosticBag();
        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [new GeneratedFeature(GeneratedFeatureKind.Water, geometry, "vectors", "nested", "vectors/nested#0", 0)],
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, diagnostic.Code);
        Assert.Contains("GeoMultiGeometry", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteRegionCoordinatesAreRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, Square(100, 100, double.NaN, 300), ruleIndex: 0)],
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
    }

    [Fact]
    public void DegenerateOuterRingsAreRejectedTyped()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);
        var diagnostics = new GeoDiagnosticBag();

        var sliver = new GeoPolygon(RingOf((100, 100), (200, 100)));
        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [Region(GeneratedFeatureKind.Water, sliver, ruleIndex: 0)],
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, diagnostic.Code);
        Assert.Contains("bounds no area", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VectorLimitsFailTypedBeforeRingCopies()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var water = Region(GeneratedFeatureKind.Water, Square(100, 100, 300, 300), ruleIndex: 0);
        var twoFeatures = new List<GeneratedFeature>
        {
            water,
            Region(GeneratedFeatureKind.Water, Square(400, 100, 600, 300), ruleIndex: 1),
        };

        var featureDiagnostics = new GeoDiagnosticBag();
        Assert.Null(TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            twoFeatures,
            diagnostics: featureDiagnostics,
            limits: new GeospatialImportLimits(MaxFeatureCount: 1)));
        Assert.Single(featureDiagnostics.Items, static d => d.Code == TerrainMaskDiagnosticCodes.TerrainMaskLimitExceeded);
        Assert.Contains("maxFeatureCount", featureDiagnostics.Items.Single().Message, StringComparison.Ordinal);

        // The playable-area square (5 positions) plus the water square (5 positions)
        // exceed the budget before any ring copy happens.
        var coordinateDiagnostics = new GeoDiagnosticBag();
        Assert.Null(TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [water],
            diagnostics: coordinateDiagnostics,
            limits: new GeospatialImportLimits(MaxCoordinateCount: 8)));
        Assert.Single(coordinateDiagnostics.Items, static d => d.Code == TerrainMaskDiagnosticCodes.TerrainMaskLimitExceeded);
        Assert.Contains("maxCoordinateCount", coordinateDiagnostics.Items.Single().Message, StringComparison.Ordinal);

        // The count pass, ring preparation, NTS validation, and the first two probes
        // fit; the per-cell classification work exhausts the remaining budget during
        // rasterization.
        var budgetDiagnostics = new GeoDiagnosticBag();
        Assert.Null(TerrainMaskGenerator.Generate(
            grid!,
            FlatTerrain(),
            [water],
            diagnostics: budgetDiagnostics,
            limits: new GeospatialImportLimits(MaxVectorWorkPerChunk: 26)));
        Assert.Single(budgetDiagnostics.Items, static d => d.Code == TerrainMaskDiagnosticCodes.TerrainMaskLimitExceeded);
        Assert.Contains("maxVectorWorkPerChunk", budgetDiagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelledTokenThrowsBeforeAnyWork()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => TerrainMaskGenerator.Generate(grid!, FlatTerrain(), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void TheChunkValidatesCopiesAndHashesItsFlagBuffer()
    {
        var buffer = new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis];
        Array.Fill(buffer, (byte)TerrainMaskFlags.Impassable);
        buffer[^1] = (byte)Both;

        var chunk = new TerrainMaskChunk(0, 0, buffer);

        buffer[0] = 0;
        buffer[^1] = 0;
        Assert.Equal(TerrainMaskFlags.Impassable, chunk.GetFlags(0, 0));
        Assert.Equal(TerrainMaskFlags.Impassable, chunk.GetFlags(64, 64));
        Assert.Equal(Both, chunk.GetFlags(128, 128));
        Assert.Equal((byte)TerrainMaskFlags.Impassable, chunk.Flags.Span[0]);

        var expected = Convert.ToHexString(SHA256.HashData(chunk.Flags.ToArray())).ToLowerInvariant();
        Assert.Equal(expected, chunk.ContentSha256Hex);

        Assert.Throws<ArgumentException>(() => new TerrainMaskChunk(0, 0, new byte[100]));
        Assert.Throws<ArgumentException>(() => new TerrainMaskChunk(0, 0, WithFlag(buffer, 0, 4)));
        Assert.Throws<ArgumentException>(() => new TerrainMaskChunk(0, 0, WithFlag(buffer, 0, 255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainMaskChunk(-1, 0, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainMaskChunk(0, -1, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.GetFlags(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.GetFlags(0, 129));
    }

    [Fact]
    public void TheReportRecordsThresholdsExaggerationAndCounts()
    {
        // A 300 x 300 m extent on an exaggerated grid: the playable area covers
        // lattice 0..30 (961 cells), the chunk spans all 16641 cells.
        var grid = CreateGrid(widthMeters: 300d, heightMeters: 300d, verticalPolicy: new VerticalPolicy(50d, 2d));
        Assert.NotNull(grid);
        var report = new TerrainMaskReport();

        var chunk = TerrainMaskGenerator.Generate(
            grid!,
            PlaneTerrain(20d),
            new List<GeneratedFeature>
            {
                Region(GeneratedFeatureKind.Water, Square(100, 100, 300, 300), ruleIndex: 0),
                Region(GeneratedFeatureKind.Water, Square(200, 200, 300, 300), ruleIndex: 1),
            },
            thresholds: new SlopeThresholds(30d, 10d),
            report: report);

        Assert.NotNull(chunk);

        // Settings and grid metadata are required report fields.
        Assert.Equal(30d, report.ImpassableThresholdDegrees);
        Assert.Equal(10d, report.UnbuildableThresholdDegrees);
        Assert.Equal(2d, report.VerticalExaggeration);
        Assert.Equal(1, report.ChunkCount);

        // The 20 degree plane is above the unbuildable threshold only.
        Assert.Equal(0L, report.SlopeImpassableCellCount);
        Assert.Equal(16641L, report.SlopeUnbuildableCellCount);

        // Region counters are rule applications, not unique cells: the 21 x 21 square
        // (441) plus the overlapping 11 x 11 square (121) counts its shared cells twice.
        Assert.Equal(441L + 121L, report.WaterRuleApplicationCount);
        Assert.Equal(0L, report.PassabilityRuleApplicationCount);
        Assert.Equal(0L, report.BuildabilityRuleApplicationCount);

        // The playable exterior forces the 15680 cells outside lattice 0..30.
        Assert.Equal(15680L, report.PlayableForcedCellCount);

        // Impassable cells: the water-covered cells plus every forced cell. Unbuildable:
        // everywhere, from slope alone.
        Assert.Equal(441L + 15680L, report.ImpassableCellCount);
        Assert.Equal(16641L, report.UnbuildableCellCount);

        // The totals match an independent scan of the emitted chunk.
        Assert.Equal(CountCells(chunk!, TerrainMaskFlags.Impassable), report.ImpassableCellCount);
        Assert.Equal(CountCells(chunk!, TerrainMaskFlags.Unbuildable), report.UnbuildableCellCount);
    }

    private static TerrainMaskChunk MaskFromGeneratedTerrain(VerticalPolicy verticalPolicy)
    {
        var grid = CreateGrid(verticalPolicy: verticalPolicy);
        Assert.NotNull(grid);

        var terrain = new TerrainChunkGenerator(SamplingTestSupport.IdentityTransformer())
            .Generate(grid!, PlaneElevation(1, 1), new SamplingOptions(), 0, 0);

        Assert.NotNull(terrain);

        var mask = TerrainMaskGenerator.Generate(grid!, terrain!);

        Assert.NotNull(mask);

        return mask!;
    }

    private static void AssertAllCells(TerrainMaskChunk chunk, TerrainMaskFlags expected)
    {
        for (var y = 0; y < ChunkLayout.PointsPerChunkAxis; y++)
        {
            for (var x = 0; x < ChunkLayout.PointsPerChunkAxis; x++)
            {
                Assert.Equal(expected, chunk.GetFlags(x, y));
            }
        }
    }

    private static long CountCells(TerrainMaskChunk chunk, TerrainMaskFlags bit)
    {
        long count = 0;
        var flags = chunk.Flags.ToArray();

        for (var i = 0; i < flags.Length; i++)
        {
            if ((flags[i] & (byte)bit) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static TerrainChunk FlatTerrain()
    {
        return PlaneTerrain(0d);
    }

    /// <summary>
    /// A plane rising east (and optionally north) at the given angles: dyadic-free but
    /// far from every threshold, so float32 storage cannot move a cell across one.
    /// Stored sample (sx, sy) carries global sample (sx - 1, sy - 1).
    /// </summary>
    private static TerrainChunk PlaneTerrain(double slopeXDegrees, double slopeYDegrees = 0d)
    {
        var stored = ChunkLayout.StoredPointsPerChunkAxis;
        var heights = new float[stored * stored];
        var tanX = Math.Tan(slopeXDegrees * Math.PI / 180d);
        var tanY = Math.Tan(slopeYDegrees * Math.PI / 180d);

        for (var sy = 0; sy < stored; sy++)
        {
            for (var sx = 0; sx < stored; sx++)
            {
                var height = (tanX * ChunkLayout.SampleSpacingGameUnits * (sx - 1))
                    + (tanY * ChunkLayout.SampleSpacingGameUnits * (sy - 1));
                heights[(sy * stored) + sx] = (float)height;
            }
        }

        return new TerrainChunk(0, 0, heights);
    }

    private static float[] FlatHeights()
    {
        var stored = ChunkLayout.StoredPointsPerChunkAxis;
        var heights = new float[stored * stored];
        Array.Fill(heights, 5f);

        return heights;
    }

    private static GeneratedFeature Region(GeneratedFeatureKind kind, GeoPolygon polygon, int ruleIndex, bool? passable = null, bool? buildable = null)
    {
        return new GeneratedFeature(kind, polygon, "vectors", "region", "vectors/region#0", ruleIndex, passable: passable, buildable: buildable);
    }

    private static GeoPolygon Square(double west, double south, double east, double north, (double West, double South, double East, double North)? hole = null)
    {
        return new GeoPolygon(
            RingOf((west, south), (east, south), (east, north), (west, north)),
            hole is { } opening
                ? [RingOf((opening.West, opening.South), (opening.East, opening.South), (opening.East, opening.North), (opening.West, opening.North))]
                : null);
    }

    private static GeoRing RingOf(params (double X, double Y)[] vertices)
    {
        var positions = new List<GeoPosition>(vertices.Length + 1);

        for (var i = 0; i < vertices.Length; i++)
        {
            positions.Add(new GeoPosition(new Vector2d(vertices[i].X, vertices[i].Y)));
        }

        positions.Add(positions[0]);

        return new GeoRing(positions);
    }

    private static LocalMapGrid? CreateGrid(
        double widthMeters = 1280d,
        double heightMeters = 1280d,
        VerticalPolicy? verticalPolicy = null,
        (double West, double South, double East, double North)? playableHole = null)
    {
        // The extent CRS is the target CRS, so projected game (0, 0) sits at the
        // (OriginEasting, OriginNorthing) southwest corner the grid anchors on.
        GeoPolygon extent = playableHole is { } hole
            ? new GeoPolygon(
                ProjectedRing(0d, 0d, widthMeters, heightMeters),
                [ProjectedRing(hole.West, hole.South, hole.East, hole.North)])
            : new GeoPolygon(ProjectedRing(0d, 0d, widthMeters, heightMeters));

        return CreateGrid(extent, verticalPolicy);
    }

    private static LocalMapGrid? CreateGrid(GeoPolygon extent, VerticalPolicy? verticalPolicy = null)
    {
        var sources = new List<ManifestSource>
        {
            new("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent),
            new("dem", ManifestSourceKind.Geotiff, "dem.tif", ManifestSourceRole.Elevation, "EGM96"),
        };

        var manifest = new GeospatialImportManifest
        {
            Sources = sources,
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: 4d),
            MetersPerGameUnit = 1d,
            DefaultMaterial = "grass",
            VerticalPolicy = verticalPolicy ?? new VerticalPolicy(),
            MaxScaleError = 0.001d,
        };

        return LocalMapGrid.Create(extent, Tm4, manifest, Transformer, new GeoDiagnosticBag());
    }

    /// <summary>
    /// The extent square with the outer ring's first vertex rotated to the given corner
    /// (0 = SW, 1 = SE, 2 = NE, 3 = NW) and optionally reversed winding; the hole ring
    /// rotates one corner further.
    /// </summary>
    private static GeoPolygon RotatedExtent(
        double widthMeters,
        double heightMeters,
        (double West, double South, double East, double North)? playableHole,
        int startVertex,
        bool reverseWinding)
    {
        return new GeoPolygon(
            OrderedProjectedRing(0d, 0d, widthMeters, heightMeters, startVertex, reverseWinding),
            playableHole is { } hole
                ? [OrderedProjectedRing(hole.West, hole.South, hole.East, hole.North, startVertex + 1, reverseWinding)]
                : null);
    }

    private static GeoRing OrderedProjectedRing(double west, double south, double east, double north, int startVertex, bool reverseWinding)
    {
        var corners = new (double X, double Y)[]
        {
            (west, south),
            (east, south),
            (east, north),
            (west, north),
        };

        var positions = new List<GeoPosition>(5);

        for (var i = 0; i < 4; i++)
        {
            var index = ((startVertex + (reverseWinding ? -i : i)) % 4 + 4) % 4;
            var (x, y) = corners[index];
            positions.Add(new GeoPosition(new Vector2d(OriginEasting + x, OriginNorthing + y)));
        }

        positions.Add(positions[0]);

        return new GeoRing(positions);
    }

    private static GeoRing ProjectedRing(double west, double south, double east, double north)
    {
        return RingOf(
            (OriginEasting + west, OriginNorthing + south),
            (OriginEasting + east, OriginNorthing + south),
            (OriginEasting + east, OriginNorthing + north),
            (OriginEasting + west, OriginNorthing + north));
    }

    private static double PlaneHeightMeters(Vector2d world)
    {
        return 50d + (0.25d * (world.X - OriginEasting)) + (0.5d * (world.Y - OriginNorthing));
    }

    /// <summary>
    /// A 10 m GSD raster whose cell centers sit exactly on the map lattice: south-up
    /// cell (c, r) carries the plane value at global sample (c - 1, r - 1).
    /// </summary>
    private static GeospatialRaster<double> PlaneElevation(int chunksX, int chunksY)
    {
        var width = (chunksX * ChunkLayout.IntervalsPerChunk) + 3;
        var height = (chunksY * ChunkLayout.IntervalsPerChunk) + 3;
        var transform = SamplingTestSupport.SouthUp(OriginEasting - 15d, OriginNorthing - 15d, 10d, 10d);

        return SamplingTestSupport.Raster(
            width,
            height,
            transform,
            Tm4,
            (column, row) => PlaneHeightMeters(transform.PixelToWorld(new Vector2d(column + 0.5d, row + 0.5d))),
            -9999d);
    }

    private static byte[] WithFlag(byte[] flags, int index, byte value)
    {
        var copy = (byte[])flags.Clone();
        copy[index] = value;

        return copy;
    }
}
