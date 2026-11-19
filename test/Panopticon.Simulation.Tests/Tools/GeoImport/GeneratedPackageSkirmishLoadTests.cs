#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial.Preview;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Panopticon.Tests.Tools.GeoImport;
using Panopticon.Tools.GeoImport;
using Xunit;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// WP1024 playable-skirmish-sides proof. The portable round trip stages the synthetic card 24
/// payload with the public GeoImport CLI (BCL GeoTIFF and KML only, fake texture catalog) and
/// reloads the package through PackageReader: the synthesized retained SidesList parses back
/// with the declared factions, and the player start PlacedObjects sit at the declared
/// positions with the properties the runtime waypoint scanner reads. The installed fact then
/// boots the same package through the real skirmish start (Game.LoadMap under
/// StartSkirmishOrMultiPlayerGame), asserts the two opposing factions, the player start
/// waypoints, and the spawned starting buildings, and runs a bounded 300-frame sequence in
/// which a slot-1 starting unit receives a move order toward slot 2 and observably moves. The
/// offscreen capture is a real native render asserted non-black and non-uniform.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageSkirmishLoadTests
{
    private const string SkirmishArgument = "FactionAmerica:100,100;FactionGLA:300,300";

    private const int FrameCount = 300;

    private const string SidesListSection = "SidesList";

    /// <summary>Under the 900-1500 terrain-void camera band (docs/screenshots.md).</summary>
    private const float CameraHeight = 560f;

    private static readonly GeoImportCliOptions FakeCatalog = new()
    {
        TerrainTextureCatalogResolver = () => new FixedTerrainTextureCatalog(["GrassType1", "AsphaltType1"]),
    };

    [Fact]
    public void SkirmishPackage_ReloadCarriesTheDeclaredFactionsAndStartWaypoints()
    {
        using var fixture = Card24ProofFixture.Create();
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = Path.Combine(scratchRoot, "payload");
            var packageDir = Path.Combine(scratchRoot, "package");

            RunCli(FakeCatalog, ["import", "--manifest", fixture.ManifestPath, "--out", payloadDir]);
            RunCli(
                FakeCatalog,
                ["package", "--payload", payloadDir, "--textures", fixture.TexturesJsonPath, "--out", packageDir, "--skirmish", SkirmishArgument]);

            var interchange = new PackageReader(packageDir).Read();

            // The synthesized SidesList parses back with the neutral, civilian, and faction players
            // in the load-bearing skirmish order.
            var sidesSection = Assert.Single(interchange.RetainedSections, section => section.SectionName == SidesListSection);
            var sides = MapBackendReaders.ReadSidesList(sidesSection, mapHasAssetList: false);
            Assert.Equal(
                new[] { ("", ""), ("PlyrCivilian", "FactionCivilian"), ("PlyrAmerica", "FactionAmerica"), ("PlyrGLA", "FactionGLA") },
                sides.Players.Select(player => (player.Name, player.Faction)).ToArray());
            Assert.All(sides.Players, player => Assert.NotNull(player.Scripts));

            // One player start waypoint per slot, at the declared map-local positions.
            var objects = interchange.Objects!.Objects;
            var start1 = Assert.Single(objects, placed => placed.StableId == "Player_1_Start");
            var start2 = Assert.Single(objects, placed => placed.StableId == "Player_2_Start");
            Assert.Equal("*Waypoints/Waypoint", start1.Type.Name);
            Assert.Equal("*Waypoints/Waypoint", start2.Type.Name);
            Assert.Equal(100d, start1.X.Value);
            Assert.Equal(100d, start1.Y.Value);
            Assert.Equal(300d, start2.X.Value);
            Assert.Equal(300d, start2.Y.Value);
            Assert.Equal(1, Assert.IsType<MapPropertyValue.IntegerValue>(start1.LegacyProperties["waypointID"]).Value);
            Assert.Equal(2, Assert.IsType<MapPropertyValue.IntegerValue>(start2.LegacyProperties["waypointID"]).Value);
            Assert.Equal("Player_1_Start", Assert.IsType<MapPropertyValue.AsciiStringValue>(start1.LegacyProperties["waypointName"]).Value);
            Assert.Equal("Player_2_Start", Assert.IsType<MapPropertyValue.AsciiStringValue>(start2.LegacyProperties["waypointName"]).Value);

            // Without the opt-in the same payload writes no sides section and no start waypoints.
            var plainPackageDir = Path.Combine(scratchRoot, "package-plain");
            RunCli(
                FakeCatalog,
                ["package", "--payload", payloadDir, "--textures", fixture.TexturesJsonPath, "--out", plainPackageDir]);
            var plain = new PackageReader(plainPackageDir).Read();
            Assert.DoesNotContain(plain.RetainedSections, section => section.SectionName == SidesListSection);
            Assert.DoesNotContain(plain.Objects?.Objects ?? [], placed => placed.StableId.StartsWith("Player_", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void SkirmishPackage_LoadsWithPlayableSides_StartBuildingsAndObservedMovement()
    {
        using var fixture = Card24ProofFixture.Create();

        // Kept after the run for evidence preservation (the capture PNG); never deleted here.
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = Path.Combine(scratchRoot, "payload");
            var packageDir = Path.Combine(scratchRoot, "package");
            var userRoot = Path.Combine(scratchRoot, "user");
            var pngPath = Path.Combine(scratchRoot, "skirmish-factions-frame.png");

            RunCli(["import", "--manifest", fixture.ManifestPath, "--out", payloadDir]);
            RunCli(
                ["package", "--payload", payloadDir, "--textures", fixture.TexturesJsonPath, "--out", packageDir, "--skirmish", SkirmishArgument]);

            var playerSettings = new PlayerSetting[]
            {
                new(1, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
                new(2, "FactionGLA", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "GLA"),
            };

            Directory.CreateDirectory(userRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, UserDataRootPath = userRoot },
                window: null);

            // The real skirmish start: StartGame runs Game.LoadMap on the package directory,
            // sets the sides up from the synthesized SidesList, and spawns a starting building
            // plus starting units per player at the declared player start waypoints.
            game.StartSkirmishOrMultiPlayerGame(packageDir, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
            game.StartRun();
            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();

            var scene = game.Scene3D;
            Assert.NotNull(scene);

            // Two opposing factions joined the game, not just neutral and civilian.
            Assert.Equal("FactionAmerica", scene.Players[2].Side);
            Assert.Equal("FactionGLA", scene.Players[3].Side);

            // The declared player start waypoints are resolvable 1-based at their positions.
            Assert.True(scene.Waypoints.TryGetPlayerStart(1, out var start1), "Player_1_Start was not found.");
            Assert.True(scene.Waypoints.TryGetPlayerStart(2, out var start2), "Player_2_Start was not found.");
            Assert.Equal(100f, start1!.Position.X);
            Assert.Equal(100f, start1.Position.Y);
            Assert.Equal(300f, start2!.Position.X);
            Assert.Equal(300f, start2.Position.Y);

            foreach (var player in scene.Players)
            {
                player.DisableAI();
            }

            game.Scripting.Active = false;
            game.EnableFixedLogicTimestep();

            // The starting buildings spawned at their waypoints.
            var building1 = Assert.Single(
                scene.GameObjects.Objects,
                object0 => object0.Owner == scene.Players[2] && object0.Definition.KindOf.Get(ObjectKinds.Structure));
            var building2 = Assert.Single(
                scene.GameObjects.Objects,
                object0 => object0.Owner == scene.Players[3] && object0.Definition.KindOf.Get(ObjectKinds.Structure));
            Assert.True(PlanarDistance(building1.Translation, start1.Position) < 40f, $"The USA starting building spawned {PlanarDistance(building1.Translation, start1.Position)} units from Player_1_Start.");
            Assert.True(PlanarDistance(building2.Translation, start2.Position) < 40f, $"The GLA starting building spawned {PlanarDistance(building2.Translation, start2.Position)} units from Player_2_Start.");

            // A slot-1 starting unit (FactionAmerica's StartingUnit0, AmericaVehicleDozer)
            // receives a move order toward slot 2's start position and observably moves.
            var unit = scene.GameObjects.Objects.First(
                object0 => object0.Owner == scene.Players[2]
                    && !object0.Definition.KindOf.Get(ObjectKinds.Structure)
                    && object0.AIUpdate is not null);
            var unitStart = unit.Translation;
            var target = start2.Position;
            var startDistance = Vector3.Distance(unitStart, target);
            Assert.True(startDistance > 100f, $"The starting unit spawned {startDistance} units from the target; the slots are expected to be apart.");

            OrderProcessor.MoveUnits(scene.Navigation, scene.Players[2], [unit], target);

            var empty = Array.Empty<InputMessage>();
            var startFrame = game.GameLogic.CurrentFrame.Value;
            var movedDistance = 0f;

            for (var frame = 0; frame < FrameCount; frame++)
            {
                game.Update(empty);

                if (frame == FrameCount - 1)
                {
                    movedDistance = Vector3.Distance(unit.Translation, unitStart);
                }
            }

            Assert.Equal(startFrame + FrameCount, game.GameLogic.CurrentFrame.Value);
            Assert.True(
                movedDistance > 10f,
                $"The ordered starting unit did not observably move in {FrameCount} frames: delta {movedDistance}, distance to target {startDistance} -> {Vector3.Distance(unit.Translation, target)}.");

            // Pause before rendering: the warmup renders must not advance the logic frame, so
            // the capture shows the exact state the 300 proof frames ended in.
            game.IsLogicRunning = false;

            var view = scene.TacticalView;
            view.InitHeightForMap();
            view.SetAngleAndPitchToDefault();
            view.SnapHeightAboveGround(CameraHeight);
            view.LookAt(new Vector3(200f, 200f, 0f));

            for (var warmup = 0; warmup < 3; warmup++)
            {
                game.Update(empty);
                game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
                game.Render();
            }

            Assert.Equal(startFrame + FrameCount, game.GameLogic.CurrentFrame.Value);

            var frame0 = FrameCapturer.Capture(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
            var luminance = LuminanceStats.Compute(frame0.PixelData);
            Assert.True(
                luminance.BrightFraction >= 0.005d,
                $"Render is black: only {luminance.BrightFraction:P2} of pixels have luminance > 8 (min {luminance.Min}, max {luminance.Max}, mean {luminance.Mean:F1}).");
            Assert.True(
                luminance.StandardDeviation > 4.0d && luminance.Distinct > 32,
                $"Render is uniform: stddev {luminance.StandardDeviation:F2}, distinct values {luminance.Distinct}.");

            FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, pngPath);
            Assert.True(File.Exists(pngPath), "The capture PNG was not written.");
            Assert.True(new FileInfo(pngPath).Length > 0, "The capture PNG is empty.");
        }
        finally
        {
            // Scratch root intentionally preserved for capture evidence.
        }
    }

    private static float PlanarDistance(Vector3 left, Vector3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;

        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static string NewScratchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g5-skirmish", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void RunCli(string[] arguments)
    {
        RunCli(null, arguments);
    }

    private static void RunCli(GeoImportCliOptions? options, string[] arguments)
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exit = GeoImportCli.Run(arguments, standardOut, errorOut, CancellationToken.None, options);

        Assert.True(exit == 0, $"GeoImport {arguments[0]} exited {exit}: {errorOut}");
        Assert.Equal(string.Empty, errorOut.ToString());
    }

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }

    private sealed record LuminanceStats(double BrightFraction, float Min, float Max, double Mean, int Distinct, double StandardDeviation)
    {
        public static LuminanceStats Compute(byte[] pixelData)
        {
            double bright = 0;
            double sum = 0;
            var minimum = float.MaxValue;
            var maximum = float.MinValue;
            var distinct = new HashSet<byte>();

            for (var index = 0; index + 3 < pixelData.Length; index += 4)
            {
                // BGRA order (FrameCapturer); luminance from the RGB channels.
                var value = (0.299f * pixelData[index + 2]) + (0.587f * pixelData[index + 1]) + (0.114f * pixelData[index]);
                bright += value > 8f ? 1 : 0;
                sum += value;
                minimum = MathF.Min(minimum, value);
                maximum = MathF.Max(maximum, value);
                distinct.Add((byte)(int)value);
            }

            var count = pixelData.Length / 4;
            var mean = sum / count;
            var variance = 0d;

            for (var index = 0; index + 3 < pixelData.Length; index += 4)
            {
                var value = (0.299f * pixelData[index + 2]) + (0.587f * pixelData[index + 1]) + (0.114f * pixelData[index]);
                variance += (value - mean) * (value - mean);
            }

            return new LuminanceStats(bright / count, minimum, maximum, mean, distinct.Count, Math.Sqrt(variance / count));
        }
    }
}
