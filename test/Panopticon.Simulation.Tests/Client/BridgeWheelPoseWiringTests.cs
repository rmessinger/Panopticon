#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Panopticon.Client;
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

namespace Panopticon.Tests.Client;

/// <summary>
/// Client wiring of the bridge tyre pose on the installed game: a drawable parked inside a
/// registered corridor gets its four tyres lifted onto the support, a vehicle outside the
/// corridor and a tracked vehicle keep byte-identical matrices, and the corridor flag
/// follows registration exactly.
/// </summary>
[Collection("SerialGameTests")]
public sealed class BridgeWheelPoseWiringTests
{
    private const string DozerDefinitionName = "AmericaVehicleDozer";

    private const string CrusaderDefinitionName = "AmericaTankCrusader";

    [GameFact(SageGame.CncGenerals)]
    public void NoCorridor_SkipsThePoseWork()
    {
        WithGame((game, _) =>
        {
            Assert.False(game.TerrainLogic.HasBridgeCorridors);

            game.TerrainLogic.RegisterBridgeCorridor(TestCorridor());
            Assert.True(game.TerrainLogic.HasBridgeCorridors);

            game.TerrainLogic.ClearBridgeSurfaces();
            Assert.False(game.TerrainLogic.HasBridgeCorridors);
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void InsideCorridor_PutsAllFourTyresOnTheSupport()
    {
        WithGame((game, package) =>
        {
            var withoutCorridor = ParkVehicle(game, DozerDefinitionName, 550f, 150f);

            game.LoadMap(package, [], GameType.SinglePlayer);
            game.TerrainLogic.RegisterBridgeCorridor(TestCorridor());
            var withCorridor = ParkVehicle(game, DozerDefinitionName, 550f, 150f);

            Assert.NotEqual(withoutCorridor.BodyMatrix, withCorridor.BodyMatrix);

            // No deck is registered here, so the support is the raw heightmap; every posed
            // tyre must sit on it.
            var world = PosedContacts(game, withCorridor);

            for (var index = 0; index < world.Length; index++)
            {
                var support = game.TerrainLogic.HeightMap.GetHeight(world[index].X, world[index].Y);
                var gap = MathF.Abs(world[index].Z - support);

                Assert.True(gap <= 0.1f, $"tyre {index} sits {gap} above its support.");
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void OutsideCorridor_MatrixIdenticalToNoCorridorLoad()
    {
        WithGame((game, package) =>
        {
            var without = ParkVehicle(game, DozerDefinitionName, 1500f, 1500f);

            game.LoadMap(package, [], GameType.SinglePlayer);
            game.TerrainLogic.RegisterBridgeCorridor(TestCorridor());
            var with = ParkVehicle(game, DozerDefinitionName, 1500f, 1500f);

            Assert.Equal(without.BodyMatrix, with.BodyMatrix);
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void TrackedVehicle_KeepsItsPose()
    {
        WithGame((game, package) =>
        {
            var without = ParkVehicle(game, CrusaderDefinitionName, 530f, 150f);

            game.LoadMap(package, [], GameType.SinglePlayer);
            game.TerrainLogic.RegisterBridgeCorridor(TestCorridor());
            var with = ParkVehicle(game, CrusaderDefinitionName, 530f, 150f);

            Assert.Equal(without.BodyMatrix, with.BodyMatrix);
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void BlendZone_AppliesAPartialPose()
    {
        WithGame((game, package) =>
        {
            var poseFree = ParkVehicle(game, DozerDefinitionName, 550f, 1330f);
            var poseFreeContacts = PosedContacts(game, poseFree);

            game.LoadMap(package, [], GameType.SinglePlayer);
            game.TerrainLogic.RegisterBridgeCorridor(TestCorridor());
            var blended = ParkVehicle(game, DozerDefinitionName, 550f, 1330f);

            var samples = poseFreeContacts.Select(contact => new Vector2(contact.X, contact.Y)).ToArray();
            var weight = game.TerrainLogic.GetBridgeCorridorPoseWeight(samples);
            Assert.True(weight > 0f && weight < 1f, $"blend weight {weight} must be strictly between 0 and 1.");

            game.LoadMap(package, [], GameType.SinglePlayer);
            game.TerrainLogic.RegisterBridgeCorridor(TestCorridor());
            var full = ParkVehicle(game, DozerDefinitionName, 550f, 1200f);

            var blendedAngle = RotationAngle(blended.BodyMatrix);
            var fullAngle = RotationAngle(full.BodyMatrix);

            Assert.True(fullAngle > 0f, $"full-pose angle {fullAngle} must be above zero.");
            Assert.True(blendedAngle < 0.99f * fullAngle, $"blend angle {blendedAngle} must stay below 99% of the full-pose angle {fullAngle}.");
        });
    }

    /// <summary>The pose corridor: the bridge line rectangle with 40-unit blend margins.</summary>
    private static BridgeCorridor TestCorridor()
    {
        return new BridgeCorridor(
            new BridgeRoute(new Vector2(550f, 300f), new Vector2(550f, 1000f)),
            -40d,
            40d,
            -300d,
            1000d,
            40d);
    }

    /// <summary>The rotation angle of a rotation matrix against the identity rotation.</summary>
    private static float RotationAngle(in Matrix4x4 matrix)
    {
        var quaternion = Quaternion.CreateFromRotationMatrix(matrix);

        return 2f * MathF.Acos(Math.Clamp(MathF.Abs(quaternion.W), 0f, 1f));
    }

    /// <summary>Instance-space tyre contacts transformed by the recorded body matrix.</summary>
    private static Vector3[] PosedContacts(Game game, ParkedVehicle vehicle)
    {
        var instance = FirstW3dModelInstance(vehicle.Drawable);
        Assert.NotNull(instance);

        var contacts = new Vector3[4];
        Assert.True(new WheelContactPoints().TryGetContacts(instance, game.AssetStore.LoadContext, contacts), "the dozer must resolve four tyre contacts.");

        var world = new Vector3[4];

        for (var index = 0; index < contacts.Length; index++)
        {
            world[index] = Vector3.Transform(contacts[index], vehicle.BodyMatrix);
        }

        return world;
    }

    private sealed record ParkedVehicle(GameObject Drawable, Matrix4x4 BodyMatrix);

    private static ParkedVehicle ParkVehicle(Game game, string definitionName, float x, float y)
    {
        var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
        player.DisableAI();

        var definition = game.AssetStore.ObjectDefinitions.GetByName(definitionName);
        Assert.NotNull(definition);

        var drawable = game.Scene3D.GameObjects.CreateObject(definition!, player);
        var startZ = game.TerrainLogic.GetGroundHeight(x, y);
        drawable.SetTranslation(new Vector3(x, y, startZ));

        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        game.StartRun();
        game.EnableFixedLogicTimestep();

        while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
        {
            game.Scene2D.WndWindowManager.PopWindow();
        }

        for (var frame = 0; frame < 5; frame++)
        {
            game.Update(Array.Empty<InputMessage>());
        }

        var position = drawable.Translation;
        var eye = new Vector3(position.X - 40f, position.Y - 50f, position.Z + 45f);
        var look = new Vector3(position.X, position.Y - 6f, position.Z - 3f);
        game.Scene3D.Camera.SetLookAt(eye, look, Vector3.UnitZ);
        game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
        game.Render();

        var instance = FirstW3dModelInstance(drawable);
        Assert.NotNull(instance);

        Matrix4x4.Invert(instance!.RelativeBoneTransforms[0], out var inverse);

        return new ParkedVehicle(drawable, instance.AbsoluteBoneTransforms[0] * inverse);
    }

    private static void WithGame(Action<Game, string> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-wheel-pose-wiring", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var package = Path.Combine(scratchRoot, "package");

            Convert(payloadDir, package, withRules: false);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            game.LoadMap(package, [], GameType.SinglePlayer);
            body(game, package);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    private static ModelInstance? FirstW3dModelInstance(GameObject gameObject)
    {
        if (gameObject.Client is not Drawable drawable)
        {
            return null;
        }

        foreach (var module in drawable.DrawModules)
        {
            if (module is W3dModelDraw w3dModule)
            {
                return w3dModule.ActiveModelInstanceOrNull;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Fixture helpers (copied from WheelContactPointsTests).
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
            "r3-g8-wheel-pose-wiring",
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
