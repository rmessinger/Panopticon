#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Tests.Pipeline;
using Panopticon.Geospatial.Tests.Sampling;
using Xunit;

namespace Panopticon.Geospatial.Tests.Generation;

/// <summary>
/// Line-corridor material rules (WP1026): width resolution ladder, raster &lt; line &lt;
/// polygon precedence, chunk-boundary continuity, map-edge clipping, determinism, opt-in
/// dormancy, and actionable failures, plus one full-pipeline import over synthetic sources.
/// </summary>
public class LineMaterialCorridorTests
{
    private const double OriginEasting = 100000d;

    private const double OriginNorthing = 6000000d;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private static readonly CoordinateSystemId Tm4 = CoordinateSystemId.TransverseMercator(4d, 1d, 0d, 0d);

    [Fact]
    public void RoadRailRiverSourcesPaintDistinctCorridorsWithLaterRuleTiebreak()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var roads = LineFeature("roads", "r1", [(0d, 100d), (1280d, 100d)], new Dictionary<string, string> { ["largeur"] = "10" });
        var rails = LineFeature("rails", "t1", [(200d, 0d), (200d, 1280d)], new Dictionary<string, string> { ["nb_voies"] = "2" });
        var rivers = LineFeature("rivers", "w1", [(0d, 0d), (1280d, 1280d)]);
        var features = new List<LocalFeature> { roads, rails, rivers };

        var lineRules = new List<LineMaterialRule>
        {
            new("roads", "dirt", WidthAttribute: "largeur"),
            new("rails", "rock", LanesAttribute: "nb_voies", MetersPerLane: 3.5d),
            new("rivers", "sand", DefaultWidthMeters: 10d),
        };

