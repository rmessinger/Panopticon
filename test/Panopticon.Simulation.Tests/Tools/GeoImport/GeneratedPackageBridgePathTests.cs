#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
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
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Path decision wiring on the installed fixture: a marked bridge takes the level path (deck, corridor, towers,
/// graded ground), the dozer crosses it without a step, the disabled and unknown-handoff cases fall back to the
/// converter's landing band, and the supported-asset rule is exact.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageBridgePathTests
{
    private const string DozerDefinitionName = "AmericaVehicleDozer";

    private const double DatumZ = 1101.25d;

    private static readonly Vector2 RouteStart = new(550f, 300f);

    private static readonly Vector2 RouteEnd = new(550f, 1000f);

    [GameFact(SageGame.CncGenerals)]
    public void MarkedBridge_TakesTheLevelPath()
    {
        WithPackages((game, packageOn, packageOff) =>
        {
            var probes = new[] { new Vector2(450f, 650f), new Vector2(650f, 650f), new Vector2(550f, 1500f), new Vector2(200f, 200f) };

            game.LoadMap(packageOff, [], GameType.SinglePlayer);
            var graded = probes.Select(point => game.TerrainLogic.HeightMap.GetHeight(point.X, point.Y)).ToList();

            game.LoadMap(packageOn, [], GameType.SinglePlayer);
            var bridge = game.Scene3D.Bridges[0];

            Assert.True(bridge.IsPlaced);
            Assert.NotNull(bridge.LevelLayout);
            Assert.NotNull(bridge.CorridorPlan);
            Assert.NotNull(bridge.DeckModel);
            Assert.Empty(bridge.Meshes);

            var terrain = game.TerrainLogic;
            Assert.InRange(terrain.GetBridgeCorridorPoseWeight(new[] { new Vector2(550f, 650f) }), 0.999f, 1.001f);
            Assert.InRange(terrain.GetBridgeCorridorPoseWeight(new[] { new Vector2(550f, 1400f) }), -0.001f, 0.001f);

            var surfaceZ = terrain.GetGeneratedBridgeSurfaceHeight(550f, 650f, 1101f);
            Assert.NotNull(surfaceZ);
            Assert.InRange(surfaceZ!.Value, DatumZ - 0.01d, DatumZ + 0.01d);

            var heights = game.TerrainLogic.HeightMap;
            Assert.InRange(heights.GetHeight(550f, 270f), DatumZ - 0.01d, DatumZ + 0.01d);
            Assert.InRange(heights.GetHeight(550f, 300f), 1101.210938d - 0.04d, 1101.210938d + 0.04d);
            Assert.InRange(heights.GetHeight(550f, 970f), 1100.234375d - 0.01d, 1100.234375d + 0.01d);
            Assert.InRange(heights.GetHeight(550f, 150f), 1090.039062d - 0.08d, 1090.039062d + 0.08d);

            for (var index = 0; index < probes.Length; index++)
            {
                Assert.True(
                    MathF.Abs(heights.GetHeight(probes[index].X, probes[index].Y) - graded[index]) <= 1e-6f,
                    $"ground at ({probes[index].X}, {probes[index].Y}) is not the no-rules height.");
            }

            var towerNames = TowerNames(game);
            var towers = game.Scene3D.GameObjects.Objects.Where(obj => towerNames.Contains(obj.Definition.Name)).ToList();
            Assert.Equal(4, towers.Count);

            var layout = bridge.LevelLayout;
            var route = layout.Route;
            var lateral = layout.Faces
                .SelectMany(face => new[] { face.A, face.B, face.C })
                .Select(vertex => route.Project(new Vector2(vertex.X, vertex.Y)).N)
                .ToList();
            var lateralMin = lateral.Min();
            var lateralMax = lateral.Max();

            foreach (var tower in towers)
            {
                var position = tower.Translation;
                Assert.InRange(position.Z, DatumZ - 1e-3d, DatumZ + 1e-3d);

                var (along, across) = route.Project(new Vector2(position.X, position.Y));
                Assert.True(
                    Math.Abs(along) <= 0.5d || Math.Abs(along - route.Length) <= 0.5d,
                    $"tower along {along} is not at a deck end.");
                Assert.True(
                    Math.Abs(across - lateralMin) <= 0.5d || Math.Abs(across - lateralMax) <= 0.5d,
                    $"tower lateral {across} is not at a deck side ({lateralMin}, {lateralMax}).");
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void MarkedBridge_DozerCrossesWithoutAStep()
    {
        WithPackages((game, packageOn, _) =>
        {
            game.LoadMap(packageOn, [], GameType.SinglePlayer);

            var trace = RunDozerTraversal(game, (550f, 150f), (550f, 1150f));

            Assert.True(trace.Last().Y >= 1148f, $"the dozer stopped at y={trace.Last().Y}.");
            Assert.True(trace.Min(sample => sample.Z) >= 1060f, $"the dozer dipped to z={trace.Min(sample => sample.Z)}.");

            var largestStep = 0f;

            for (var index = 1; index < trace.Count; index++)
            {
                largestStep = MathF.Max(largestStep, MathF.Abs(trace[index].Z - trace[index - 1].Z));
            }

            Assert.True(largestStep < 2.0f, $"largest z step {largestStep} between consecutive samples.");
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void LevelPathDisabled_BridgeFallsBackToTheAuthoredPath()
    {
        WithPackages((game, packageOn, _) =>
        {
            Bridge.GeneratedPathPlanEnabled = false;

            try
            {
                game.LoadMap(packageOn, [], GameType.SinglePlayer);
                var bridge = game.Scene3D.Bridges[0];

                Assert.Null(bridge.LevelLayout);
                Assert.Null(bridge.CorridorPlan);
                Assert.Null(bridge.DeckModel);
                Assert.NotEmpty(bridge.Meshes);
                Assert.Equal(424, bridge.GetRawWorldFaces().Count);

                var terrain = game.TerrainLogic;
                Assert.InRange(terrain.GetBridgeCorridorPoseWeight(new[] { new Vector2(550f, 650f) }), -0.001f, 0.001f);
                Assert.True(terrain.IsWithinGeneratedBridgeExtent(550f, 650f));

                var heights = game.TerrainLogic.HeightMap;
                Assert.InRange(heights.GetHeight(550f, 300f), 1083.75d - 1e-3d, 1083.75d + 1e-3d);
                Assert.InRange(heights.GetHeight(550f, 270f), 1083.34375d - 0.04d, 1083.34375d + 0.04d);
            }
            finally
            {
                Bridge.GeneratedPathPlanEnabled = true;
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void UnknownHandoffVersion_FallsBackWithTheBand()
    {
        WithPackages((game, packageOn, _) =>
        {
            game.LoadMap(packageOn, [], GameType.SinglePlayer);
            var (start, end) = FindBridgePair(game);

            var copy = new MapObject(start.Position, start.Angle, start.RoadType, start.TypeName);
            copy.Properties.AddAsciiString(Bridge.GeneratedProvenanceKey, Bridge.GeneratedProvenanceValue);
            copy.Properties.AddInteger(GeneratedBridgePathHandoff.Key, 2);

            using var bridge = new Bridge(game.Scene3D.GameEngine, copy, copy.Position, end.Position);
            var decision = GeneratedBridgePathDecision.Decide(bridge, game.TerrainLogic.HeightMap);

            Assert.False(decision.TakesLevelPath);
            Assert.NotEmpty(decision.LandingBand);
            Assert.Contains("handoff version", decision.Reason);
        });
    }

    [Fact]
    public void IsSupportedAsset_RequiresTheSupportedTemplateAndGeometry()
    {
        var template = BridgeSurface.SupportedTemplateName;
        var fingerprint = BridgeSurface.SupportedGeometryFingerprint;

        Assert.True(GeneratedBridgePathDecision.IsSupportedAsset(template, fingerprint));
        Assert.False(GeneratedBridgePathDecision.IsSupportedAsset("AmericaTankCrusader", fingerprint));
        Assert.False(GeneratedBridgePathDecision.IsSupportedAsset(template, "0000000000000000"));
        Assert.False(GeneratedBridgePathDecision.IsSupportedAsset(null, fingerprint));
        Assert.False(GeneratedBridgePathDecision.IsSupportedAsset(template, null));
    }

    private readonly record struct TraceSample(int Frame, float X, float Y, float Z, float TerrainZ, float? SurfaceZ);

    /// <summary>First BridgeStart and its following BridgeEnd map object, as Scene3D pairs them.</summary>
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

    /// <summary>Same drive as the traversal fact: a move order and a sample per logic frame until the goal.</summary>
    private static List<TraceSample> RunDozerTraversal(Game game, (float X, float Y) start, (float X, float Y) goal)
    {
        var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
        player.DisableAI();

        var dozerDefinition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
        Assert.True(dozerDefinition != null, $"definition {DozerDefinitionName} not found.");

        var dozer = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);
        var startZ = game.TerrainLogic.GetGroundHeight(start.X, start.Y);
        dozer.SetTranslation(new Vector3(start.X, start.Y, startZ));

        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        game.StartRun();
        game.EnableFixedLogicTimestep();

        while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
        {
            game.Scene2D.WndWindowManager.PopWindow();
        }

        player.SelectUnits([dozer]);

        var order = Order.CreateMoveOrder(game.Scene3D.GetPlayerIndex(player), new Vector3(goal.X, goal.Y, 0f));
        new OrderProcessor(game).Process([order]);

        var trace = new List<TraceSample>(4096);

        for (var frame = 0; frame < 6000; frame++)
        {
            game.Update(Array.Empty<InputMessage>());

            var position = dozer.Translation;
            var terrainZ = game.TerrainLogic.HeightMap.GetHeight(position.X, position.Y);
            var surfaceZ = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(position.X, position.Y);
            trace.Add(new TraceSample(frame, position.X, position.Y, position.Z, terrainZ, surfaceZ));

            if (position.Y >= goal.Y - 2f)
            {
                break;
            }
        }

        return trace;
    }

    private static void WithPackages(Action<Game, string, string> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-path-wiring", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var packageOn = Path.Combine(scratchRoot, "package-on");
            var packageOff = Path.Combine(scratchRoot, "package-off");

            Convert(payloadDir, packageOn, withRules: true);
            Convert(payloadDir, packageOff, withRules: false);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            body(game, packageOn, packageOff);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Fixture helpers (copied from GeneratedPackageBridgeTraversalTests).
    // ------------------------------------------------------------------

    private static string StagePayload(string payloadDir)
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

        // One straight two-point bridge line crossing the gorge, in payload game units.
        var bridgeEndpoints = new (double X, double Y)[] { (550d, 300d), (550d, 1000d) };
        var features = new List<GeneratedFeature>
        {
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(bridgeEndpoints[0].X, bridgeEndpoints[0].Y)), new GeoPosition(new Vector2d(bridgeEndpoints[1].X, bridgeEndpoints[1].Y))]),
                "vectors",
                "bridge-a",
                "vectors/bridge-a#0",
                -1),
        };

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

    private static void Convert(string payloadDir, string packageDir, bool withRules)
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
            "r3-g8-path-wiring",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors")] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
