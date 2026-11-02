#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

public class MaterialLayerGeneratorTests
{
    private const double OriginEasting = 100000d;

    private const double OriginNorthing = 6000000d;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private static readonly CoordinateSystemId Tm4 = CoordinateSystemId.TransverseMercator(4d, 1d, 0d, 0d);

    [Fact]
    public void ClassificationValuesMapThroughNearestAndUnmatchedCellsKeepTheDefault()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, (gx, gy) => gy <= 40 ? 20 : (gx >= 100 ? 30 : 10));
        var rules = new List<MaterialRule>
        {
            new("water", ClassificationValue: 20),
            new("grass", ClassificationValue: 10),
        };

        var palette = new MaterialPalette(["sand", "grass", "water"]);
        var stats = new SamplingStats();
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification,
            clippedFeatures: null,
            classificationRules: rules,
            polygonRules: null,
            palette,
            0,
            0,
            stats: stats,
            diagnostics: diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(1L * ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis, stats.SampleCount);

        // Class 20 north of row 40, class 10 south of it, class 30 unmatched -> default 0.
        Assert.Equal(2, chunk!.GetIndex(0, 0));
        Assert.Equal(2, chunk.GetIndex(128, 40));
        Assert.Equal(1, chunk.GetIndex(0, 41));
        Assert.Equal(1, chunk.GetIndex(64, 64));
        Assert.Equal(1, chunk.GetIndex(99, 50));
        Assert.Equal(0, chunk.GetIndex(110, 50));
    }

    [Fact]
    public void CoarseClassificationSourcesStayNearestAndNeverAverageClasses()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // 20 m cells against the 10 m lattice: an area or bilinear kernel would average
        // neighboring classes into non-integral values and fail; nearest does not.
        var classification = CoarseClassification();
        var rules = new List<MaterialRule>
        {
            new("grass", ClassificationValue: 10),
            new("water", ClassificationValue: 20),
        };

        var palette = new MaterialPalette(["sand", "grass", "water"]);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification,
            clippedFeatures: null,
            classificationRules: rules,
            polygonRules: null,
            palette,
            0,
            0,
            diagnostics: diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);

        // Lattice (0, 0) is nearest cell (1, 1), class 10; lattice (2, 0) is nearest
        // cell (2, 1), class 20.
        Assert.Equal(1, chunk!.GetIndex(0, 0));
        Assert.Equal(2, chunk.GetIndex(2, 0));
    }

    [Fact]
    public void IntegerFormatsAreAcceptedAndFloatSourcesAreRejected()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var rules = new List<MaterialRule> { new("grass", ClassificationValue: 10) };
        var palette = new MaterialPalette(["sand", "grass"]);
        var generator = CreateGenerator();
        MaterialChunk? reference = null;

        foreach (var format in new[]
        {
            RasterSampleFormat.UInt8,
            RasterSampleFormat.Int16,
            RasterSampleFormat.UInt16,
            RasterSampleFormat.Int32,
        })
        {
            var chunk = generator.Generate(
                grid!,
                Classification(1, 1, format, static (_, _) => 10),
                clippedFeatures: null,
                classificationRules: rules,
                polygonRules: null,
                palette,
                0,
                0);

            Assert.NotNull(chunk);

            if (reference is null)
            {
                reference = chunk;
            }
            else
            {
                Assert.Equal(reference.Indices.ToArray(), chunk!.Indices.ToArray());
            }
        }

        var diagnostics = new GeoDiagnosticBag();
        var rejected = generator.Generate(
            grid!,
            Classification(1, 1, RasterSampleFormat.Float32, static (_, _) => 10),
            clippedFeatures: null,
            classificationRules: rules,
            polygonRules: null,
            palette,
            0,
            0,
            diagnostics: diagnostics);

        Assert.Null(rejected);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialSourceFormatInvalid, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void NonIntegralSampledClassificationValuesAreRejected()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // A constant no-data fill of 2.5 carries a fractional class value into the cell.
        var classification = Classification(
            1,
            1,
            RasterSampleFormat.UInt8,
            static (_, _) => 10,
            noDataCell: (Column: 1, Row: 1));
        var rules = new List<MaterialRule> { new("grass", ClassificationValue: 10) };
        var palette = new MaterialPalette(["sand", "grass"]);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification,
            clippedFeatures: null,
            classificationRules: rules,
            polygonRules: null,
            palette,
            0,
            0,
            samplingOptions: new SamplingOptions(noData: NoDataPolicy.Constant, noDataConstantMeters: 2.5d),
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialValueNotIntegral, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
    }

    [Fact]
    public void NoDataFillsFollowTheSuppliedPolicyAndCountOnTheOwnStats()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(
            1,
            1,
            RasterSampleFormat.UInt8,
            static (_, _) => 10,
            noDataCell: (Column: 1, Row: 1));
        var rules = new List<MaterialRule> { new("grass", ClassificationValue: 10) };
        var palette = new MaterialPalette(["sand", "grass"]);
        var stats = new SamplingStats();
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification,
            clippedFeatures: null,
            classificationRules: rules,
            polygonRules: null,
            palette,
            0,
            0,
            samplingOptions: new SamplingOptions(noData: NoDataPolicy.Constant, noDataConstantMeters: 10),
            stats: stats,
            diagnostics: diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(1, stats.NoDataFillCount);
        Assert.Equal(0, stats.FailedSampleCount);

        // The fill constant 10 is itself a class: the cell maps through the lookup.
        Assert.Equal(1, chunk!.GetIndex(0, 0));
    }

    [Fact]
    public void PolygonRulesOverrideTheClassificationAtCoveredPoints()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var rules = new List<MaterialRule> { new("grass", ClassificationValue: 10) };
        var polygons = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Geometry: ManifestFeatureGeometry.Polygon, Material: "concrete"),
        };

        var palette = new MaterialPalette(["sand", "grass", "water", "concrete"]);
        var features = new List<LocalFeature> { PolygonFeature("vectors", "square", 100, 100, 300, 300) };
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification,
            features,
            rules,
            polygons,
            palette,
            0,
            0,
            diagnostics: diagnostics);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);

        // The square spans game units 100..300: lattice 10..30 inclusive on its outer
        // boundary, one step outside it the classification shows through.
        Assert.Equal(3, chunk!.GetIndex(10, 10));
        Assert.Equal(3, chunk.GetIndex(20, 20));
        Assert.Equal(3, chunk.GetIndex(30, 30));
        Assert.Equal(1, chunk.GetIndex(31, 20));
        Assert.Equal(1, chunk.GetIndex(20, 9));
        Assert.Equal(1, chunk.GetIndex(0, 0));
    }

    [Fact]
    public void LaterPolygonRulesOverrideEarlierOnesAcrossOverlappingFeatures()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var polygons = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Attribute: "kind", Value: "a", Material: "water"),
            new("vectors", ManifestFeatureTarget.Material, Attribute: "kind", Value: "b", Material: "concrete"),
        };

        var palette = new MaterialPalette(["sand", "grass", "water", "concrete"]);
        var features = new List<LocalFeature>
        {
            PolygonFeature("vectors", "west", 100, 100, 300, 300, attributes: new Dictionary<string, string> { ["kind"] = "a" }),
            PolygonFeature("vectors", "east", 200, 100, 400, 300, attributes: new Dictionary<string, string> { ["kind"] = "b" }),
        };

        var chunk = CreateGenerator().Generate(grid!, classification, features, [], polygons, palette, 0, 0);

        Assert.NotNull(chunk);

        // Only the first feature reaches lattice 15, only the second reaches 35, and the
        // overlap at 25 keeps the later rule's material.
        Assert.Equal(2, chunk!.GetIndex(15, 20));
        Assert.Equal(3, chunk.GetIndex(35, 20));
        Assert.Equal(3, chunk.GetIndex(25, 20));
        Assert.Equal(0, chunk.GetIndex(5, 20));
    }

    [Fact]
    public void PolygonHolesRemainExcludedWithUnambiguousBoundaries()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var rules = new List<MaterialRule> { new("grass", ClassificationValue: 10) };
        var polygons = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Material: "concrete"),
        };

        var palette = new MaterialPalette(["sand", "grass", "concrete"]);
        var features = new List<LocalFeature>
        {
            PolygonFeature("vectors", "ring", 100, 100, 500, 500, hole: (200, 200, 400, 400)),
        };

        var chunk = CreateGenerator().Generate(grid!, classification, features, rules, polygons, palette, 0, 0);

        Assert.NotNull(chunk);

        // Outer ring: boundary at lattice 10 and 50 included. Hole 200..400: its boundary
        // at lattice 20 and 40 and its interior stay excluded.
        Assert.Equal(2, chunk!.GetIndex(10, 25));
        Assert.Equal(2, chunk.GetIndex(15, 15));
        Assert.Equal(2, chunk.GetIndex(50, 50));
        Assert.Equal(1, chunk.GetIndex(20, 25));
        Assert.Equal(1, chunk.GetIndex(40, 25));
        Assert.Equal(1, chunk.GetIndex(25, 25));
        Assert.Equal(2, chunk.GetIndex(41, 25));
        Assert.Equal(1, chunk.GetIndex(9, 25));
    }

    [Fact]
    public void LayerFolderGeometryAndAttributePredicatesSelectThePaintedFeatures()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var polygons = new List<FeatureMappingRule>
        {
            // Attribute presence paints every feature that carries the attribute.
            new("vectors", ManifestFeatureTarget.Material, Attribute: "kind", Material: "bare"),

            // Exact attribute value paints only the marsh region.
            new("vectors", ManifestFeatureTarget.Material, Attribute: "kind", Value: "marsh", Material: "water"),

            // Layer name matches the source feature's full folder path.
            new("vectors", ManifestFeatureTarget.Material, LayerName: "Base/Regions", Material: "concrete"),

            // A point-geometry predicate can never match a polygon feature.
            new("vectors", ManifestFeatureTarget.Material, Geometry: ManifestFeatureGeometry.Point, Material: "snow"),
        };

        var palette = new MaterialPalette(["grass", "bare", "water", "concrete", "snow"]);
        var features = new List<LocalFeature>
        {
            PolygonFeature("vectors", "layered", 100, 100, 300, 200, folder: "Base/Regions"),
            PolygonFeature("vectors", "marsh", 400, 100, 600, 200, attributes: new Dictionary<string, string> { ["kind"] = "marsh" }),
            PolygonFeature("vectors", "gravel", 700, 100, 900, 200, attributes: new Dictionary<string, string> { ["kind"] = "gravel" }),
            PolygonFeature("vectors", "otherLayer", 1000, 100, 1200, 200, folder: "Other"),
            PolygonFeature("others", "foreign", 100, 400, 300, 500),
        };

        var chunk = CreateGenerator().Generate(grid!, classification, features, [], polygons, palette, 0, 0);

        Assert.NotNull(chunk);

        Assert.Equal(3, chunk!.GetIndex(20, 15));
        Assert.Equal(2, chunk.GetIndex(50, 15));
        Assert.Equal(1, chunk.GetIndex(80, 15));
        Assert.Equal(0, chunk.GetIndex(110, 15));

        // The foreign-source feature never matched any rule.
        Assert.Equal(0, chunk.GetIndex(20, 45));

        // The point-geometry predicate never matched a polygon feature.
        Assert.DoesNotContain((byte)4, chunk.Indices.ToArray());
    }

    [Fact]
    public void AdjacentChunksShareIdenticalCoreEdgesAcrossPolygonsAndClassification()
    {
        var grid = CreateGrid(widthMeters: 2560d, heightMeters: 1280d);
        Assert.NotNull(grid);
        Assert.Equal(2, grid!.ChunksX);

        // The class flips exactly at the chunk seam, and a polygon straddles it.
        var classification = Classification(2, 1, RasterSampleFormat.UInt8, (gx, _) => gx <= 128 ? 20 : 10);
        var rules = new List<MaterialRule>
        {
            new("water", ClassificationValue: 20),
            new("grass", ClassificationValue: 10),
        };

        var polygons = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Material: "concrete"),
        };

        var palette = new MaterialPalette(["sand", "grass", "water", "concrete"]);
        var features = new List<LocalFeature> { PolygonFeature("vectors", "seam", 1200, 100, 1400, 300) };
        var generator = CreateGenerator();

        var west = generator.Generate(grid, classification, features, rules, polygons, palette, 0, 0);
        var east = generator.Generate(grid, classification, features, rules, polygons, palette, 1, 0);
        var westAgain = generator.Generate(grid, classification, features, rules, polygons, palette, 0, 0);

        Assert.NotNull(west);
        Assert.NotNull(east);

        for (var sy = 0; sy < ChunkLayout.PointsPerChunkAxis; sy++)
        {
            Assert.Equal(west!.GetIndex(128, sy), east!.GetIndex(0, sy));
        }

        // Rows on the seam inside the polygon painted identically in both chunks.
        Assert.Equal(3, west!.GetIndex(128, 20));
        Assert.Equal(3, east!.GetIndex(0, 20));

        // Rows outside the polygon show the classification at the seam sample.
        Assert.Equal(2, west.GetIndex(128, 0));
        Assert.Equal(2, east.GetIndex(0, 0));

        Assert.Equal(west.ContentSha256Hex, westAgain!.ContentSha256Hex);
    }

    [Fact]
    public void ClassificationRulesWithoutARasterFailGeneration()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var rules = new List<MaterialRule> { new("grass", ClassificationValue: 10) };
        var palette = new MaterialPalette(["sand", "grass"]);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification: null,
            clippedFeatures: null,
            classificationRules: rules,
            polygonRules: null,
            palette,
            0,
            0,
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialClassificationMissing, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
    }

    [Fact]
    public void RulesReferencingNamesOutsideThePaletteFailGeneration()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var generator = CreateGenerator();

        // The explicit palette omits the rule's material: nothing auto-adds it.
        var classificationDiagnostics = new GeoDiagnosticBag();
        var classificationChunk = generator.Generate(
            grid!,
            classification,
            clippedFeatures: null,
            classificationRules: [new MaterialRule("grass", ClassificationValue: 10)],
            polygonRules: null,
            new MaterialPalette(["sand"]),
            0,
            0,
            diagnostics: classificationDiagnostics);

        Assert.Null(classificationChunk);
        var classificationDiagnostic = Assert.Single(classificationDiagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialNameUnknown, classificationDiagnostic.Code);
        Assert.Contains("grass", classificationDiagnostic.Message, StringComparison.Ordinal);

        var polygonDiagnostics = new GeoDiagnosticBag();
        var polygonChunk = generator.Generate(
            grid!,
            classification,
            clippedFeatures: [PolygonFeature("vectors", "square", 100, 100, 300, 300)],
            classificationRules: null,
            polygonRules: [new FeatureMappingRule("vectors", ManifestFeatureTarget.Material, Material: "concrete")],
            new MaterialPalette(["sand", "grass"]),
            0,
            0,
            diagnostics: polygonDiagnostics);

        Assert.Null(polygonChunk);
        var polygonDiagnostic = Assert.Single(polygonDiagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialNameUnknown, polygonDiagnostic.Code);
        Assert.Contains("concrete", polygonDiagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutputBudgetBelowTheIndexBufferFailsBeforeAllocation()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification: null,
            clippedFeatures: null,
            classificationRules: null,
            polygonRules: null,
            new MaterialPalette(["sand"]),
            0,
            0,
            diagnostics: diagnostics,
            limits: new GeospatialImportLimits(MaxRasterDecodedBytes: 1024));

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialOutputLimitExceeded, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
    }

    [Fact]
    public void CancelledTokenThrowsBeforeTheFirstRow()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => CreateGenerator().Generate(
                grid!,
                Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10),
                clippedFeatures: null,
                classificationRules: [new MaterialRule("grass", ClassificationValue: 10)],
                polygonRules: null,
                new MaterialPalette(["sand", "grass"]),
                0,
                0,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ChunkIndexesAreRejectedOutsideTheGrid()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var generator = CreateGenerator();
        var palette = new MaterialPalette(["sand"]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Generate(grid!, null, null, null, null, palette, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Generate(grid!, null, null, null, null, palette, grid!.ChunksX, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Generate(grid!, null, null, null, null, palette, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => generator.Generate(grid!, null, null, null, null, palette, 0, grid.ChunksY));
    }

    [Fact]
    public void ChunkCopiesItsIndexBufferAndValidatesItsInput()
    {
        var palette = new MaterialPalette(["sand", "water", "concrete"]);
        var indices = new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis];
        Array.Fill(indices, (byte)1);
        indices[^1] = 2;

        var chunk = new MaterialChunk(0, 0, palette, indices);

        indices[0] = 0;
        indices[^1] = 0;
        Assert.Equal(1, chunk.GetIndex(0, 0));
        Assert.Equal(1, chunk.Indices.Span[0]);
        Assert.Equal(2, chunk.GetIndex(128, 128));
        Assert.Same(palette, chunk.Palette);

        var expected = Convert.ToHexString(SHA256.HashData(chunk.Indices.ToArray())).ToLowerInvariant();
        Assert.Equal(expected, chunk.ContentSha256Hex);

        Assert.Throws<ArgumentException>(() => new MaterialChunk(0, 0, palette, new byte[100]));
        Assert.Throws<ArgumentException>(() => new MaterialChunk(0, 0, palette, WithIndex(indices, 0, 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialChunk(-1, 0, palette, indices));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialChunk(0, -1, palette, indices));
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.GetIndex(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.GetIndex(0, 129));
    }

    [Fact]
    public void PaletteValidatesNamesAndDerivesItsOrderFromTheRules()
    {
        Assert.Throws<ArgumentException>(() => new MaterialPalette(Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => new MaterialPalette((IReadOnlyList<string>?)null));
        Assert.Throws<ArgumentException>(() => new MaterialPalette(["sand", " "]));
        Assert.Throws<ArgumentException>(() => new MaterialPalette(["sand", "sand"]));
        Assert.Throws<ArgumentException>(
            () => new MaterialPalette(Enumerable.Range(0, MaterialPalette.MaxEntries + 1).Select(i => "material" + i).ToArray()));

        var rules = new List<MaterialRule>
        {
            new("grass", ClassificationValue: 10),
            new("water", ClassificationValue: 20),
            new("marsh", Attribute: "kind"),
        };

        var mappings = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, Geometry: ManifestFeatureGeometry.Point),
            new("vectors", ManifestFeatureTarget.Material, Geometry: ManifestFeatureGeometry.Polygon, Material: "concrete"),
        };

        var palette = MaterialPalette.FromRules("sand", rules, mappings);

        Assert.Equal(new[] { "sand", "grass", "water", "marsh", "concrete" }, palette.Names);
        Assert.Equal("sand", palette.DefaultMaterial);
        Assert.Equal(0, palette.DefaultIndex);
        Assert.Equal(5, palette.Count);
        Assert.True(palette.TryGetIndex("water", out var waterIndex));
        Assert.Equal(2, waterIndex);
        Assert.False(palette.TryGetIndex("GRASS", out _));
        Assert.False(palette.TryGetIndex("missing", out _));

        Assert.Throws<ArgumentException>(() => MaterialPalette.FromRules(" ", null, null));
        Assert.Throws<ArgumentException>(
            () => MaterialPalette.FromRules(
                "sand",
                null,
                [new FeatureMappingRule("vectors", ManifestFeatureTarget.Material, Material: null)]));
    }

    [Fact]
    public void WorldCoverExampleManifestLoadsWithoutDiagnostics()
    {
        var diagnostics = new GeoDiagnosticBag();
        var manifest = ManifestLoader.Load(ExampleManifestPath(), diagnostics);

        Assert.NotNull(manifest);
        Assert.Empty(diagnostics.Items);
        Assert.Equal("grass", manifest!.DefaultMaterial);

        Assert.Equal(
            new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 100 },
            manifest.MaterialRules.Select(static rule => rule.ClassificationValue!.Value).ToArray());

        var palette = MaterialPalette.FromRules(manifest.DefaultMaterial, manifest.MaterialRules, manifest.FeatureMappings);

        Assert.Equal("grass", palette.Names[0]);
        Assert.Equal(13, palette.Count);
        Assert.Equal(palette.Names.Count, palette.Names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("sand", palette.Names);
    }

    [Fact]
    public void AttributeMaterialRulesPaintOverTheClassification()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var rules = new List<MaterialRule>
        {
            new("grass", ClassificationValue: 10),
            new("water", Attribute: "kind", Value: "marsh"),
        };

        var palette = new MaterialPalette(["sand", "grass", "water"]);
        var features = new List<LocalFeature>
        {
            PolygonFeature("vectors", "marsh", 100, 100, 300, 300, attributes: new Dictionary<string, string> { ["kind"] = "marsh" }),
            PolygonFeature("vectors", "dry", 400, 100, 600, 300, attributes: new Dictionary<string, string> { ["kind"] = "scrub" }),
        };

        var chunk = CreateGenerator().Generate(grid!, classification, features, rules, polygonRules: null, palette, 0, 0);

        Assert.NotNull(chunk);

        // The attribute stage paints only the marsh feature; the dry feature keeps the
        // classification value beneath it.
        Assert.Equal(2, chunk!.GetIndex(20, 20));
        Assert.Equal(1, chunk.GetIndex(50, 20));
        Assert.Equal(1, chunk.GetIndex(0, 0));
    }

    [Fact]
    public void AttributeRulePresenceSourceScopeAndMissingAttributeDefault()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var rules = new List<MaterialRule>
        {
            // Presence: any kind value, but only from the vectors source.
            new("bare", SourceId: "vectors", Attribute: "kind"),

            // A null source id matches every source, exact value only.
            new("water", Attribute: "surface", Value: "marsh"),
        };

        var palette = new MaterialPalette(["sand", "bare", "water"]);
        var features = new List<LocalFeature>
        {
            PolygonFeature("vectors", "gravel", 100, 100, 300, 200, attributes: new Dictionary<string, string> { ["kind"] = "gravel" }),
            PolygonFeature("vectors", "bare", 400, 100, 600, 200),
            PolygonFeature("others", "marsh", 700, 100, 900, 200, attributes: new Dictionary<string, string> { ["surface"] = "marsh" }),
            PolygonFeature("others", "dry", 1000, 100, 1200, 200, attributes: new Dictionary<string, string> { ["kind"] = "x", ["surface"] = "dry" }),
        };

        var chunk = CreateGenerator().Generate(grid!, classification: null, features, rules, polygonRules: null, palette, 0, 0);

        Assert.NotNull(chunk);
        Assert.Equal(1, chunk!.GetIndex(20, 15));
        Assert.Equal(0, chunk.GetIndex(50, 15));
        Assert.Equal(2, chunk.GetIndex(80, 15));
        Assert.Equal(0, chunk.GetIndex(110, 15));
    }

    [Fact]
    public void AttributeRulesAreValidatedAgainstThePaletteWithoutFeatures()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification: null,
            clippedFeatures: null,
            classificationRules: [new MaterialRule("marsh", Attribute: "zone")],
            polygonRules: null,
            new MaterialPalette(["sand"]),
            0,
            0,
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialNameUnknown, diagnostic.Code);
        Assert.Contains("marsh", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PaintOrderIsFrozenClassificationThenAttributeRulesThenMappingRules()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var valueRules = new List<MaterialRule>
        {
            new("grass", ClassificationValue: 10),
            new("water", Attribute: "zone", Value: "special"),
        };

        var mappings = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Material: "concrete"),
        };

        var palette = new MaterialPalette(["sand", "grass", "water", "concrete"]);
        var features = new List<LocalFeature>
        {
            PolygonFeature("vectors", "region", 100, 100, 500, 500, attributes: new Dictionary<string, string> { ["zone"] = "special" }),
        };

        var generator = CreateGenerator();

        var both = generator.Generate(grid!, classification, features, valueRules, mappings, palette, 0, 0);
        Assert.NotNull(both);
        Assert.Equal(3, both!.GetIndex(30, 30));

        var attributeOnly = generator.Generate(grid!, classification, features, valueRules, polygonRules: null, palette, 0, 0);
        Assert.NotNull(attributeOnly);
        Assert.Equal(2, attributeOnly!.GetIndex(30, 30));
        Assert.Equal(1, attributeOnly.GetIndex(60, 60));
    }

    [Fact]
    public void DuplicateClassificationValuesKeepTheLaterRule()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var rules = new List<MaterialRule>
        {
            new("grass", ClassificationValue: 10),
            new("water", ClassificationValue: 10),
        };

        var palette = new MaterialPalette(["sand", "grass", "water"]);
        var chunk = CreateGenerator().Generate(grid!, classification, clippedFeatures: null, rules, polygonRules: null, palette, 0, 0);

        Assert.NotNull(chunk);
        Assert.Equal(2, chunk!.GetIndex(64, 64));
    }

    [Fact]
    public void MultiBandClassificationSourcesAreRejected()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var rules = new List<MaterialRule> { new("grass", ClassificationValue: 10) };
        var palette = new MaterialPalette(["sand", "grass"]);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10, bandCount: 3),
            clippedFeatures: null,
            rules,
            polygonRules: null,
            palette,
            0,
            0,
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialSourceBandCountInvalid, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
    }

    [Fact]
    public void VectorLimitsFailTypedBeforePainting()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var rules = new List<MaterialRule> { new("concrete", Attribute: "zone") };
        var palette = new MaterialPalette(["sand", "grass", "concrete"]);
        var generator = CreateGenerator();
        var features = new List<LocalFeature>
        {
            PolygonFeature("vectors", "one", 100, 100, 300, 300, attributes: new Dictionary<string, string> { ["zone"] = "a" }),
            PolygonFeature("vectors", "two", 400, 100, 600, 300, attributes: new Dictionary<string, string> { ["zone"] = "a" }),
        };

        var featureDiagnostics = new GeoDiagnosticBag();
        Assert.Null(generator.Generate(
            grid!,
            classification: null,
            features,
            rules,
            polygonRules: null,
            palette,
            0,
            0,
            diagnostics: featureDiagnostics,
            limits: new GeospatialImportLimits(MaxFeatureCount: 1)));
        Assert.Single(featureDiagnostics.Items, static d => d.Code == MaterialDiagnosticCodes.MaterialVectorWorkLimitExceeded);
        Assert.Contains("maxFeatureCount", featureDiagnostics.Items.Single().Message, StringComparison.Ordinal);

        var coordinateDiagnostics = new GeoDiagnosticBag();
        Assert.Null(generator.Generate(
            grid!,
            classification: null,
            [features[0]],
            rules,
            polygonRules: null,
            palette,
            0,
            0,
            diagnostics: coordinateDiagnostics,
            limits: new GeospatialImportLimits(MaxCoordinateCount: 4)));
        Assert.Single(coordinateDiagnostics.Items, static d => d.Code == MaterialDiagnosticCodes.MaterialVectorWorkLimitExceeded);
        Assert.Contains("maxCoordinateCount", coordinateDiagnostics.Items.Single().Message, StringComparison.Ordinal);

        // The count pass spends 5 units, the rule-feature check 1 more, and ring
        // preparation of the 5-position ring exceeds the remaining budget of 2.
        var budgetDiagnostics = new GeoDiagnosticBag();
        Assert.Null(generator.Generate(
            grid!,
            classification: null,
            [features[0]],
            rules,
            polygonRules: null,
            palette,
            0,
            0,
            diagnostics: budgetDiagnostics,
            limits: new GeospatialImportLimits(MaxVectorWorkPerChunk: 8)));
        Assert.Single(budgetDiagnostics.Items, static d => d.Code == MaterialDiagnosticCodes.MaterialVectorWorkLimitExceeded);
        Assert.Contains("maxVectorWorkPerChunk", budgetDiagnostics.Items.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteFeatureCoordinatesAreRejected()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var mappings = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Material: "concrete"),
        };

        var palette = new MaterialPalette(["sand", "concrete"]);
        var diagnostics = new GeoDiagnosticBag();
        var features = new List<LocalFeature> { PolygonFeature("vectors", "invalid", 100, 100, double.NaN, 300) };

        var chunk = CreateGenerator().Generate(grid!, classification: null, features, classificationRules: null, mappings, palette, 0, 0, diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialFeatureInvalid, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
    }

    [Fact]
    public void NestedMultiGeometryIsRejectedTypedWithoutRecursion()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // The clipper emits flattened parts; a nested multi is unsupported input and must
        // fail typed instead of recursing over the nesting.
        GeoGeometry geometry = new GeoMultiGeometry([]);
        for (var i = 0; i < 100_000; i++)
        {
            geometry = new GeoMultiGeometry([geometry]);
        }

        var source = new GeospatialFeature("vectors", "nested", geometry, CoordinateSystemId.Wgs84);
        var features = new List<LocalFeature> { new(geometry, source, "vectors/nested#0") };
        var mappings = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Material: "concrete"),
        };

        var palette = new MaterialPalette(["sand", "concrete"]);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification: null,
            features,
            classificationRules: null,
            mappings,
            palette,
            0,
            0,
            diagnostics: diagnostics);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialFeatureInvalid, diagnostic.Code);
        Assert.Contains("GeoMultiGeometry", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HoleBoundaryTouchingTheOuterBoundaryStaysExcluded()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // A diamond hole whose left tip (100, 250) sits exactly on the outer box's left
        // edge: the touch sample is on both boundaries and the hole must win.
        var geometry = new GeoPolygon(
            RingOf((100, 100), (500, 100), (500, 500), (100, 500)),
            [RingOf((100, 250), (200, 350), (300, 250), (200, 150))]);
        var source = new GeospatialFeature("vectors", "ring", geometry, CoordinateSystemId.Wgs84);
        var features = new List<LocalFeature> { new(geometry, source, "vectors/ring#0") };
        var mappings = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Material: "concrete"),
        };

        var palette = new MaterialPalette(["sand", "concrete"]);
        var chunk = CreateGenerator().Generate(grid!, classification: null, features, classificationRules: null, mappings, palette, 0, 0);

        Assert.NotNull(chunk);

        Assert.Equal(0, chunk!.GetIndex(10, 25));
        Assert.Equal(1, chunk.GetIndex(10, 20));
        Assert.Equal(1, chunk.GetIndex(10, 30));
        Assert.Equal(0, chunk.GetIndex(25, 25));
        Assert.Equal(0, chunk.GetIndex(30, 25));
        Assert.Equal(1, chunk.GetIndex(31, 25));
    }

    private static string ExampleManifestPath([CallerFilePath] string sourcePath = "")
    {
        return Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "Examples", "worldcover-materials.json");
    }

    private static MaterialLayerGenerator CreateGenerator()
    {
        return new MaterialLayerGenerator(SamplingTestSupport.IdentityTransformer());
    }

    private static LocalMapGrid? CreateGrid(double widthMeters = 1280d, double heightMeters = 1280d)
    {
        var sources = new List<ManifestSource>
        {
            new("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent),
            new("dem", ManifestSourceKind.Geotiff, "dem.tif", ManifestSourceRole.Elevation, "EGM96"),
            new("vectors", ManifestSourceKind.Kml, "regions.kml", ManifestSourceRole.Vector),
        };

        var manifest = new GeospatialImportManifest
        {
            Sources = sources,
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: 4d),
            MetersPerGameUnit = 1d,
            DefaultMaterial = "sand",
            MaxScaleError = 0.001d,
        };

        return LocalMapGrid.Create(
            ProjectedSquare(OriginEasting, OriginNorthing, widthMeters, heightMeters),
            Tm4,
            manifest,
            Transformer,
            new GeoDiagnosticBag());
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

    /// <summary>
    /// A 10 m GSD raster whose cell centers sit exactly on the map lattice: cell (c, r)
    /// carries class <paramref name="value"/>(c - 1, r - 1), so nearest sampling reads the
    /// class of global sample (c - 1, r - 1) directly.
    /// </summary>
    private static GeospatialRaster<double> Classification(
        int chunksX,
        int chunksY,
        RasterSampleFormat format,
        Func<int, int, double> value,
        (int Column, int Row)? noDataCell = null,
        int bandCount = 1)
    {
        var width = (chunksX * ChunkLayout.IntervalsPerChunk) + 3;
        var height = (chunksY * ChunkLayout.IntervalsPerChunk) + 3;
        var transform = SamplingTestSupport.SouthUp(OriginEasting - 15d, OriginNorthing - 15d, 10d, 10d);
        var samples = new double[width * height * bandCount];

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var sample = noDataCell is { } cell && column == cell.Column && row == cell.Row
                    ? 255d
                    : value(column - 1, row - 1);

                for (var band = 0; band < bandCount; band++)
                {
                    samples[((row * width) + column) * bandCount + band] = sample;
                }
            }
        }

        return new GeospatialRaster<double>(
            width,
            height,
            bandCount,
            transform,
            Tm4,
            samples,
            255d,
            noDataCell is not null,
            format);
    }

    private static GeospatialRaster<double> CoarseClassification()
    {
        // 20 m cells: cell (c, r) centers on Origin + (20c - 15, 20r - 15).
        const int width = 66;
        const int height = 66;
        var transform = SamplingTestSupport.SouthUp(OriginEasting - 25d, OriginNorthing - 25d, 20d, 20d);
        var samples = new double[width * height];

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                samples[(row * width) + column] = (column + row) % 2 == 0 ? 10 : 20;
            }
        }

        return new GeospatialRaster<double>(
            width,
            height,
            1,
            transform,
            Tm4,
            samples,
            sampleFormat: RasterSampleFormat.UInt8);
    }

    private static LocalFeature PolygonFeature(
        string sourceId,
        string featureId,
        double west,
        double south,
        double east,
        double north,
        string? folder = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        (double West, double South, double East, double North)? hole = null)
    {
        var geometry = new GeoPolygon(
            Ring(west, south, east, north),
            hole is { } opening ? [Ring(opening.West, opening.South, opening.East, opening.North)] : null);

        var allAttributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (folder is not null)
        {
            allAttributes["folder"] = folder;
        }

        if (attributes is not null)
        {
            foreach (var pair in attributes)
            {
                allAttributes[pair.Key] = pair.Value;
            }
        }

        var source = new GeospatialFeature(sourceId, featureId, geometry, CoordinateSystemId.Wgs84, allAttributes);

        return new LocalFeature(geometry, source, sourceId + "/" + featureId + "#0");

        static GeoRing Ring(double ringWest, double ringSouth, double ringEast, double ringNorth)
        {
            return new GeoRing(
            [
                new GeoPosition(new Vector2d(ringWest, ringSouth)),
                new GeoPosition(new Vector2d(ringEast, ringSouth)),
                new GeoPosition(new Vector2d(ringEast, ringNorth)),
                new GeoPosition(new Vector2d(ringWest, ringNorth)),
                new GeoPosition(new Vector2d(ringWest, ringSouth)),
            ]);
        }
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

    private static byte[] WithIndex(byte[] indices, int index, byte value)
    {
        var copy = (byte[])indices.Clone();
        copy[index] = value;

        return copy;
    }
}