        var palette = new MaterialPalette(["grass", "dirt", "rock", "sand"]);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification: null,
            features,
            classificationRules: null,
            polygonRules: null,
            palette,
            0,
            0,
            diagnostics: diagnostics,
            lineRules: lineRules);

        Assert.NotNull(chunk);
        Assert.Empty(diagnostics.Items);

        // Road corridor: width 10 m at game scale 1 -> half-width 5 game units; only the
        // lattice row at y = 100 is within it.
        Assert.Equal(1, chunk!.GetIndex(30, 10));
        Assert.Equal(1, chunk.GetIndex(0, 10));
        Assert.Equal(1, chunk.GetIndex(128, 10));
        Assert.Equal(0, chunk.GetIndex(30, 9));
        Assert.Equal(0, chunk.GetIndex(30, 11));

        // Rail corridor: 2 lanes x 3.5 m -> half-width 3.5 units; only column x = 200.
        Assert.Equal(2, chunk.GetIndex(20, 50));

        // River corridor: half-width 5 units around the diagonal -> exactly the gy == gx cells.
        Assert.Equal(3, chunk.GetIndex(60, 60));
        Assert.Equal(0, chunk.GetIndex(60, 61));
        Assert.Equal(0, chunk.GetIndex(61, 60));

        // Road x rail overlap at cell (20, 10): the later rail rule wins.
        Assert.Equal(2, chunk.GetIndex(20, 10));

        // Rail x river overlap at cell (20, 20): the later river rule wins.
        Assert.Equal(3, chunk.GetIndex(20, 20));

        // Unpainted ground keeps the default.
        Assert.Equal(0, chunk.GetIndex(64, 63));
    }

    [Fact]
    public void ExplicitWidthAttributeResolvesTheCorridorWidth()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // Width 30 -> half-width 15; lattice rows 90, 100, 110, 120 are within 15 of y = 105.
        var feature = LineFeature("roads", "wide", [(0d, 105d), (1280d, 105d)], new Dictionary<string, string> { ["largeur"] = "30" });
        var rule = new LineMaterialRule("roads", "dirt", WidthAttribute: "largeur");
        var chunk = GenerateSingle(grid!, [feature], rule, new MaterialPalette(["grass", "dirt"]));

        Assert.NotNull(chunk);
        Assert.Equal(1, chunk!.GetIndex(64, 9));
        Assert.Equal(1, chunk.GetIndex(64, 10));
        Assert.Equal(1, chunk.GetIndex(64, 11));
        Assert.Equal(1, chunk.GetIndex(64, 12));
        Assert.Equal(0, chunk.GetIndex(64, 8));
        Assert.Equal(0, chunk.GetIndex(64, 13));
    }

    [Fact]
    public void LanesAttributeTimesMetersPerLaneResolvesTheWidth()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // 4 lanes x 3.5 m = 14 m -> half-width 7; lattice rows 100 and 110 are within 7
        // of y = 105, rows 90 and 120 are 15 units away.
        var feature = LineFeature("roads", "lanes", [(0d, 105d), (1280d, 105d)], new Dictionary<string, string> { ["nb_voies"] = "4" });
        var rule = new LineMaterialRule("roads", "dirt", LanesAttribute: "nb_voies", MetersPerLane: 3.5d);
        var chunk = GenerateSingle(grid!, [feature], rule, new MaterialPalette(["grass", "dirt"]));

        Assert.NotNull(chunk);
        Assert.Equal(1, chunk!.GetIndex(64, 10));
        Assert.Equal(1, chunk.GetIndex(64, 11));
        Assert.Equal(0, chunk.GetIndex(64, 9));
        Assert.Equal(0, chunk.GetIndex(64, 12));
    }

    [Fact]
    public void ClassAttributeSelectsTheClassWidth()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var widths = new Dictionary<string, double> { ["Autoroute"] = 20d, ["Chemin"] = 4d };
        var autoroute = LineFeature("roads", "a", [(0d, 100d), (1280d, 100d)], new Dictionary<string, string> { ["nature"] = "Autoroute" });
        var chemin = LineFeature("roads", "c", [(0d, 500d), (1280d, 500d)], new Dictionary<string, string> { ["nature"] = "Chemin" });
        var rule = new LineMaterialRule("roads", "dirt", ClassAttribute: "nature", ClassWidthsMeters: widths);
        var chunk = GenerateSingle(grid!, [autoroute, chemin], rule, new MaterialPalette(["grass", "dirt"]));

        Assert.NotNull(chunk);

        // Autoroute: half-width 10 -> rows 9, 10, 11.
        Assert.Equal(1, chunk!.GetIndex(64, 9));
        Assert.Equal(1, chunk.GetIndex(64, 10));
        Assert.Equal(1, chunk.GetIndex(64, 11));
        Assert.Equal(0, chunk.GetIndex(64, 8));
        Assert.Equal(0, chunk.GetIndex(64, 12));

        // Chemin: half-width 2 -> only row 50.
        Assert.Equal(1, chunk.GetIndex(100, 50));
        Assert.Equal(0, chunk.GetIndex(100, 49));
        Assert.Equal(0, chunk.GetIndex(100, 51));
    }

    [Fact]
    public void DefaultWidthMetersIsTheFinalFallback()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var feature = LineFeature("roads", "plain", [(0d, 100d), (1280d, 100d)]);
        var rule = new LineMaterialRule("roads", "dirt", DefaultWidthMeters: 10d);
        var chunk = GenerateSingle(grid!, [feature], rule, new MaterialPalette(["grass", "dirt"]));

        Assert.NotNull(chunk);
        Assert.Equal(1, chunk!.GetIndex(64, 10));
        Assert.Equal(0, chunk.GetIndex(64, 9));
        Assert.Equal(0, chunk.GetIndex(64, 11));
    }

    [Fact]
    public void UnresolvableWidthFailsTheChunkWithAnActionableError()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        // The feature lacks the width attribute and the rule declares no fallback.
        var feature = LineFeature("roads", "bare", [(0d, 100d), (1280d, 100d)]);
        var rule = new LineMaterialRule("roads", "dirt", WidthAttribute: "largeur");
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification: null,
            [feature],
            classificationRules: null,
            polygonRules: null,
            new MaterialPalette(["grass", "dirt"]),
            0,
            0,
            diagnostics: diagnostics,
            lineRules: [rule]);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(GeoDiagnosticCodes.GenerateFailure, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
        Assert.Contains("lineMaterialRules[0]", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("roads/bare#0", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LineCorridorsOverrideTheClassificationBase()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var classification = Classification(1, 1, RasterSampleFormat.UInt8, static (_, _) => 10);
        var classificationRules = new List<MaterialRule> { new("grass", ClassificationValue: 10) };
        var feature = LineFeature("roads", "r", [(0d, 100d), (1280d, 100d)]);
        var rule = new LineMaterialRule("roads", "dirt", DefaultWidthMeters: 10d);
        var palette = new MaterialPalette(["grass", "dirt"]);
        var chunk = CreateGenerator().Generate(
            grid!,
            classification,
            [feature],
            classificationRules,
            polygonRules: null,
            palette,
            0,
            0,
            lineRules: [rule]);

        Assert.NotNull(chunk);
        Assert.Equal(1, chunk!.GetIndex(64, 10));
        Assert.Equal(0, chunk.GetIndex(64, 9));
        Assert.Equal(0, chunk.GetIndex(64, 11));
    }

    [Fact]
    public void PolygonRulesOverrideLineCorridors()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var road = LineFeature("roads", "r", [(0d, 100d), (1280d, 100d)]);
        var plaza = PolygonFeature("vectors", "plaza", 100, 50, 300, 150);
        var mappings = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, Geometry: ManifestFeatureGeometry.Polygon, Material: "concrete"),
        };

        var rule = new LineMaterialRule("roads", "dirt", DefaultWidthMeters: 10d);
        var palette = new MaterialPalette(["grass", "dirt", "concrete"]);
        var chunk = CreateGenerator().Generate(
            grid!,
            classification: null,
            [road, plaza],
            classificationRules: null,
            mappings,
            palette,
            0,
            0,
            lineRules: [rule]);

        Assert.NotNull(chunk);

        // Corridor inside the plaza is repainted by the polygon; corridor outside keeps dirt.
        Assert.Equal(2, chunk!.GetIndex(15, 10));
        Assert.Equal(1, chunk.GetIndex(50, 10));
    }

    [Fact]
    public void DiagonalCorridorStaysContinuousAcrossChunkBoundaries()
    {
        var grid = CreateGrid(widthMeters: 2560d, heightMeters: 2560d);
        Assert.NotNull(grid);

        var feature = LineFeature("rivers", "diag", [(0d, 0d), (2560d, 2560d)]);
        var rule = new LineMaterialRule("rivers", "sand", DefaultWidthMeters: 20d);
        var palette = new MaterialPalette(["grass", "sand"]);
        var generator = CreateGenerator();

        // Half-width 10 game units around y = x: painted exactly where |gy - gx| <= 1.
        // Chunk (0,0) and (1,1) each hold 129 diagonal cells plus 128 cells on either
        // offset diagonal; the off-diagonal chunks hold only the two corner cells.
        var paintedPerChunk = new Dictionary<(int ChunkX, int ChunkY), List<(int Gx, int Gy)>>();
        for (var cy = 0; cy < 2; cy++)
        {
            for (var cx = 0; cx < 2; cx++)
            {
                var first = generator.Generate(
                    grid!, classification: null, [feature], null, null, palette, cx, cy, lineRules: [rule]);
                Assert.NotNull(first);

                var second = generator.Generate(
                    grid!, classification: null, [feature], null, null, palette, cx, cy, lineRules: [rule]);
                Assert.NotNull(second);

                Assert.Equal(first!.ContentSha256Hex, second!.ContentSha256Hex);

                var painted = new List<(int Gx, int Gy)>();
                for (var ly = 0; ly <= ChunkLayout.IntervalsPerChunk; ly++)
                {
                    for (var lx = 0; lx <= ChunkLayout.IntervalsPerChunk; lx++)
                    {
                        if (first.GetIndex(lx, ly) == 1)
                        {
                            painted.Add(((cx * ChunkLayout.IntervalsPerChunk) + lx, (cy * ChunkLayout.IntervalsPerChunk) + ly));
                        }
                    }
                }

                paintedPerChunk[(cx, cy)] = painted;
            }
        }

        // Every painted cell lies within the analytic corridor, including the shared edge
        // cell (128, 128) painted by both adjacent chunks.
        Assert.Contains((128, 128), paintedPerChunk[(0, 0)]);
        Assert.Contains((128, 128), paintedPerChunk[(1, 1)]);
        foreach (var painted in paintedPerChunk.Values)
        {
            Assert.All(painted, cell => Assert.True(
                Math.Abs(cell.Gy - cell.Gx) <= 1,
                $"Cell ({cell.Gx}, {cell.Gy}) is painted but outside the corridor."));
        }

        Assert.Equal(385, paintedPerChunk[(0, 0)].Count);
        Assert.Equal(3, paintedPerChunk[(1, 0)].Count);
        Assert.Equal(3, paintedPerChunk[(0, 1)].Count);
        Assert.Equal(385, paintedPerChunk[(1, 1)].Count);
    }

    [Fact]
    public void CorridorsClipAtTheMapEdges()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var crossing = LineFeature("roads", "cross", [(-500d, 100d), (3000d, 100d)]);
        var outside = LineFeature("roads", "out", [(-500d, -500d), (3000d, -500d)]);
        var rule = new LineMaterialRule("roads", "dirt", DefaultWidthMeters: 10d);
        var chunk = GenerateSingle(grid!, [crossing, outside], rule, new MaterialPalette(["grass", "dirt"]));

        Assert.NotNull(chunk);

        // The corridor is truncated at the map edge; nothing outside is written.
        Assert.Equal(1, chunk!.GetIndex(0, 10));
        Assert.Equal(1, chunk.GetIndex(128, 10));
        Assert.Equal(1, chunk.GetIndex(64, 10));
        Assert.Equal(0, chunk.GetIndex(64, 9));
        Assert.Equal(0, chunk.GetIndex(64, 11));
        Assert.Equal(0, chunk.GetIndex(64, 0));
    }

    [Fact]
    public void UnknownLineRuleMaterialFailsEvenWithoutFeatures()
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
            new MaterialPalette(["grass"]),
            0,
            0,
            diagnostics: diagnostics,
            lineRules: [new LineMaterialRule("roads", "asphalt", DefaultWidthMeters: 8d)]);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialNameUnknown, diagnostic.Code);
        Assert.Contains("lineMaterialRules[0]", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("asphalt", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CorridorWorkIsBudgetedLikePolygonWork()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var feature = LineFeature("roads", "long", [(0d, 100d), (1280d, 100d)]);
        var rule = new LineMaterialRule("roads", "dirt", DefaultWidthMeters: 10d);
        var diagnostics = new GeoDiagnosticBag();

        var chunk = CreateGenerator().Generate(
            grid!,
            classification: null,
            [feature],
            null,
            null,
            new MaterialPalette(["grass", "dirt"]),
            0,
            0,
            diagnostics: diagnostics,
            limits: new GeospatialImportLimits(MaxVectorWorkPerChunk: 8),
            lineRules: [rule]);

        Assert.Null(chunk);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(MaterialDiagnosticCodes.MaterialVectorWorkLimitExceeded, diagnostic.Code);
    }

    [Fact]
    public void EmptyLineRulesAreByteIdenticalToNoLineRules()
    {
        var grid = CreateGrid();
        Assert.NotNull(grid);

        var feature = LineFeature("roads", "r", [(0d, 100d), (1280d, 100d)]);
        var palette = new MaterialPalette(["grass", "dirt"]);
        var generator = CreateGenerator();

        var omitted = generator.Generate(grid!, null, [feature], null, null, palette, 0, 0);
        var emptyList = generator.Generate(grid!, null, [feature], null, null, palette, 0, 0, lineRules: []);
        var emptyArray = generator.Generate(grid!, null, [feature], null, null, palette, 0, 0, lineRules: new List<LineMaterialRule>());

        Assert.NotNull(omitted);
        Assert.NotNull(emptyList);
        Assert.NotNull(emptyArray);
        Assert.Equal(omitted!.ContentSha256Hex, emptyList!.ContentSha256Hex);
        Assert.Equal(omitted.ContentSha256Hex, emptyArray!.ContentSha256Hex);
        Assert.Equal(0, omitted.GetIndex(64, 10));
    }

    [Fact]
    public void ValidatorRejectsInvalidLineMaterialRules()
    {
        var cases = new[]
        {
            (
                Json: """[{ "sourceId": "roads", "material": "asphalt", "defaultWidthMeters": 8 }]""",
                Fragment: "not an entry of the palette",
                Code: GeoDiagnosticCodes.ManifestValueInvalid
            ),
            (
                Json: """[{ "sourceId": "roads", "material": "dirt" }]""",
                Fragment: "declares no width resolution path",
                Code: GeoDiagnosticCodes.ManifestValueInvalid
            ),
            (
                Json: """[{ "sourceId": "roads", "material": "dirt", "defaultWidthMeters": -4 }]""",
                Fragment: "defaultWidthMeters must be a positive finite number",
                Code: GeoDiagnosticCodes.ManifestValueInvalid
            ),
            (
                Json: """[{ "material": "dirt", "defaultWidthMeters": 8 }]""",
                Fragment: "sourceId is required",
                Code: GeoDiagnosticCodes.ManifestMissingRequired
            ),
            (
                Json: """[{ "sourceId": "canals", "material": "dirt", "defaultWidthMeters": 8 }]""",
                Fragment: "does not match any declared source",
                Code: GeoDiagnosticCodes.ManifestValueInvalid
            ),
            (
                Json: """[{ "sourceId": "dem", "material": "dirt", "lanesAttribute": "nb_voies", "metersPerLane": 3.5 }]""",
                Fragment: "must reference a source with role 'vector' or 'extent'",
                Code: GeoDiagnosticCodes.ManifestValueInvalid
            ),
            (
                Json: """[{ "sourceId": "roads", "material": "dirt", "metersPerLane": 3.5, "defaultWidthMeters": 8 }]""",
                Fragment: "metersPerLane requires lineMaterialRules[0].lanesAttribute",
                Code: GeoDiagnosticCodes.ManifestValueInvalid
            ),
            (
                Json: """[{ "sourceId": "roads", "material": "dirt", "classWidthsMeters": { "Autoroute": 20 }, "defaultWidthMeters": 8 }]""",
                Fragment: "classWidthsMeters requires lineMaterialRules[0].classAttribute",
                Code: GeoDiagnosticCodes.ManifestValueInvalid
            ),
            (
                Json: """[{ "sourceId": "roads", "material": "dirt", "classAttribute": "nature", "classWidthsMeters": { "Autoroute": -1 }, "defaultWidthMeters": 8 }]""",
                Fragment: "must be a positive finite number",
                Code: GeoDiagnosticCodes.ManifestValueInvalid
            )
        };

        foreach (var (json, fragment, code) in cases)
        {
            using var project = ManifestProject.Create($"\"lineMaterialRules\": {json}");
            var diagnostics = new GeoDiagnosticBag();
            var manifest = ManifestLoader.Load(project.ManifestPath, diagnostics);

            Assert.True(manifest is null, $"a rule that must be rejected loaded anyway: {json}");
            Assert.Contains(
                diagnostics.Items,
                diagnostic => diagnostic.Code == code && diagnostic.Message.Contains(fragment, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ValidatorAcceptsCompleteLineMaterialRules()
    {
        var json = """
            [
              {
                "sourceId": "roads",
                "material": "dirt",
                "defaultWidthMeters": 4,
                "widthAttribute": "largeur_de_chaussee",
                "lanesAttribute": "nombre_de_voies",
                "metersPerLane": 3.5,
                "classAttribute": "nature",
                "classWidthsMeters": { "Autoroute": 20, "Route empierrée": 4, "Sentier": 2 }
              }
            ]
            """;
        using var project = ManifestProject.Create($"\"lineMaterialRules\": {json}");
        var diagnostics = new GeoDiagnosticBag();
        var manifest = ManifestLoader.Load(project.ManifestPath, diagnostics);

        Assert.NotNull(manifest);
        Assert.Empty(diagnostics.Items);

        var rule = Assert.Single(manifest!.LineMaterialRules);
        Assert.Equal("roads", rule.SourceId);
        Assert.Equal("dirt", rule.Material);
        Assert.Equal(4d, rule.DefaultWidthMeters);
        Assert.Equal("largeur_de_chaussee", rule.WidthAttribute);
        Assert.Equal("nombre_de_voies", rule.LanesAttribute);
        Assert.Equal(3.5d, rule.MetersPerLane);
        Assert.Equal("nature", rule.ClassAttribute);
        Assert.Equal(20d, rule.ClassWidthsMeters!["Autoroute"]);
        Assert.Equal(2d, rule.ClassWidthsMeters["Sentier"]);
    }

    [Fact]
    public void FullPipelinePaintsRoadsAndIsDeterministic()
    {
        using var project = ImportProjectFixture.Create(
            imagery: false,
            classification: false,
            vectors: false,
            manifest: false);

        WriteRoadsKml(project.Root);
        var demRelative = Directory
            .EnumerateFiles(project.Root, "raster.tif", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(project.Root, file).Replace('\\', '/'))
            .Single();

        File.WriteAllText(
            project.ManifestPath,
            $$"""
            {
              "sources": [
                { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                { "id": "dem", "kind": "geotiff", "path": "{{demRelative}}", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1.5, "horizontalAccuracyMeters": 0.6 },
                { "id": "roads", "kind": "kml", "path": "roads.kml", "role": "vector" }
              ],
              "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
              "metersPerGameUnit": 1,
              "defaultMaterial": "grass",
              "extentFeatureId": "playable",
              "materialRules": [ { "material": "dirt", "attribute": "paletteEntry" } ],
              "lineMaterialRules": [
                { "sourceId": "roads", "material": "dirt", "widthAttribute": "largeur", "defaultWidthMeters": 8 }
              ],
              "rasterPolicy": { "imageryPixelsPerSample": 1 }
            }
            """);

        var first = project.OutputPath("out-a");
        var second = project.OutputPath("out-b");

        var firstResult = GeospatialImporter.Run(project.ManifestPath, first);
        var secondResult = GeospatialImporter.Run(project.ManifestPath, second, new GeoImportOptions(), CancellationToken.None);

        Assert.True(
            firstResult.Success,
            string.Join("\n", firstResult.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.True(
            secondResult.Success,
            string.Join("\n", secondResult.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));

        // Same input, independently staged runs: identical payload and report bytes.
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(first, "payload.json")),
            File.ReadAllBytes(Path.Combine(second, "payload.json")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(first, "report.json")),
            File.ReadAllBytes(Path.Combine(second, "report.json")));

        var readBag = new GeoDiagnosticBag();
        using var reader = GeneratedMapPayloadReader.Open(first, readBag);
        Assert.NotNull(reader);
        Assert.False(readBag.HasErrors);

        var dirtIndex = ReaderPaletteIndex(reader!, "dirt");
        var material = reader!.ReadMaterial(0, 0, readBag);
        Assert.NotNull(material);

        // The 8 m road along y = 100 covers exactly the lattice row gy = 10.
        for (var gx = 0; gx <= ChunkLayout.IntervalsPerChunk; gx++)
        {
            Assert.Equal(dirtIndex, material!.GetIndex(gx, 10));
        }

        Assert.Equal(0, material!.GetIndex(64, 9));
        Assert.Equal(0, material.GetIndex(64, 11));
    }

    private static byte ReaderPaletteIndex(GeneratedMapPayloadReader reader, string name)
    {
        Assert.True(reader.Metadata.Palette.TryGetIndex(name, out var index), $"the palette is missing '{name}'");
        Assert.True(index > 0, "a non-default material must not be palette entry 0");
        return index;
    }

    private static void WriteRoadsKml(string root)
    {
        var ring = string.Join(
            " ",
            ImportProjectFixture.LonLat(0d, 100d),
            ImportProjectFixture.LonLat(1279d, 100d));

        File.WriteAllText(
            Path.Combine(root, "roads.kml"),
            "<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><Folder><name>roads</name>"
            + "<Placemark id=\"road-1\"><name>road-1</name><ExtendedData>"
            + "<Data name=\"largeur\"><value>8</value></Data>"
            + "</ExtendedData><LineString><coordinates>" + ring + "</coordinates></LineString></Placemark>"
            + "</Folder></Document></kml>");
    }

    private static MaterialChunk? GenerateSingle(
        LocalMapGrid grid,
        IReadOnlyList<LocalFeature> features,
        LineMaterialRule rule,
        MaterialPalette palette)
    {
        return CreateGenerator().Generate(
            grid,
            classification: null,
            features,
            classificationRules: null,
            polygonRules: null,
            palette,
            0,
            0,
            lineRules: [rule]);
    }

    private sealed class ManifestProject : IDisposable
    {
        private ManifestProject(string root)
        {
            Root = root;
        }

        internal string Root { get; }

        internal string ManifestPath => Path.Combine(Root, "manifest.json");

        internal static ManifestProject Create(string lineRulesClause)
        {
            var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g6-line-rules", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "area.kml"), "<kml/>");
            File.WriteAllText(Path.Combine(root, "dem.tif"), string.Empty);
            File.WriteAllText(Path.Combine(root, "roads.kml"), "<kml/>");

            File.WriteAllText(
                Path.Combine(root, "manifest.json"),
                $$"""
                {
                  "sources": [
                    { "id": "extent", "kind": "kml", "path": "area.kml", "role": "extent" },
                    { "id": "dem", "kind": "geotiff", "path": "dem.tif", "role": "elevation", "verticalDatum": "EGM96", "verticalAccuracyMeters": 1.5 },
                    { "id": "roads", "kind": "kml", "path": "roads.kml", "role": "vector" }
                  ],
                  "targetCoordinateSystem": { "type": "utm", "zone": 31, "hemisphere": "north" },
                  "metersPerGameUnit": 1,
                  "defaultMaterial": "grass",
                  "extentFeatureId": "playable",
                  "materialRules": [ { "material": "dirt", "attribute": "paletteEntry" } ],
                  {{lineRulesClause}}
                }
                """);

            return new ManifestProject(root);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static MaterialLayerGenerator CreateGenerator()
    {
        return new MaterialLayerGenerator(SamplingTestSupport.IdentityTransformer());
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
        Func<int, int, double> value)
    {
        var width = (chunksX * ChunkLayout.IntervalsPerChunk) + 3;
        var height = (chunksY * ChunkLayout.IntervalsPerChunk) + 3;
        var transform = SamplingTestSupport.SouthUp(OriginEasting - 15d, OriginNorthing - 15d, 10d, 10d);
        var samples = new double[width * height];

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                samples[(row * width) + column] = value(column - 1, row - 1);
            }
        }

        return new GeospatialRaster<double>(
            width,
            height,
            1,
            transform,
            Tm4,
            samples,
            sampleFormat: format);
    }

    private static LocalMapGrid? CreateGrid(double widthMeters = 1280d, double heightMeters = 1280d)
    {
        var sources = new List<ManifestSource>
        {
            new("extent", ManifestSourceKind.Kml, "area.kml", ManifestSourceRole.Extent),
            new("dem", ManifestSourceKind.Geotiff, "dem.tif", ManifestSourceRole.Elevation, "EGM96"),
            new("roads", ManifestSourceKind.Kml, "roads.kml", ManifestSourceRole.Vector),
        };

        var manifest = new GeospatialImportManifest
        {
            Sources = sources,
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(ManifestCrsKind.TransverseMercator, CentralMeridianDegrees: 4d),
            MetersPerGameUnit = 1d,
            DefaultMaterial = "grass",
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

    private static LocalFeature LineFeature(
        string sourceId,
        string featureId,
        (double X, double Y)[] vertices,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var positions = new List<GeoPosition>(vertices.Length);
        foreach (var (x, y) in vertices)
        {
            positions.Add(new GeoPosition(new Vector2d(x, y)));
        }

        var geometry = new GeoLineString(positions);
        var source = new GeospatialFeature(sourceId, featureId, geometry, CoordinateSystemId.Wgs84, attributes);

        return new LocalFeature(geometry, source, sourceId + "/" + featureId + "#0");
    }

    private static LocalFeature PolygonFeature(
        string sourceId,
        string featureId,
        double west,
        double south,
        double east,
        double north)
    {
        var geometry = new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(west, south)),
                new GeoPosition(new Vector2d(east, south)),
                new GeoPosition(new Vector2d(east, north)),
                new GeoPosition(new Vector2d(west, north)),
                new GeoPosition(new Vector2d(west, south)),
            ]));

        var source = new GeospatialFeature(sourceId, featureId, geometry, CoordinateSystemId.Wgs84);

        return new LocalFeature(geometry, source, sourceId + "/" + featureId + "#0");
    }
}
