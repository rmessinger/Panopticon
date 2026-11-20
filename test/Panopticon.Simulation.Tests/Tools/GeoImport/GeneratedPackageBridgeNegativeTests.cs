#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
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
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;
using GameObject = Panopticon.Logic.Object.GameObject;
using Player = Panopticon.Logic.Player;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// WP1029 rework part 2 falsification and negative facts:
/// - without-correction: with the scoped pitch correction disabled, the north exit gap
///   returns to the doubled-pitch magnitude (the correction is what fixes it);
/// - beside-bridge: a vehicle outside the roadway cross-width gets no bridge surface and
///   follows raw terrain;
/// - below-bridge: a vehicle on the gorge floor under the deck is not snapped up.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageBridgeNegativeTests
{
    private const string DozerDefinitionName = "AmericaVehicleDozer";

    [GameFact(SageGame.CncGenerals)]
    public void WithoutCorrection_NorthExitGapReturns_FalsifyingTheFix()
    {
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var packageOn = Path.Combine(scratchRoot, "package-on");
            Convert(payloadDir, packageOn, withRules: true);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            // Correction DISABLED: the placement keeps the doubled pitch, so the deck at
            // the north exit must sit ~19-35 above the graded terrain again.
            Panopticon.Terrain.Bridge.GeneratedBridgePitchCorrectionEnabled = false;

            try
            {
                game.LoadMap(packageOn, [], GameType.SinglePlayer);
                Assert.NotEmpty(game.Scene3D.Bridges);

                for (var y = 975f; y <= 995f; y += 5f)
                {
                    var deckZ = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(550f, y);
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(550f, y);
                    Assert.True(deckZ.HasValue, $"no deck at y={y:F0} even uncorrected.");
                    var gap = deckZ.Value - terrainZ;
                    Assert.True(
                        gap >= 15f,
                        $"uncorrected north exit gap only {gap:F2} at y={y:F0} (deck {deckZ.Value:F2} vs terrain {terrainZ:F2}); the falsification expected the doubled-pitch drop to return.");
                    Console.WriteLine(
                        $"r3-g8 falsified (correction off) y={y.ToString("F0", CultureInfo.InvariantCulture)}: deck {deckZ.Value.ToString("F2", CultureInfo.InvariantCulture)} terrain {terrainZ.ToString("F2", CultureInfo.InvariantCulture)} gap {gap.ToString("F2", CultureInfo.InvariantCulture)}");
                }

                // The vehicle confirms the drop: crossing with the correction off, the last
                // on-deck Z sits far above the graded terrain it lands on.
                var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
                player.DisableAI();

                var dozerDefinition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
                var dozer = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);
                dozer.SetTranslation(new Vector3(550f, 150f, game.TerrainLogic.HeightMap.GetHeight(550f, 150f)));

                game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
                game.StartRun();
                game.EnableFixedLogicTimestep();

                player.SelectUnits([dozer]);
                var order = Order.CreateMoveOrder(game.Scene3D.GetPlayerIndex(player), new Vector3(550f, 1100f, 0f));
                new OrderProcessor(game).Process([order]);

                float? lastOnDeckGap = null;
                var sawExitRegion = false;

                for (var frame = 0; frame < 2000; frame++)
                {
                    game.Update(Array.Empty<InputMessage>());

                    var position = dozer.Translation;
                    var onDeck = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(position.X, position.Y);
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(position.X, position.Y);

                    if (onDeck.HasValue)
                    {
                        lastOnDeckGap = onDeck.Value - terrainZ;
                    }

                    if (position.Y > 990f)
                    {
                        sawExitRegion = true;
                    }

                    if (position.Y >= 1098f)
                    {
                        break;
                    }
                }

                Assert.True(sawExitRegion, "the dozer never crossed the north exit region.");
                Assert.True(lastOnDeckGap.HasValue, "the dozer never rode the deck with the correction disabled.");
                Assert.True(
                    lastOnDeckGap.Value >= 15f,
                    $"uncorrected last on-deck gap only {lastOnDeckGap.Value:F2} above terrain; the falsification expects the doubled-pitch drop.");
                Console.WriteLine(
                    $"r3-g8 falsified (correction off): final on-deck height above terrain {lastOnDeckGap.Value.ToString("F2", CultureInfo.InvariantCulture)} (the corrected run keeps this under 8).");
            }
            finally
            {
                // Restore the production behavior no matter what the asserts did.
                Panopticon.Terrain.Bridge.GeneratedBridgePitchCorrectionEnabled = true;
            }
        }
        finally
        {
            // Scratch root preserved for evidence.
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void BesideAndBelowBridge_NoSurfaceSnap_VehiclesFollowRawTerrain()
    {
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var packageOn = Path.Combine(scratchRoot, "package-on");
            Convert(payloadDir, packageOn, withRules: true);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            game.LoadMap(packageOn, [], GameType.SinglePlayer);
            Assert.NotEmpty(game.Scene3D.Bridges);

            var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
            player.DisableAI();

            var dozerDefinition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
            var deckAtGorge = game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(550f, 650f);
            Assert.True(deckAtGorge.HasValue, "the generated deck must exist mid-span for this negative pair.");

            // BESIDE: x=600 is outside the roadway cross-width; the surface query must
            // return null there and the vehicle must follow raw terrain.
            var beside = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);
            var besideStartZ = game.TerrainLogic.HeightMap.GetHeight(600f, 650f);
            beside.SetTranslation(new Vector3(600f, 650f, besideStartZ));

            // BELOW: on the gorge floor under the deck; the surface exists at this XY but
            // the vehicle is far below it and must NOT be snapped up.
            var below = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);
            var belowStartZ = game.TerrainLogic.HeightMap.GetHeight(550f, 650f);
            below.SetTranslation(new Vector3(550f, 650f, belowStartZ));

            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
            game.StartRun();
            game.EnableFixedLogicTimestep();

            Assert.Null(game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(600f, 650f));

            for (var frame = 0; frame < 40; frame++)
            {
                game.Update(Array.Empty<InputMessage>());
            }

            var besidePosition = beside.Translation;
            Assert.Null(game.TerrainLogic.GetGeneratedBridgeSurfaceHeight(besidePosition.X, besidePosition.Y));
            var besideTerrainZ = game.TerrainLogic.HeightMap.GetHeight(besidePosition.X, besidePosition.Y);
            Assert.True(
                MathF.Abs(besidePosition.Z - besideTerrainZ) <= 5f,
                $"beside-bridge vehicle Z {besidePosition.Z:F2} vs raw terrain {besideTerrainZ:F2}; the outside vehicle must follow raw terrain.");

            var belowPosition = below.Translation;
            var belowTerrainZ = game.TerrainLogic.HeightMap.GetHeight(belowPosition.X, belowPosition.Y);
            Assert.True(
                MathF.Abs(belowPosition.Z - belowTerrainZ) <= 5f,
                $"below-bridge vehicle Z {belowPosition.Z:F2} vs gorge terrain {belowTerrainZ:F2}; the body must not snap up to the deck at {deckAtGorge.Value:F2}.");

            Console.WriteLine(
                $"r3-g8 negatives: beside ({besidePosition.X:F0},{besidePosition.Y:F0}) Z {besidePosition.Z.ToString("F2", CultureInfo.InvariantCulture)} == terrain {besideTerrainZ.ToString("F2", CultureInfo.InvariantCulture)}, surface null; " +
                $"below (550,650) Z {belowPosition.Z.ToString("F2", CultureInfo.InvariantCulture)} == gorge terrain {belowTerrainZ.ToString("F2", CultureInfo.InvariantCulture)}, deck {deckAtGorge.Value.ToString("F2", CultureInfo.InvariantCulture)} not applied.");
        }
        finally
        {
            // Scratch root preserved for evidence.
        }
    }

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
                descriptors.Add(new PayloadChunkDescriptor(cx, cy, terrain!, materials!, masks!, null));
            }
        }

        var features = new List<GeneratedFeature>
        {
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(550d, 300d)), new GeoPosition(new Vector2d(550d, 1000d))]),
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
            "r3-g8-bridge-traversal",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors")] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    private static string NewScratchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-bridge-negatives", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
