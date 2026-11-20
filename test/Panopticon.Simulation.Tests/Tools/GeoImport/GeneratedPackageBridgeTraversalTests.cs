#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
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
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using Xunit;
using GameObject = Panopticon.Logic.Object.GameObject;
using MapInterchange = Panopticon.Content.Importing.Maps.MapInterchange;
using Player = Panopticon.Logic.Player;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// WP1029 vehicle bridge traversal proof. The portable fact converts the WP1022 gorge
/// fixture with bridge rules, reloads it through PackageReader, and checks the graded
/// landing heights at the ramp positions. The installed fact then orders a dozer across
/// the bridge: without the bridge surface the locomotor's raw-heightmap Z drives the
/// vehicle into the gorge (G7 witness: Z 1020 against a 1109 deck), with the surface
/// registered the vehicle rides the deck within tolerance and both transitions stay
/// under 3 units of Z per logic frame.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageBridgeTraversalTests
{
    /// <summary>
    /// Operator capture root, mounted on the agent host only; absent in the MCP container.
    /// </summary>
    private const string ExternalCaptureRoot = "/mnt/pit2/Network Storage/temp";

    /// <summary>Gorge band of the fixture terrain, in game units (lattice rows 61..67).</summary>
    private const float GorgeMinY = 610f;

    private const float GorgeMaxY = 670f;

    /// <summary>
    /// Static full-span camera height: above the follow-camera void band on this Z≈1000
    /// map (empirically probed: 140 voids from unstreamed terrain, 460 renders the span,
    /// 660 shows the map-edge void) and wide enough to frame the 700-unit span.
    /// </summary>
    private const float CaptureCameraHeight = 460f;

    private const int CaptureWidth = 1280;

    private const int CaptureHeight = 800;

    /// <summary>24 captures per angle at every 38th logic frame covers the ~900-frame crossing.</summary>
    private const int MaxCaptureCount = 26;

    private const int MaxCaptureFrames = 2400;

    /// <summary>GIF playback at 4 fps: per-frame delay in centiseconds (250 ms).</summary>
    private const int GifFrameDelayCentiseconds = 25;

    private static readonly (double X, double Y)[] BridgeEndpoints = [(550d, 300d), (550d, 1000d)];

    private const string DozerDefinitionName = "AmericaVehicleDozer";

    [Fact]
    public void GradedPackage_ReloadCarriesGradedLandingHeights()
    {
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var package = Path.Combine(scratchRoot, "package");

            Convert(payloadDir, package, withRules: true);

            var model = new PackageReader(package).Read();
            var latticeWidth = model.Layout.LatticeWidth;

            // Endpoint (550, 300) = lattice (55, 30): terrain 1083.75, deck 1090.25, ramp
            // target 1089.25. The endpoint cell keeps its height; the cells strictly south
            // of it ramp up with the smoothstep curve.
            Assert.Equal(1083.75f, ReloadedLatticeHeight(model, latticeWidth, 55, 30));

            var currentAt29 = ResidentGridHeight(PayloadHeight(55, 29));
            var graded29 = Graded(currentAt29, 1083.75d + 6.5d, 10d);
            Assert.Equal(ResidentGridHeight(graded29), ReloadedLatticeHeight(model, latticeWidth, 55, 29));

            var currentAt28 = ResidentGridHeight(PayloadHeight(55, 28));
            var graded28 = Graded(currentAt28, 1083.75d + 6.5d, 20d);
            Assert.Equal(ResidentGridHeight(graded28), ReloadedLatticeHeight(model, latticeWidth, 55, 28));

            // Ramp edge (40 game units) and outside the cross-width band stay untouched.
            Assert.Equal(ResidentGridHeight(PayloadHeight(55, 25)), ReloadedLatticeHeight(model, latticeWidth, 55, 25));
            Assert.Equal(ResidentGridHeight(PayloadHeight(49, 29)), ReloadedLatticeHeight(model, latticeWidth, 49, 29));
            Assert.Equal(ResidentGridHeight(PayloadHeight(61, 29)), ReloadedLatticeHeight(model, latticeWidth, 61, 29));

            // The north landing ramps symmetrically toward its deck (terrain 1118.75).
            var currentAt101 = ResidentGridHeight(PayloadHeight(55, 101));
            var graded101 = Graded(currentAt101, 1118.75d + 6.5d, 10d);
            Assert.Equal(ResidentGridHeight(graded101), ReloadedLatticeHeight(model, latticeWidth, 55, 101));
            Assert.Equal(1118.75f, ReloadedLatticeHeight(model, latticeWidth, 55, 100));
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void DozerOrder_CrossesBridgeOnDeck_WithoutGorgeDip()
    {
        // Kept after the run for capture evidence; never deleted here.
        var scratchRoot = NewScratchRoot();

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

            // ---- OFF (before behavior; documented, not asserted): raw terrain drives the
            // ---- dozer into the gorge. G7 witness: dozer Z 1020 vs deck 1109 at mid-span.
            game.LoadMap(packageOff, [], GameType.SinglePlayer);
            {
                Assert.Empty(game.Scene3D.Bridges);
                Assert.Null(game.TerrainLogic.GetBridgeSurfaceZ(550f, 650f));

                var beforeTrace = RunDozerTraversal(game, (550f, 150f), (550f, 1100f));
                var beforeMid = beforeTrace.Where(sample => sample.Y > GorgeMinY && sample.Y < GorgeMaxY).ToList();

                if (beforeMid.Count > 0)
                {
                    var lowest = beforeMid.Min(sample => sample.Z);
                    Console.WriteLine(
                        $"r3-g8 BEFORE (no bridge surface): gorge-band samples {beforeMid.Count}, lowest dozer Z {lowest.ToString("F1", CultureInfo.InvariantCulture)} " +
                        $"(deck approx 1090; the dozer dips {lowest < 1070f}); max |dz| per frame " +
                        $"{MaxZStep(beforeTrace).ToString("F2", CultureInfo.InvariantCulture)}");
                }
                else
                {
                    Console.WriteLine("r3-g8 BEFORE: dozer did not enter the gorge band with raw terrain heights.");
                }
            }

            // ---- ON (after): the bridge surface carries the vehicle across on the deck.
            game.LoadMap(packageOn, [], GameType.SinglePlayer);
            {
                Assert.NotEmpty(game.Scene3D.Bridges);

                // Print the deck profile and bridge facts for the evidence report before
                // any assertion, so a surface-coverage gap shows its shape directly.
                var bridge = game.Scene3D.Bridges[0];
                Console.WriteLine(
                    "r3-g8 bridge facts: bridges " + game.Scene3D.Bridges.Length +
                    ", anchor start " + bridge.StartPositionWithHeight.ToString("F2", CultureInfo.InvariantCulture) +
                    ", anchor end " + bridge.EndPositionWithHeight.ToString("F2", CultureInfo.InvariantCulture) +
                    ", surface triangles " + (bridge.CreateSurface()?.TriangleCount.ToString(CultureInfo.InvariantCulture) ?? "0"));
                for (var y = 240f; y <= 1060f; y += 20f)
                {
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(550f, y);
                    var deckZ = game.TerrainLogic.GetBridgeSurfaceZ(550f, y);
                    Console.WriteLine(
                        $"r3-g8 profile y={y.ToString("F0", CultureInfo.InvariantCulture)} terrain {terrainZ.ToString("F2", CultureInfo.InvariantCulture)} deck {(deckZ?.ToString("F2", CultureInfo.InvariantCulture) ?? "none")}");
                }

                // The bridge registered its roadway surface: mid-span queries resolve high
                // above the gorge floor (terrain 920.75 there, deck around 1090).
                var midSpanSurfaceZ = game.TerrainLogic.GetBridgeSurfaceZ(550f, 650f);
                Assert.True(midSpanSurfaceZ.HasValue, "the bridge surface must cover the mid-span XY.");
                Assert.True(midSpanSurfaceZ.Value > 1050f, $"mid-span surface Z {midSpanSurfaceZ.Value} must sit near deck height, not gorge terrain.");

                var trace = RunDozerTraversal(game, (550f, 150f), (550f, 1100f));

                // The dozer crossed the whole span.
                var maxY = trace.Max(sample => sample.Y);
                Assert.True(maxY > 1060f, $"the dozer reached only y={maxY}; it must cross the bridge toward (550, 1100).");

                var gorgeSamples = trace.Where(sample => sample.Y > GorgeMinY && sample.Y < GorgeMaxY).ToList();
                Assert.True(gorgeSamples.Count > 0, "the dozer never entered the gorge band; it must drive across the span.");

                // On-deck: every gorge-band sample rides the bridge surface within ±5.
                foreach (var sample in gorgeSamples)
                {
                    Assert.True(sample.SurfaceZ.HasValue, $"no bridge surface at ({sample.X:F1}, {sample.Y:F1}) mid-span.");
                    var delta = sample.Z - sample.SurfaceZ.Value;
                    Assert.True(
                        MathF.Abs(delta) <= 5f,
                        $"dozer Z {sample.Z:F2} vs deck {sample.SurfaceZ.Value:F2} at y={sample.Y:F1} (frame {sample.Frame}); the vehicle must stay on the deck within ±5.");
                }

                var lowestOnDeck = gorgeSamples.Min(sample => sample.Z);
                var gorgeTerrainZ = game.TerrainLogic.HeightMap.GetHeight(550f, 640f);
                Assert.True(
                    lowestOnDeck > gorgeTerrainZ + 50f,
                    $"dozer lowest gorge Z {lowestOnDeck:F1} against gorge terrain {gorgeTerrainZ:F1}; the vehicle dipped into the gorge.");

                // No Z discontinuity above 3 units per logic frame across the whole run.
                var maxStep = MaxZStep(trace);
                Assert.True(maxStep <= 3f, $"max per-frame Z step {maxStep:F2} exceeds the 3-unit discontinuity bound.");

                // Contact clearance mid-span: Translation.Z - surface Z within [-1, +3].
                var contact = gorgeSamples.Select(sample => sample.Z - sample.SurfaceZ!.Value).ToList();
                Assert.True(
                    contact.All(delta => delta is >= -1f and <= 3f),
                    $"contact clearance mid-span outside [-1, +3]: [{contact.Min():F2}, {contact.Max():F2}].");

                // Exit transition: the rendered deck rises above the terrain slope over the
                // span (pre-existing bridge layout behavior: the baked model tilt adds to
                // the slope rotation), so past the elevated deck end (y≈990, deck ≈1150)
                // the vehicle descends, physics-smoothed within the per-frame bound above,
                // and lands on the graded north landing ramp.
                var exitSamples = trace.Where(sample => sample.Y > 1040f && sample.Y < 1090f).ToList();
                Assert.True(exitSamples.Count > 0, "the dozer never reached the north landing.");
                foreach (var sample in exitSamples)
                {
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(sample.X, sample.Y);
                    Assert.True(
                        MathF.Abs(sample.Z - terrainZ) <= 8f,
                        $"exit Z {sample.Z:F2} vs terrain {terrainZ:F2} at y={sample.Y:F1}; the vehicle must return to (graded) terrain.");
                }

                Console.WriteLine(
                    $"r3-g8 AFTER: frames {trace.Count}, gorge samples {gorgeSamples.Count}, max |dz|/frame {maxStep:F2}, " +
                    $"contact clearance [{contact.Min():F2}, {contact.Max():F2}], final y {maxY:F1}.");
            }
        }
        finally
        {
            // Scratch root intentionally preserved for capture evidence.
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void DozerCrossing_CaptureTwoAngles_TraceAndGifFrames()
    {
        // Kept after the run for evidence preservation (the capture PNGs); never deleted here.
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var packageOn = Path.Combine(scratchRoot, "package-on");

            Convert(payloadDir, packageOn, withRules: true);

            var userRoot = Path.Combine(scratchRoot, "user");
            Directory.CreateDirectory(userRoot);

            // Captures always land in a portable per-run directory; the operator capture
            // root (/mnt/pit2, host-local only) receives best-effort additional copies
            // when it is mounted, so the fact runs unchanged in the MCP container.
            var portableRoot = Path.Combine(Path.GetTempPath(), "r3-g8-traversal", Guid.NewGuid().ToString("N"));
            var framesRoot = Path.Combine(portableRoot, "frames", "r3-g8-bridge-traversal-v1");
            Directory.CreateDirectory(framesRoot);

            using var game = new Game(
                InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
                preferredBackend: null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
                window: null);

            game.LoadMap(packageOn, [], GameType.SinglePlayer);
            Assert.NotEmpty(game.Scene3D.Bridges);

            foreach (var angleName in new[] { "side", "oblique" })
            {
                var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
                player.DisableAI();

                var dozerDefinition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
                Assert.True(dozerDefinition != null, $"definition {DozerDefinitionName} not found.");

                var dozer = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);
                var startZ = game.TerrainLogic.GetGroundHeight(550f, 150f);
                dozer.SetTranslation(new Vector3(550f, 150f, startZ));

                game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();

                // Direct camera placement (Camera.SetLookAt after each logic tick, which
                // TacticalView would otherwise overwrite): two static 3/4 views framing the
                // full span. The tactical camera path is unusable here because its
                // ground-level clamp bakes a Z≈120 map model into the offset math, which on
                // this Z≈1000 map parks the eye outside the map or in the terrain void.
                var eye = angleName == "side"
                    ? new Vector3(1400f, 650f, 1620f)
                    : new Vector3(0f, 40f, 1680f);
                var lookTarget = angleName == "side"
                    ? new Vector3(550f, 650f, 1110f)
                    : new Vector3(600f, 640f, 1115f);

                game.StartRun();
                game.EnableFixedLogicTimestep();

                while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
                {
                    game.Scene2D.WndWindowManager.PopWindow();
                }

                // Settle window: hold the camera so terrain streams in around the eye
                // before the dozer starts crossing.
                for (var settle = 0; settle < 30; settle++)
                {
                    game.Scene3D.Camera.SetLookAt(eye, lookTarget, Vector3.UnitZ);
                    game.Update(Array.Empty<InputMessage>());
                }

                player.SelectUnits([dozer]);
                var order = Order.CreateMoveOrder(game.Scene3D.GetPlayerIndex(player), new Vector3(550f, 1100f, 0f));
                new OrderProcessor(game).Process([order]);
                var tracePath = Path.Combine(framesRoot, $"position-trace-{angleName}-v1.csv");
                var cameraPath = Path.Combine(framesRoot, $"camera-{angleName}-v1.txt");
                var traceLines = new List<string> { "frame,x,y,z,terrain_z,surface_z" };

                const int captureEveryNthFrame = 38;
                var captured = 0;

                for (var frame = 0; frame < MaxCaptureFrames; frame++)
                {
                    game.Update(Array.Empty<InputMessage>());

                    var position = dozer.Translation;
                    var terrainZ = game.TerrainLogic.HeightMap.GetHeight(position.X, position.Y);
                    var surfaceZ = game.TerrainLogic.GetBridgeSurfaceZ(position.X, position.Y);
                    traceLines.Add(string.Create(CultureInfo.InvariantCulture, $"{frame},{position.X:F2},{position.Y:F2},{position.Z:F2},{terrainZ:F2},{(surfaceZ?.ToString("F2") ?? string.Empty)}"));

                    if (frame % captureEveryNthFrame == 0 && captured < MaxCaptureCount)
                    {
                        game.Scene3D.Camera.SetLookAt(eye, lookTarget, Vector3.UnitZ);
                        game.Panel.EnsureFrame(new Rectangle(0, 0, CaptureWidth, CaptureHeight));
                        game.Render();

                        var pngPath = Path.Combine(framesRoot, $"{angleName}-{captured:00}-f{frame:0000}.png");
                        FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, pngPath);
                        captured++;
                    }

                    if (position.Y >= 1098f)
                    {
                        break;
                    }
                }

                File.WriteAllLines(tracePath, traceLines);
                File.WriteAllText(
                    cameraPath,
                    $"angle-name={angleName}\neye={eye.X.ToString("F1", CultureInfo.InvariantCulture)},{eye.Y.ToString("F1", CultureInfo.InvariantCulture)},{eye.Z.ToString("F1", CultureInfo.InvariantCulture)}\n" +
                    $"look-at={lookTarget.X.ToString("F1", CultureInfo.InvariantCulture)},{lookTarget.Y.ToString("F1", CultureInfo.InvariantCulture)},{lookTarget.Z.ToString("F1", CultureInfo.InvariantCulture)}\n" +
                    $"camera-mode=static Camera.SetLookAt per captured frame (tactical view overwritten by game.Update)\n" +
                    $"resolution={CaptureWidth}x{CaptureHeight}\n" +
                    $"capture-every-nth-logic-frame={captureEveryNthFrame}\nframes-captured={captured}\n" +
                    $"dozer-definition={DozerDefinitionName}\norder=(550,150)->(550,1100)\n");
                Console.WriteLine($"r3-g8 capture {angleName}: {captured} frames, trace {traceLines.Count - 1} samples -> {framesRoot}");
            }

            // The GIF deliverable is assembled from the portable frames so the crossing
            // evidence is complete without the operator capture root; each GIF is verified
            // against the captured frame count (20-30 frames at 4 fps).
            var gifPaths = new List<string>();

            foreach (var angleName in new[] { "side", "oblique" })
            {
                var gifPath = BuildTraversalGif(framesRoot, portableRoot, angleName);
                gifPaths.Add(gifPath);
            }

            PublishCaptureRoot(portableRoot, gifPaths);
        }
        finally
        {
            // Scratch and portable roots intentionally preserved for capture evidence.
        }
    }

    /// <summary>
    /// Assembles the capture frames for one angle into the portable GIF (4 fps, 250 ms per
    /// frame) and returns the GIF path; the frame count must match the capture set and
    /// stay within the 20-30 frame band the evidence contract requires.
    /// </summary>
    private static string BuildTraversalGif(string framesRoot, string portableRoot, string angleName)
    {
        var pngPaths = Directory.GetFiles(framesRoot, $"{angleName}-*.png").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.True(
            pngPaths.Length is >= 20 and <= 30,
            $"{angleName}: {pngPaths.Length} captured frames; the evidence contract needs 20-30.");

        using var master = Image.Load(pngPaths[0]);

        for (var index = 1; index < pngPaths.Length; index++)
        {
            using var next = Image.Load(pngPaths[index]);
            master.Frames.AddFrame(next.Frames.RootFrame);
        }

        foreach (var frame in master.Frames)
        {
            frame.Metadata.GetGifMetadata().FrameDelay = GifFrameDelayCentiseconds;
        }

        var gifPath = Path.Combine(portableRoot, $"r3-g8-bridge-traversal-{angleName}-v1.gif");
        master.SaveAsGif(gifPath);

        using var verify = Image.Load(gifPath);
        Assert.Equal(pngPaths.Length, verify.Frames.Count);

        Console.WriteLine($"r3-g8 gif {angleName}: {verify.Frames.Count} frames @ 4 fps -> {gifPath} ({new FileInfo(gifPath).Length} bytes)");
        return gifPath;
    }

    /// <summary>
    /// Best-effort publication of the portable capture set to the operator capture root:
    /// GIFs top-level and the frames directory beneath it. Existing published files are
    /// never overwritten (the historical copy stays); without the mount this is skipped.
    /// </summary>
    private static void PublishCaptureRoot(string portableRoot, IReadOnlyList<string> gifPaths)
    {
        if (!Directory.Exists(ExternalCaptureRoot))
        {
            Console.WriteLine($"external capture root not mounted, skipped: {ExternalCaptureRoot}");
            return;
        }

        foreach (var gifPath in gifPaths)
        {
            PublishFile(gifPath, ExternalCaptureRoot, Path.GetFileName(gifPath));
        }

        var portableFrames = Path.Combine(portableRoot, "frames", "r3-g8-bridge-traversal-v1");
        var externalFrames = Path.Combine(ExternalCaptureRoot, "frames", "r3-g8-bridge-traversal-v1");
        Directory.CreateDirectory(externalFrames);

        foreach (var file in Directory.GetFiles(portableFrames))
        {
            PublishFile(file, externalFrames, Path.GetFileName(file));
        }
    }

    private static void PublishFile(string sourcePath, string directory, string publishedName)
    {
        var publishedPath = Path.Combine(directory, publishedName);

        if (File.Exists(publishedPath))
        {
            // Never overwrite published evidence; the existing file is the historical copy.
            Console.WriteLine($"published capture already exists, kept: {publishedPath}");
            return;
        }

        File.Copy(sourcePath, publishedPath);
        Console.WriteLine($"published capture: {publishedPath} ({new FileInfo(publishedPath).Length} bytes)");
    }

    private static List<TraceSample> RunDozerTraversal(Game game, (float X, float Y) start, (float X, float Y) goal)
    {
        var player = game.Scene3D.LocalPlayer ?? game.Scene3D.Players[0];
        player.DisableAI();

        var dozerDefinition = game.AssetStore.ObjectDefinitions.GetByName(DozerDefinitionName);
        Assert.True(dozerDefinition != null, $"definition {DozerDefinitionName} not found.");

        var dozer = game.Scene3D.GameObjects.CreateObject(dozerDefinition!, player);
        Assert.NotNull(dozer);

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
        const int maxFrames = 6000;

        for (var frame = 0; frame < maxFrames; frame++)
        {
            game.Update(Array.Empty<InputMessage>());

            var position = dozer.Translation;
            var terrainZ = game.TerrainLogic.HeightMap.GetHeight(position.X, position.Y);
            var surfaceZ = game.TerrainLogic.GetBridgeSurfaceZ(position.X, position.Y);
            trace.Add(new TraceSample(frame, position.X, position.Y, position.Z, terrainZ, surfaceZ));

            if (position.Y >= goal.Y - 2f)
            {
                break;
            }
        }

        return trace;
    }

    private static float MaxZStep(List<TraceSample> trace)
    {
        var max = 0f;

        for (var index = 1; index < trace.Count; index++)
        {
            max = MathF.Max(max, MathF.Abs(trace[index].Z - trace[index - 1].Z));
        }

        return max;
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

    private static float ReloadedLatticeHeight(MapInterchange model, int latticeWidth, int latticeX, int latticeY)
    {
        var cx = latticeX / ChunkLayout.IntervalsPerChunk;
        var cy = latticeY / ChunkLayout.IntervalsPerChunk;
        var chunk = Assert.Single(model.Layout.Chunks, candidate => candidate.Key.Cx == cx && candidate.Key.Cy == cy);
        var originX = cx * ChunkLayout.IntervalsPerChunk;
        var originY = cy * ChunkLayout.IntervalsPerChunk;
        var coreWidth = Math.Min(ChunkLayout.PointsPerChunkAxis, model.Layout.LatticeWidth - originX);

        return chunk.Heights.Samples[((latticeY - originY + 1) * (coreWidth + 2)) + (latticeX - originX + 1)];
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
                Assert.NotNull(terrain);
                Assert.NotNull(materials);
                Assert.NotNull(masks);

                descriptors.Add(new PayloadChunkDescriptor(cx, cy, terrain!, materials!, masks!, null));
            }
        }

        // One straight two-point bridge line crossing the gorge, in payload game units.
        var features = new List<GeneratedFeature>
        {
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(BridgeEndpoints[0].X, BridgeEndpoints[0].Y)), new GeoPosition(new Vector2d(BridgeEndpoints[1].X, BridgeEndpoints[1].Y))]),
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
            "r3-g8-bridge-traversal",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors")] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    private static string NewScratchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-bridge-traversal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private readonly record struct TraceSample(int Frame, float X, float Y, float Z, float TerrainZ, float? SurfaceZ);

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
