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
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Client;

/// <summary>
/// Wheel contact resolver on the installed game: the dozer's four tyre contacts sit at the
/// measured front and rear offsets, stay stable across frames, and vehicles without four
/// tyres get no result.
/// </summary>
[Collection("SerialGameTests")]
public sealed class WheelContactPointsTests
{
    private const string DozerDefinitionName = "AmericaVehicleDozer";

    private const string CrusaderDefinitionName = "AmericaTankCrusader";

    private const string HumveeDefinitionName = "AmericaVehicleHumvee";

    /// <summary>Frame-0 tyre contacts from the harness (C0-native-tires.csv), instance space along +X (forward).</summary>
    private const float RearAxleForward = -14.491f;

    private const float FrontAxleForward = -1.578f;

    /// <summary>Frame-0 tyre contacts from the harness, instance space along Y (lateral).</summary>
    private const float LeftLateral = -7.109f;

    private const float RightLateral = 7.187f;

    private const float OffsetTolerance = 0.05f;

    [GameFact(SageGame.CncGenerals)]
    public void Dozer_ResolvesFourContacts_AtMeasuredOffsets()
    {
        WithLoadedGame(game =>
        {
            var dozer = StartDrawable(game, DozerDefinitionName);
            var modelInstance = FirstW3dModelInstance(dozer);
            Assert.NotNull(modelInstance);

            var resolver = new WheelContactPoints();
            var contacts = new Vector3[4];
            Assert.True(resolver.TryGetContacts(modelInstance, game.AssetStore.LoadContext, contacts), "dozer must resolve four tyre contacts.");

            // Instance-space contacts (RelativeBoneTransforms): forward is +X, lateral is Y.
            foreach (var contact in contacts)
            {
                Console.WriteLine(
                    $"wheel contact dozer: x {contact.X.ToString("F3", CultureInfo.InvariantCulture)}, y {contact.Y.ToString("F3", CultureInfo.InvariantCulture)}, z {contact.Z.ToString("F3", CultureInfo.InvariantCulture)}");
            }

            var forward = contacts.Select(c => c.X).OrderBy(v => v).ToArray();
            var lateral = contacts.Select(c => c.Y).OrderBy(v => v).ToArray();

            Assert.All(forward.Take(2), value => Assert.InRange(value, RearAxleForward - OffsetTolerance, RearAxleForward + OffsetTolerance));
            Assert.All(forward.Skip(2), value => Assert.InRange(value, FrontAxleForward - OffsetTolerance, FrontAxleForward + OffsetTolerance));
            Assert.All(lateral.Take(2), value => Assert.InRange(value, LeftLateral - OffsetTolerance, LeftLateral + OffsetTolerance));
            Assert.All(lateral.Skip(2), value => Assert.InRange(value, RightLateral - OffsetTolerance, RightLateral + OffsetTolerance));
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void Dozer_ContactsStableAcrossFrames()
    {
        WithLoadedGame(game =>
        {
            var dozer = StartDrawable(game, DozerDefinitionName);
            var resolver = new WheelContactPoints();
            var first = new Vector3[4];
            Assert.True(resolver.TryGetContacts(FirstW3dModelInstance(dozer), game.AssetStore.LoadContext, first));

            Step(game, dozer);

            var second = new Vector3[4];
            Assert.True(resolver.TryGetContacts(FirstW3dModelInstance(dozer), game.AssetStore.LoadContext, second));

            for (var index = 0; index < 4; index++)
            {
                Assert.True(MathF.Abs(first[index].X - second[index].X) <= 1e-3f, $"contact {index} X moved between frames.");
                Assert.True(MathF.Abs(first[index].Y - second[index].Y) <= 1e-3f, $"contact {index} Y moved between frames.");
                Assert.True(MathF.Abs(first[index].Z - second[index].Z) <= 0.1f, $"contact {index} Z moved between frames (tyres spin, Z within 0.1).");
            }
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void NullModelInstance_ReturnsFalse_WithoutWriting()
    {
        WithLoadedGame(game =>
        {
            var resolver = new WheelContactPoints();
            var contacts = new Vector3[4];

            Assert.False(resolver.TryGetContacts(null, game.AssetStore.LoadContext, contacts));
            Assert.All(contacts, contact => Assert.Equal(Vector3.Zero, contact));
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void SpanShorterThanFour_ThrowsArgumentOutOfRange()
    {
        WithLoadedGame(game =>
        {
            var dozer = StartDrawable(game, DozerDefinitionName);
            var resolver = new WheelContactPoints();
            var modelInstance = FirstW3dModelInstance(dozer);

            Assert.Throws<ArgumentOutOfRangeException>(() => resolver.TryGetContacts(modelInstance, game.AssetStore.LoadContext, new Vector3[3]));
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void Crusader_TrackedVehicle_ReturnsFalse()
    {
        WithLoadedGame(game =>
        {
            var crusader = StartDrawable(game, CrusaderDefinitionName);
            var resolver = new WheelContactPoints();

            Assert.False(resolver.TryGetContacts(FirstW3dModelInstance(crusader), game.AssetStore.LoadContext, new Vector3[4]));
        });
    }

    [GameFact(SageGame.CncGenerals)]
    public void Humvee_ResolutionDoesNotThrow()
    {
        WithLoadedGame(game =>
        {
            var humvee = StartDrawable(game, HumveeDefinitionName);
            var resolver = new WheelContactPoints();
            var contacts = new Vector3[4];
            var resolved = resolver.TryGetContacts(FirstW3dModelInstance(humvee), game.AssetStore.LoadContext, contacts);

            Console.WriteLine($"wheel contact humvee: resolved {resolved}");
        });
    }

    private static void WithLoadedGame(Action<Game> body)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-wheel-contacts", Guid.NewGuid().ToString("N"));
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
            body(game);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    private static GameObject StartDrawable(Game game, string definitionName)
    {
        var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
        player.DisableAI();

        var definition = game.AssetStore.ObjectDefinitions.GetByName(definitionName);
        Assert.NotNull(definition);

        var drawable = game.Scene3D.GameObjects.CreateObject(definition!, player);
        var startZ = game.TerrainLogic.GetGroundHeight(550f, 150f);
        drawable.SetTranslation(new Vector3(550f, 150f, startZ));

        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        game.StartRun();
        game.EnableFixedLogicTimestep();

        while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
        {
            game.Scene2D.WndWindowManager.PopWindow();
        }

        for (var frame = 0; frame < 5; frame++)
        {
            Step(game, drawable);
        }

        return drawable;
    }

    /// <summary>One logic frame and one render, with the camera framing the object as the tyre recorder does.</summary>
    private static void Step(Game game, GameObject drawable)
    {
        game.Update(Array.Empty<InputMessage>());

        var position = drawable.Translation;
        var eye = new Vector3(position.X - 40f, position.Y - 50f, position.Z + 45f);
        var look = new Vector3(position.X, position.Y - 6f, position.Z - 3f);
        game.Scene3D.Camera.SetLookAt(eye, look, Vector3.UnitZ);
        game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
        game.Render();
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
            "r3-g8-wheel-contacts",
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
