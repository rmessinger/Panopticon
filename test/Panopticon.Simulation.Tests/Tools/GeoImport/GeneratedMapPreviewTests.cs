using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Panopticon.Data.Map;
using Panopticon.Geospatial.Preview;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Scripting;
using Panopticon.Tests.Data;
using Panopticon.Tools.GeoImport;
using Xunit;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Card 24 native proof (WI-B4b-24): the actual public GeoImport CLI produces a playable
/// .map from the accepted synthetic fixture, previewing through the real installed
/// Generals terrain texture catalog. A fresh real Game boots that map in an isolated
/// user-data root, runs exactly 300 logic frames, and the terrain heights, impassability
/// mask, and waypoints are checked against the fixture's independent analytic oracle.
/// The offscreen capture is a real native render (native lighting only, no scene staging)
/// asserted non-black and non-uniform.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedMapPreviewTests
{
    private const string MapFileName = "card24preview";

    private const string CropArgument = "1,0,2,1";

    /// <summary>Crop origin lattice X: chunk 1 * 128 intervals; engine X = payload X - 1280.</summary>
    private const int CropBaseLatticeX = 128;

    private const int StoredLatticeWidth = 129;

    /// <summary>The forced eastern edge node column, stored index 128.</summary>
    private const int StoredEdgeIndex = 128;

    private const int FrameCount = 300;

    /// <summary>Under the 900-1500 terrain-void camera band (docs/screenshots.md).</summary>
    private const float CameraHeight = 560f;

    private const string Scope = "card24 native preview proof (WI-B4b-24)";

    [GameFact(SageGame.CncGenerals)]
    public void CliPreview_ProducesPlayableMap_RealGameFrames_AnalyticHeightsAndNativeCapture()
    {
        using var fixture = Card24ProofFixture.Create();

        // Kept after the run for evidence preservation; never deleted here.
        var scratchRoot = Path.Combine(Path.GetTempPath(), "opencode", "card24-native-proof", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchRoot);

        var payloadDir = Path.Combine(scratchRoot, MapFileName);
        var userRoot = Path.Combine(scratchRoot, "user");
        var mapsDir = Path.Combine(userRoot, "Maps");
        var mapPath = Path.Combine(mapsDir, MapFileName + ".map");
        var pngPath = Path.Combine(scratchRoot, MapFileName + "-frame.png");

        var realUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Command and Conquer Generals Data");
        var realMapCache = Path.Combine(realUserDataFolder, @"Maps\MapCache.ini");
        var realFolderExistedBefore = Directory.Exists(realUserDataFolder);
        var realMapCacheWriteBefore = File.Exists(realMapCache) ? File.GetLastWriteTimeUtc(realMapCache) : (DateTime?)null;

        var fixtureBaseline = HashInventory(fixture.Root);

        RunCliImport(fixture, payloadDir);
        Assert.True(Directory.Exists(payloadDir), $"Import did not publish the payload directory: {payloadDir}");
        var payloadAfterImport = HashInventory(payloadDir);

        Directory.CreateDirectory(mapsDir);
        RunCliPreview(fixture, payloadDir, mapsDir);
        Assert.True(File.Exists(mapPath), $"Preview did not write the map: {mapPath}");

        var mapSha256 = Sha256File(mapPath);
        AssertInventoryUnchanged(fixtureBaseline, HashInventory(fixture.Root), "fixture sources after preview");
        AssertInventoryUnchanged(payloadAfterImport, HashInventory(payloadDir), "payload after preview");

        AssertMapContent(mapPath);

        uint startFrame = 0;
        uint frameAfter300 = 0;
        uint frameAfterCapture = 0;
        var controlPoints = new List<ControlPointMetric>();
        var luminance = new LuminanceStats(0d, 0d, 0d, 0d, 0, 0d);

        var game = new Game(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            preferredBackend: null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
            window: null);

        try
        {
            Assert.Equal(userRoot, game.UserDataFolder);

            // Generated maps carry the skirmish sides America and GLA only
            // (MapFile.SkirmishFactions); China exists solely on shipped maps.
            var playerSettings = new PlayerSetting[]
            {
                new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
                new(1, "FactionGLA", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "GLA"),
            };

            game.StartSkirmishOrMultiPlayerGame(
                $"Maps/{MapFileName}.map",
                new EchoConnection(),
                playerSettings,
                1,
                isMultiPlayer: false);
            game.StartRun();
            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
            foreach (var player in game.Scene3D.Players)
            {
                player.DisableAI();
            }

            game.EnableFixedLogicTimestep();

            var empty = Array.Empty<InputMessage>();
            startFrame = game.GameLogic.CurrentFrame.Value;
            for (var i = 0; i < FrameCount; i++)
            {
                game.Update(empty);
            }

            frameAfter300 = game.GameLogic.CurrentFrame.Value;
            Assert.Equal(startFrame + FrameCount, frameAfter300);
            Assert.True(game.InGame, "Skirmish start on the generated map did not reach the in-game state.");
            Assert.NotNull(game.Scene3D.Terrain);

            AssertTerrainHeights(game, controlPoints);

            // Pause before rendering: the warmup renders must not advance the logic frame,
            // so the capture shows the exact state the 300 proof frames ended in.
            game.IsLogicRunning = false;

            // Height first, then aim: LookAt clamps against the map constraint for the
            // CURRENT camera height, so aiming before raising it lands off target.
            var view = game.Scene3D.TacticalView;
            view.InitHeightForMap();
            view.SetAngleAndPitchToDefault();
            view.SnapHeightAboveGround(CameraHeight);
            view.LookAt(new Vector3(640f, 640f, 0f));

            for (var i = 0; i < 3; i++)
            {
                game.Update(empty);
                game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
                game.Render();
            }

            frameAfterCapture = game.GameLogic.CurrentFrame.Value;
            Assert.Equal(startFrame + FrameCount, frameAfterCapture);

            var frame = FrameCapturer.Capture(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
            luminance = LuminanceStats.Compute(frame.PixelData);
            Assert.True(
                luminance.BrightFraction >= 0.005d,
                $"Render is black: only {luminance.BrightFraction:P2} of pixels have luminance > 8 "
                + $"(min {luminance.Min}, max {luminance.Max}, mean {luminance.Mean:F1}).");
            Assert.True(
                luminance.StandardDeviation > 4.0d && luminance.Distinct > 32,
                $"Render is uniform: stddev {luminance.StandardDeviation:F2}, distinct values {luminance.Distinct}.");

            FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, pngPath);
        }
        finally
        {
            game.Dispose();
        }

        // The operator's real Generals user-data folder is untouched by the sandbox run.
        Assert.Equal(realFolderExistedBefore, Directory.Exists(realUserDataFolder));
        var realMapCacheWriteAfter = File.Exists(realMapCache) ? File.GetLastWriteTimeUtc(realMapCache) : (DateTime?)null;
        Assert.Equal(realMapCacheWriteBefore, realMapCacheWriteAfter);

        // UserMapCache writes Path.Combine(root, @"Maps\MapCache.ini") verbatim; on Linux
        // that is one file with a backslash in its name, not the Maps directory.
        var sandboxMapCache = Path.Combine(userRoot, @"Maps\MapCache.ini");
        Assert.True(File.Exists(sandboxMapCache), $"Expected the sandbox map cache: {sandboxMapCache}");
        Assert.Contains(MapFileName, File.ReadAllText(sandboxMapCache));

        AssertInventoryUnchanged(fixtureBaseline, HashInventory(fixture.Root), "fixture sources after the run");
        AssertInventoryUnchanged(payloadAfterImport, HashInventory(payloadDir), "payload after the run");
        Assert.Equal(mapSha256, Sha256File(mapPath));

        var envShotSkippedExists = false;
        string? envMetricsPath = null;
        var envShotPath = Environment.GetEnvironmentVariable("PANOPTICON_GEO_PREVIEW_SHOT");

        if (!string.IsNullOrEmpty(envShotPath))
        {
            // The env shot path is never overwritten: an existing file skips the copy.
            envShotSkippedExists = File.Exists(envShotPath);

            if (!envShotSkippedExists)
            {
                var envShotDirectory = Path.GetDirectoryName(envShotPath);

                if (!string.IsNullOrEmpty(envShotDirectory))
                {
                    Directory.CreateDirectory(envShotDirectory);
                }

                File.Copy(pngPath, envShotPath);
            }

            envMetricsPath = envShotPath + ".metrics.json";
        }

        var metricsJson = JsonSerializer.Serialize(
            BuildMetrics(fixtureBaseline, payloadAfterImport, mapPath, mapSha256, startFrame, frameAfter300, frameAfterCapture, controlPoints, luminance, pngPath, envShotSkippedExists),
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(Path.Combine(scratchRoot, "preview-runtime.metrics.json"), metricsJson);

        if (envMetricsPath is not null && !File.Exists(envMetricsPath))
        {
            File.WriteAllText(envMetricsPath, metricsJson);
        }
    }

    private static void RunCliImport(Card24ProofFixture fixture, string payloadDir)
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();
        var exit = GeoImportCli.Run(
            new[] { "import", "--manifest", fixture.ManifestPath, "--out", payloadDir },
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("published: ", standardOut.ToString());
        Assert.Equal(string.Empty, errorOut.ToString());
    }

    private static void RunCliPreview(Card24ProofFixture fixture, string payloadDir, string mapsDir)
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();
        var exit = GeoImportCli.Run(
            new[] { "preview", "--payload", payloadDir, "--textures", fixture.TexturesJsonPath, "--crop", CropArgument, "--out", mapsDir },
            standardOut,
            errorOut,
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("preview: ", standardOut.ToString());
        Assert.Contains(MapFileName + ".map", standardOut.ToString());
        Assert.Equal(string.Empty, errorOut.ToString());
    }

    private static void AssertMapContent(string mapPath)
    {
        MapFile mapFile;

        using (var stream = File.OpenRead(mapPath))
        {
            mapFile = MapFile.FromStream(stream);
        }

        Assert.Equal(
            new[] { Card24ProofFixture.GrassTextureName, Card24ProofFixture.PavedTextureName },
            mapFile.BlendTileData.Textures.Select(texture => texture.Name).ToArray());

        // The full stored mask in engine coordinates against the payload-space oracle:
        // eastern edge column, southern edge row, plaza ring nodes strictly inside.
        var mismatches = new List<string>();

        for (var y = 0; y < StoredLatticeWidth; y++)
        {
            for (var x = 0; x < StoredLatticeWidth; x++)
            {
                var actual = mapFile.BlendTileData.Impassability[x, y];
                var expected = (x == StoredEdgeIndex) || (y == 0) || Card24ProofFixture.IsPlazaLattice(x + CropBaseLatticeX, y);

                if (actual != expected)
                {
                    mismatches.Add($"engine ({x * 10}, {y * 10}): map {actual}, expected {expected}");
                }
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} impassable mismatches, first 10: {string.Join("; ", mismatches.Take(10))}");

        var playerOne = FindWaypoint(mapFile, "Player_1_Start");
        Assert.NotNull(playerOne);
        Assert.Equal(new Vector3(220f, 300f, 0f), playerOne!.Position);

        var playerTwo = FindWaypoint(mapFile, "Player_2_Start");
        Assert.NotNull(playerTwo);
        Assert.Equal(new Vector3(1020f, 900f, 0f), playerTwo!.Position);

        var ridgePeak = FindWaypoint(mapFile, "ridgepeak");
        Assert.NotNull(ridgePeak);
        Assert.Equal(640f, ridgePeak!.Position.X);
        Assert.Equal(640f, ridgePeak.Position.Y);

        Assert.Null(FindWaypoint(mapFile, "farpoint"));
    }

    private static void AssertTerrainHeights(Game game, List<ControlPointMetric> controlPoints)
    {
        var quantum = GeneratedMapPreviewBuilder.HeightQuantumGameUnits;
        var cropMinimum = (double)Card24ProofFixture.ExpectedCropMinimumGameZ();

        foreach (var (engineX, engineY) in new[] { (640d, 640d), (100d, 100d), (1200d, 1200d) })
        {
            var gx = ((int)engineX / 10) + CropBaseLatticeX;
            var gy = (int)engineY / 10;
            var expected = (double)Card24ProofFixture.ExpectedHeightAtLattice(gx, gy) - cropMinimum;
            var measured = game.Scene3D.Terrain.GetGroundHeight(new Vector2((float)engineX, (float)engineY));

            Assert.True(measured.HasValue, $"No terrain height at engine ({engineX}, {engineY}).");

            var error = Math.Abs((double)measured.Value - expected);

            // Measuring the ridge peak at engine (640, 640) against the payload-space
            // analytic value proves the non-zero XY crop translation: without it the
            // engine point would sample payload (640, 640), whose plane height is 29.2.
            Assert.True(
                error <= quantum,
                $"Engine ({engineX}, {engineY}) measured {measured.Value}, analytic {expected}, "
                + $"error {error} exceeds one height quantum {quantum}.");

            controlPoints.Add(new ControlPointMetric(engineX, engineY, gx, gy, expected, measured.Value, error));
        }
    }

    private static MapObject? FindWaypoint(MapFile mapFile, string name)
    {
        foreach (var mapObject in mapFile.ObjectsList.Objects)
        {
            if (mapObject.TypeName == Waypoint.ObjectTypeName
                && mapObject.Properties.TryGetValue("waypointName", out var property)
                && property.Value is string value
                && value == name)
            {
                return mapObject;
            }
        }

        return null;
    }

    private static PreviewRuntimeMetrics BuildMetrics(
        Inventory fixtureBaseline,
        Inventory payloadAfterImport,
        string mapPath,
        string mapSha256,
        uint startFrame,
        uint frameAfter300,
        uint frameAfterCapture,
        List<ControlPointMetric> controlPoints,
        LuminanceStats luminance,
        string pngPath,
        bool envShotSkippedExists)
    {
        return new PreviewRuntimeMetrics
        {
            TimestampLocal = LocalTimestamp(),
            Scope = Scope,
            Commit = Environment.GetEnvironmentVariable("PANOPTICON_GEO_PROOF_COMMIT"),
            MapSha256 = mapSha256,
            MapBytes = new FileInfo(mapPath).Length,
            PayloadAggregateSha256 = payloadAfterImport.Aggregate,
            FixtureAggregateSha256 = fixtureBaseline.Aggregate,
            StartFrame = startFrame,
            FrameAfter300 = frameAfter300,
            FrameAfterCapture = frameAfterCapture,
            ControlPoints = controlPoints.ToArray(),
            Luminance = luminance,
            PngPath = pngPath,
            EnvShotSkippedExists = envShotSkippedExists ? true : null,
        };
    }

    private static string LocalTimestamp()
    {
        return DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
    }

    private static Inventory HashInventory(string root)
    {
        var files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Relative = Path.GetRelativePath(root, path).Replace('\\', '/'),
                Hash = Sha256File(path),
            })
            .OrderBy(entry => entry.Relative, StringComparer.Ordinal)
            .ToList();

        // The aggregate covers relative names and bytes, so one equality proves the whole
        // inventory byte-identical.
        var manifest = string.Join("\n", files.Select(entry => $"{entry.Relative} {entry.Hash}"));

        return new Inventory(
            Sha256Text(manifest),
            files.ToDictionary(entry => entry.Relative, entry => entry.Hash, StringComparer.Ordinal));
    }

    private static void AssertInventoryUnchanged(Inventory before, Inventory after, string what)
    {
        if (before.Aggregate == after.Aggregate)
        {
            return;
        }

        var changes = before.Files.Keys
            .Union(after.Files.Keys)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Where(key => !string.Equals(before.Files.GetValueOrDefault(key), after.Files.GetValueOrDefault(key), StringComparison.Ordinal))
            .Select(key => $"{key}: {before.Files.GetValueOrDefault(key) ?? "absent"} -> {after.Files.GetValueOrDefault(key) ?? "absent"}");

        Assert.Fail($"{what} changed: {string.Join("; ", changes)}");
    }

    private static string Sha256File(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static string Sha256Text(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private sealed record Inventory(string Aggregate, IReadOnlyDictionary<string, string> Files);

    private sealed record ControlPointMetric(
        double EngineX,
        double EngineY,
        int LatticeGx,
        int LatticeGy,
        double AnalyticExpected,
        double Measured,
        double AbsError);

    private sealed record LuminanceStats(
        double Min,
        double Max,
        double Mean,
        double StandardDeviation,
        int Distinct,
        double BrightFraction)
    {
        // PixelData is tightly packed 4 bytes per pixel in BGRA order (FrameCapturer).
        public static LuminanceStats Compute(byte[] pixels)
        {
            var count = pixels.Length / 4;
            var values = new int[count];
            long sum = 0;
            var bright = 0;

            for (var i = 0; i < count; i++)
            {
                var offset = i * 4;
                var r = pixels[offset + 2];
                var g = pixels[offset + 1];
                var b = pixels[offset];
                var luminance = (int)((0.2126d * r) + (0.7152d * g) + (0.0722d * b));
                values[i] = luminance;
                sum += luminance;

                if (luminance > 8)
                {
                    bright++;
                }
            }

            var mean = (double)sum / count;
            double squaredSum = 0;

            foreach (var value in values)
            {
                var delta = value - mean;
                squaredSum += delta * delta;
            }

            return new LuminanceStats(
                values.Min(),
                values.Max(),
                mean,
                Math.Sqrt(squaredSum / count),
                values.Distinct().Count(),
                (double)bright / count);
        }
    }

    private sealed class PreviewRuntimeMetrics
    {
        public string TimestampLocal { get; init; } = string.Empty;

        public string Scope { get; init; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Commit { get; init; }

        public string MapSha256 { get; init; } = string.Empty;

        public long MapBytes { get; init; }

        public string PayloadAggregateSha256 { get; init; } = string.Empty;

        public string FixtureAggregateSha256 { get; init; } = string.Empty;

        public uint StartFrame { get; init; }

        public uint FrameAfter300 { get; init; }

        public uint FrameAfterCapture { get; init; }

        public ControlPointMetric[] ControlPoints { get; init; } = Array.Empty<ControlPointMetric>();

        public LuminanceStats Luminance { get; init; } = new(0d, 0d, 0d, 0d, 0, 0d);

        public string PngPath { get; init; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EnvShotSkippedExists { get; init; }
    }
}
