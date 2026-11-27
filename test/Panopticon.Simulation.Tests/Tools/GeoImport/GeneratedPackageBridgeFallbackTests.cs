#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Preview;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Fallback parity on the installed game: a marked bridge that cannot take the level path has the
/// converter's landing band applied at runtime over the converter's lattice, so the stored lattice
/// reproduces the pregraded package bit for bit outside the deck footprint; the real failure branches
/// (short route, route without approach room, mixed bridges, unsupported template) fall back whole,
/// and the handoff marker predicates separate the versions.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageBridgeFallbackTests
{
    /// <summary>The converter band's stored elevation at lattice (55, 27) of the standard line (measured).</summary>
    private const int BandElevationAt55x27 = 27734;

    private static readonly (double X, double Y) StandardStart = (550d, 300d);

    private static readonly (double X, double Y) StandardEnd = (550d, 1000d);

    [GameFact(SageGame.CncGenerals)]
    public void Fallback_LatticeIsBitIdenticalToTheConverterBand_OutsideTheDeckFootprint()
    {
        WithFixture([((StandardStart), (StandardEnd))], (game, packageMarked, packageBand) =>
        {
            game.LoadMap(packageBand, [], GameType.SinglePlayer);
            var converterLattice = CopyLattice(game.TerrainLogic.HeightMap);
            Assert.Equal(BandElevationAt55x27, LatticeAt(converterLattice, 55, 27));

            Bridge.GeneratedPathPlanEnabled = false;

            try
            {
                game.LoadMap(packageMarked, [], GameType.SinglePlayer);
                var fallbackLattice = CopyLattice(game.TerrainLogic.HeightMap);
                Assert.Equal(BandElevationAt55x27, LatticeAt(fallbackLattice, 55, 27));

                var compared = 0;
                var mismatches = 0;
                var insideBelow = 0;
                var insideAbove = 0;

                for (var y = 0; y < LatticeHeight(game.TerrainLogic.HeightMap); y++)
                {
                    for (var x = 0; x < LatticeWidth(game.TerrainLogic.HeightMap); x++)
                    {
                        var index = (y * LatticeWidth(game.TerrainLogic.HeightMap)) + x;

                        if (IsInsideDeckFootprint(x, y, 260, 1040))
                        {
                            if (fallbackLattice[index] > converterLattice[index])
                            {
                                insideAbove++;
                            }
                            else if (fallbackLattice[index] < converterLattice[index])
                            {
                                insideBelow++;
                            }

                            continue;
                        }

                        compared++;

                        if (fallbackLattice[index] != converterLattice[index])
                        {
                            mismatches++;
                        }
                    }
                }

                Assert.Equal(0, mismatches);
                Assert.Equal(0, insideAbove);
                Assert.True(insideBelow > 0, "the clearance cut must lower some vertex inside the footprint.");
            }
            finally
            {
                Bridge.GeneratedPathPlanEnabled = true;
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void ShortRoute_FallsBackWithTheBand_NothingPartlyPlaced()
    {
        WithFixture([((550d, 300d), (550d, 600d))], (game, packageMarked, packageBand) =>
        {
            game.LoadMap(packageMarked, [], GameType.SinglePlayer);
            var bridge = game.Scene3D.Bridges.Single(candidate => candidate.StartPoint == new Vector2(550f, 300f));

            Assert.Null(bridge.LevelLayout);
            Assert.Null(bridge.CorridorPlan);
            Assert.Null(bridge.DeckModel);
            Assert.True(bridge.IsPlaced);
            Assert.NotEmpty(bridge.Meshes);
            Assert.NotEmpty(bridge.GetRawWorldFaces());
            Assert.Equal(0f, game.TerrainLogic.GetBridgeCorridorPoseWeight(new[] { new Vector2(550f, 450f) }));
            Assert.True(game.TerrainLogic.IsWithinGeneratedBridgeExtent(550f, 450f));

            var towerNames = TowerNames(game);
            Assert.Equal(4, game.Scene3D.GameObjects.Objects.Count(obj => towerNames.Contains(obj.Definition.Name)));

            AssertLatticeMatchesConverterBand(game, packageMarked, packageBand, 260, 640);

            var (start, end) = FindBridgePair(game);
            using var byHand = BuildMarkedBridge(game, start, end);
            var decision = GeneratedBridgePathDecision.Decide(byHand, game.TerrainLogic.HeightMap);

            Assert.False(decision.TakesLevelPath);
            Assert.StartsWith("level layout unsupported", decision.Reason!, StringComparison.Ordinal);
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void RouteNearTheMapEdge_FallsBackWithTheBand()
    {
        WithFixture([((550d, 150d), (550d, 850d))], (game, packageMarked, packageBand) =>
        {
            game.LoadMap(packageMarked, [], GameType.SinglePlayer);
            var bridge = game.Scene3D.Bridges.Single(candidate => candidate.StartPoint == new Vector2(550f, 150f));

            Assert.Null(bridge.LevelLayout);
            Assert.Null(bridge.CorridorPlan);
            Assert.Null(bridge.DeckModel);
            Assert.True(bridge.IsPlaced);
            Assert.NotEmpty(bridge.Meshes);
            Assert.NotEmpty(bridge.GetRawWorldFaces());
            Assert.Equal(0f, game.TerrainLogic.GetBridgeCorridorPoseWeight(new[] { new Vector2(550f, 450f) }));
            Assert.True(game.TerrainLogic.IsWithinGeneratedBridgeExtent(550f, 450f));

            AssertLatticeMatchesConverterBand(game, packageMarked, packageBand, 110, 890);

            var (start, end) = FindBridgePair(game);
            using var byHand = BuildMarkedBridge(game, start, end);
            var decision = GeneratedBridgePathDecision.Decide(byHand, game.TerrainLogic.HeightMap);

            Assert.False(decision.TakesLevelPath);
            Assert.StartsWith("corridor unsupported", decision.Reason!, StringComparison.Ordinal);
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void TwoMarkedBridges_EachTakesItsOwnPath()
    {
        WithFixture([(StandardStart, StandardEnd), ((1500d, 300d), (1500d, 600d))], (game, packageMarked, _) =>
        {
            game.LoadMap(packageMarked, [], GameType.SinglePlayer);

            var level = game.Scene3D.Bridges.Single(candidate => candidate.StartPoint == new Vector2(550f, 300f));
            var fallback = game.Scene3D.Bridges.Single(candidate => candidate.StartPoint == new Vector2(1500f, 300f));

            Assert.NotNull(level.LevelLayout);
            Assert.True(level.IsPlaced);
            Assert.Null(fallback.LevelLayout);
            Assert.True(fallback.IsPlaced);
            Assert.NotEmpty(fallback.Meshes);

            Assert.InRange(
                game.TerrainLogic.GetBridgeCorridorPoseWeight(new[] { new Vector2(550f, 650f) }),
                0.999f,
                1.001f);

            var heights = game.TerrainLogic.HeightMap;
            Assert.InRange(heights.GetHeight(550f, 270f), 1101.25d - 0.01d, 1101.25d + 0.01d);

            // The second line's band value at (1500, 270): native 1201.0, deck 1209.0, 30 units along.
            var expectedBand = ResidentGridHeight(Graded(ResidentGridHeight(1201f), 1209d, 30d));
            Assert.InRange(heights.GetHeight(1500f, 270f), expectedBand - 0.04f, expectedBand + 0.04f);
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void UnsupportedTemplatePackage_NoRuntimeChange_NoCorridor()
    {
        WithFixture([(StandardStart, StandardEnd)], (game, _, packageBand) =>
        {
            game.LoadMap(packageBand, [], GameType.SinglePlayer);

            Assert.False(game.TerrainLogic.HasBridgeCorridors);
            Assert.False(game.TerrainLogic.IsWithinGeneratedBridgeExtent(550f, 650f));

            var lattice = CopyLattice(game.TerrainLogic.HeightMap);
            Assert.Equal(BandElevationAt55x27, LatticeAt(lattice, 55, 27));
        });
    }

    /// <summary>Lattice index space is border-less: data index = lattice index + border (0 on these maps).</summary>
    private static int LatticeWidth(HeightMap heightMap)
    {
        return heightMap.Width + 1;
    }

    private static int LatticeHeight(HeightMap heightMap)
    {
        return heightMap.Height + 1;
    }

    private static int LatticeAt(ushort[] lattice, int x, int y)
    {
        return lattice[(y * LatticeWidthOf(lattice)) + x];
    }

    private static int LatticeWidthOf(ushort[] lattice)
    {
        return (int)Math.Sqrt(lattice.Length) is var side && side * side == lattice.Length ? side : throw new InvalidOperationException("lattice is not square.");
    }

    private static ushort[] CopyLattice(HeightMap heightMap)
    {
        var width = LatticeWidth(heightMap);
        var height = LatticeHeight(heightMap);
        var elevations = new ushort[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                elevations[(y * width) + x] = (ushort)heightMap.GetElevation(x, y);
            }
        }

        return elevations;
    }

    /// <summary>True when the lattice point's world position sits over the standard bridge's deck (the clearance-cut region).</summary>
    private static bool IsInsideDeckFootprint(int x, int y, int footprintMinY, int footprintMaxY)
    {
        var worldX = x * HeightMap.HorizontalScale;
        var worldY = y * HeightMap.HorizontalScale;

        return worldX >= 500 && worldX <= 600 && worldY >= footprintMinY && worldY <= footprintMaxY;
    }

    private static void AssertLatticeMatchesConverterBand(Game game, string packageMarked, string packageBand, int footprintMinY, int footprintMaxY)
    {
        game.LoadMap(packageBand, [], GameType.SinglePlayer);
        var converterLattice = CopyLattice(game.TerrainLogic.HeightMap);

        game.LoadMap(packageMarked, [], GameType.SinglePlayer);
        var fallbackLattice = CopyLattice(game.TerrainLogic.HeightMap);

        var mismatches = 0;

        for (var y = 0; y < LatticeHeight(game.TerrainLogic.HeightMap); y++)
        {
            for (var x = 0; x < LatticeWidth(game.TerrainLogic.HeightMap); x++)
            {
                if (IsInsideDeckFootprint(x, y, footprintMinY, footprintMaxY))
                {
                    continue;
                }

                var index = (y * LatticeWidth(game.TerrainLogic.HeightMap)) + x;

                if (fallbackLattice[index] != converterLattice[index])
                {
                    mismatches++;
                }
            }
        }

        Assert.Equal(0, mismatches);
    }

    private static Bridge BuildMarkedBridge(Game game, MapObject start, MapObject end)
    {
        var copy = new MapObject(start.Position, start.Angle, start.RoadType, start.TypeName);
        copy.Properties.AddAsciiString(Bridge.GeneratedProvenanceKey, Bridge.GeneratedProvenanceValue);
        copy.Properties.AddInteger(GeneratedBridgePathHandoff.Key, GeneratedBridgePathHandoff.Version);

        return new Bridge(game.Scene3D.GameEngine, copy, copy.Position, end.Position);
    }

    private static (MapObject Start, MapObject End) FindBridgePair(Game game)
    {
        var objects = game.Scene3D.MapFile.ObjectsList.Objects;

        for (var index = 0; index + 1 < objects.Length; index++)
        {
            if ((objects[index].RoadType & RoadType.PrimaryType) == RoadType.BridgeStart && objects[index + 1].RoadType.HasFlag(RoadType.BridgeEnd))
            {
                return (objects[index], objects[index + 1]);
            }
        }

        throw new InvalidOperationException("the fixture map has no bridge pair.");
    }

    private static HashSet<string> TowerNames(Game game)
    {
        var template = game.AssetStore.BridgeTemplates.GetByName("Concrete")!;

        return new HashSet<string>(StringComparer.Ordinal)
        {
            template.TowerObjectNameFromLeft.Value.Name,
            template.TowerObjectNameFromRight.Value.Name,
            template.TowerObjectNameToLeft.Value.Name,
            template.TowerObjectNameToRight.Value.Name,
        };
    }

    private static double Graded(double current, double deckZ, double along)
    {
        var rampT = 1d - (along / 40d);
        var weight = (rampT * rampT) * (3d - (2d * rampT));
        var target = deckZ - 1d;
        var graded = current + ((target - current) * weight);

        return Math.Clamp(graded, Math.Min(current, deckZ), Math.Max(current, deckZ));
    }

    private static float ResidentGridHeight(double value)
    {
        const double step = 0.0390625d;
        return (float)(Math.Round(value / step, MidpointRounding.ToEven) * step);
    }

    private static void WithFixture(IReadOnlyList<((double X, double Y) Start, (double X, double Y) End)> bridgeLines, Action<Game, string, string> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-fallback-parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"), bridgeLines);
            var packageMarked = Path.Combine(scratchRoot, "package-marked");
            var packageBand = Path.Combine(scratchRoot, "package-band");

            Convert(payloadDir, packageMarked, withRules: true);
            Convert(payloadDir, packageBand, withRules: true, "ConcreteOneLane");

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            body(game, packageMarked, packageBand);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Fixture helpers (copied from GeneratedPackageBridgePathTests, parameterized over lines).
    // ------------------------------------------------------------------

    private static string StagePayload(string payloadDir, IReadOnlyList<((double X, double Y) Start, (double X, double Y) End)> bridgeLines)
    {
        var diagnostics = new GeoDiagnosticBag();
        var writer = GeneratedMapPayloadWriter.Create(payloadDir, [], diagnostics);
        Assert.NotNull(writer);
        Assert.False(diagnostics.HasErrors);

        var palette = new MaterialPalette(["grass", "dirt"]);
        var descriptors = new List<PayloadChunkDescriptor>();

        for (var cy = 0; cy < 2; cy++)
        {
            for (var cx = 0; cx < 2; cx++)
            {
                var heights = new float[ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis];

                for (var sy = 0; sy < ChunkLayout.StoredPointsPerChunkAxis; sy++)
                {
                    for (var sx = 0; sx < ChunkLayout.StoredPointsPerChunkAxis; sx++)
                    {
                        heights[(sy * ChunkLayout.StoredPointsPerChunkAxis) + sx] =
                            PayloadHeight((cx * ChunkLayout.IntervalsPerChunk) + sx - 1, (cy * ChunkLayout.IntervalsPerChunk) + sy - 1);
                    }
                }

                var terrain = writer.WriteTerrain(new TerrainChunk(cx, cy, heights));
                var materials = writer.WriteMaterial(new MaterialChunk(cx, cy, palette, new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis]));
                var masks = writer.WriteMask(new TerrainMaskChunk(cx, cy, new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis]));
                Assert.NotNull(terrain);
                Assert.NotNull(materials);
                Assert.NotNull(masks);

                descriptors.Add(new PayloadChunkDescriptor(cx, cy, terrain!, materials!, masks!, null));
            }
        }

        var features = bridgeLines
            .Select((line, index) => new GeneratedFeature(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(line.Start.X, line.Start.Y)), new GeoPosition(new Vector2d(line.End.X, line.End.Y))]),
                "vectors",
                $"bridge-{(char)('a' + index)}",
                $"vectors/bridge-{(char)('a' + index)}#0",
                -1))
            .ToList();

        var transform = new GeospatialMapTransform(
            [CoordinateSystemId.Wgs84],
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            new Vector2d(500000d, 6000000d),
            metersPerGameUnit: 1d,
            verticalDatum: "EGM96",
            verticalOriginMeters: 0d,
            verticalExaggeration: 1d);
        var grid = new PayloadGridDefinition(
            2,
            2,
            transform,
            new GeoPolygon(new GeoRing(
            [
                new GeoPosition(new Vector2d(500000d, 6000000d)),
                new GeoPosition(new Vector2d(512560d, 6000000d)),
                new GeoPosition(new Vector2d(512560d, 6012560d)),
                new GeoPosition(new Vector2d(500000d, 6012560d)),
                new GeoPosition(new Vector2d(500000d, 6000000d)),
            ])),
            null,
            new SlopeThresholds(45d, 15d));
        var metadata = new GeneratedMapPayloadMetadata(grid, palette, features, new string('7', 64), [], descriptors);
        var payloadSha256Hex = writer.WriteMetadata(metadata);
        Assert.NotNull(payloadSha256Hex);

        var report = new GeneratedMapPayloadReport(
            [new PayloadDiagnostic("GEO-VAL-001", GeoStage.Validate, GeoSeverity.Info, "Control points matched.")],
            new PayloadTerrainGenerationSummary(SamplingSummary(), 0, 0, 0),
            new PayloadMaskGenerationSummary(45d, 15d, 1d, 4, 0, 0, 0, 0, 0, 0, 0, 0),
            new PayloadFeatureGenerationSummary(
                features.Count,
                [.. Enum.GetValues<GeneratedFeatureKind>().Select(kind => new PayloadFeatureKindCount(kind, features.Count(feature => feature.Kind == kind)))]),
            [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Inventory complete.")],
            payloadSha256Hex);
        Assert.True(writer.WriteReport(report));
        Assert.True(writer.TryCommit());

        return payloadDir;
    }

    private static float PayloadHeight(long latticeX, long latticeY)
    {
        // Plane terrain with a north-south gorge strip the bridge crosses.
        var gorge = Math.Abs(latticeY - 64) < 4 ? -80f : 0f;

        return 1000f + (1.25f * latticeX) + (0.5f * latticeY) + gorge;
    }

    private static PayloadSamplingSummary SamplingSummary()
    {
        return new PayloadSamplingSummary(
            0,
            0,
            0,
            0,
            [.. Enum.GetValues<SampleKernel>().Select(kernel => new PayloadKernelSampleCount(kernel, 0))],
            new PayloadObservedRanges(
                gsdRatioX: new PayloadObservedRange(0.98d, 1.02d),
                gsdRatioY: new PayloadObservedRange(0.97d, 1.03d)));
    }

    private static void Convert(string payloadDir, string packageDir, bool withRules, string bridgeTemplate = "Concrete")
    {
        var diagnostics = new GeoDiagnosticBag();
        using var reader = GeneratedMapPayloadReader.Open(payloadDir, diagnostics);
        Assert.NotNull(reader);
        Assert.False(diagnostics.HasErrors);

        var textureMap = PreviewTextureMap.FromJson(
            """{"grass":"GrassType1","dirt":"DirtMediumType1"}""",
            new FixedTerrainTextureCatalog(["GrassType1", "DirtMediumType1"]),
            new GeoDiagnosticBag());
        Assert.NotNull(textureMap);

        var result = GeneratedMapPackageConverter.Convert(
            reader!,
            "r3-g8-fallback-parity",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors", bridgeTemplate)] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
