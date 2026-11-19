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
using Panopticon.Mathematics;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// WP1022 installed-game proof. The portable round trip stages a synthetic 2x2-chunk
/// payload (plane terrain with a gorge) whose vector layer carries two straight two-point
/// bridge lines, converts with and without bridge rules, and reloads both packages through
/// PackageReader: the ON package carries two adjacent BridgeStart/BridgeEnd placed-object
/// pairs at the line endpoints, the OFF package carries none. The installed fact then loads
/// both packages into the real game through Game.LoadMap: without rules Scene3D.Bridges is
/// empty; with rules it holds both bridges, the Concrete template's tower objects spawn
/// next to the line endpoints, and same-camera OFF/ON captures differ measurably pixel by
/// pixel.
/// </summary>
[Collection("SerialGameTests")]
public sealed class GeneratedPackageBridgeLoadTests
{
    /// <summary>Above the 900-1500 terrain-void camera band framing needs; the bridge close-up sits below it (docs/screenshots.md).</summary>
    private const float CameraHeight = 700f;

    /// <summary>
    /// Operator capture root, mounted on the agent host only; absent in the MCP container.
    /// Publication there is an opt-in extra copy, never a test dependency.
    /// </summary>
    private const string ExternalCaptureRoot = "/mnt/pit2/Network Storage/temp";

    /// <summary>The staged bridge-a midpoint, in game units.</summary>
    private static readonly Vector2 BridgeALookAt = new(550f, 650f);

    private static readonly (double X, double Y)[] BridgeAEndpoints = [(550d, 300d), (550d, 1000d)];

    private static readonly (double X, double Y)[] BridgeBEndpoints = [(1800d, 200d), (1750d, 1050d)];

    [Fact]
    public void BridgePackage_ReloadCarriesAdjacentPairs_WithoutRulesNoBridges()
    {
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var packageOn = Path.Combine(scratchRoot, "package-on");
            var packageOff = Path.Combine(scratchRoot, "package-off");

            Convert(payloadDir, packageOn, withRules: true);
            Convert(payloadDir, packageOff, withRules: false);

            var on = new PackageReader(packageOn).Read();
            var objects = on.Objects!.Objects;
            Assert.Equal(4, objects.Count);

            foreach (var (start, end, pairName, endpoints) in new[]
                     {
                         (objects[0], objects[1], "Bridge_1", BridgeAEndpoints),
                         (objects[2], objects[3], "Bridge_2", BridgeBEndpoints),
                     })
            {
                Assert.Equal((uint)RoadType.BridgeStart, start.RoadType);
                Assert.Equal((uint)RoadType.BridgeEnd, end.RoadType);
                Assert.Equal("Concrete", start.Type.Name);
                Assert.Equal("Concrete", end.Type.Name);
                Assert.Equal($"{pairName}_Start", start.StableId);
                Assert.Equal($"{pairName}_End", end.StableId);
                Assert.Equal(endpoints[0].X, start.X.Value);
                Assert.Equal(endpoints[0].Y, start.Y.Value);
                Assert.Equal(endpoints[1].X, end.X.Value);
                Assert.Equal(endpoints[1].Y, end.Y.Value);
            }

            var off = new PackageReader(packageOff).Read();
            Assert.DoesNotContain(
                off.Objects?.Objects ?? [],
                placed => placed.StableId.StartsWith("Bridge_", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void BridgePackage_BridgesRenderAtEndpoints_AreAbsentWithoutRules()
    {
        // Kept after the run for evidence preservation (the capture PNGs); never deleted here.
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

            var offPng = Path.Combine(scratchRoot, "r3-g7-bridges-off-frame.png");
            CapturedFrame offFrame;

            // Portable per-run capture publication: always written here, external copy only
            // when the operator capture root is mounted.
            var portableCaptures = Path.Combine(Path.GetTempPath(), "r3-g7-bridges", Guid.NewGuid().ToString("N"));

            game.LoadMap(packageOff, [], GameType.SinglePlayer);
            {
                Assert.NotNull(game.Scene3D.Terrain);

                // Same-camera framing for the OFF and ON captures.
                AimCamera(game);

                Assert.Empty(game.Scene3D.Bridges);
                Assert.False(
                    game.Scene3D.GameObjects.Objects.Any(gameObject => gameObject.Definition.Name.StartsWith("BridgeTower", StringComparison.Ordinal)),
                    "the OFF package must not spawn bridge tower objects.");

                for (var i = 0; i < 3; i++)
                {
                    game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
                    game.Render();
                }

                offFrame = FrameCapturer.Capture(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
                FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, offPng);
                Assert.True(new FileInfo(offPng).Length > 0, "the OFF capture file is empty");
            }

            var onPng = Path.Combine(scratchRoot, "r3-g7-bridges-on-frame.png");

            game.LoadMap(packageOn, [], GameType.SinglePlayer);
            {
                Assert.NotNull(game.Scene3D.Terrain);
                AimCamera(game);

                // The paired map objects materialized as two engine bridges.
                var bridges = game.Scene3D.Bridges;
                Assert.True(bridges.Length >= 2, $"Scene3D.Bridges holds {bridges.Length} entries; the two bridge lines must place two bridges.");

                // The Concrete template resolved and its meshes built: each bridge spawns
                // four tower objects, two flanking each endpoint of its line.
                var towers = game.Scene3D.GameObjects.Objects
                    .Where(gameObject => gameObject.Definition.Name.StartsWith("BridgeTower", StringComparison.Ordinal))
                    .ToArray();
                Assert.True(towers.Length >= 8, $"Bridge tower objects: {towers.Length}; two bridges must spawn eight.");

                foreach (var endpoint in BridgeAEndpoints.Concat(BridgeBEndpoints))
                {
                    var nearest = towers.Min(tower => PlanarDistance(tower.Translation, new Vector3((float)endpoint.X, (float)endpoint.Y, 0f)));
                    Assert.True(
                        nearest < 120f,
                        $"No bridge tower spawns near the line endpoint ({endpoint.X}, {endpoint.Y}); nearest tower is {nearest} units away.");
                }

                for (var i = 0; i < 3; i++)
                {
                    game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
                    game.Render();
                }

                var onFrame = FrameCapturer.Capture(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
                FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, onPng);
                Assert.True(new FileInfo(onPng).Length > 0, "the ON capture file is empty");

                var diff = PixelDiffStats.Compute(offFrame.PixelData, onFrame.PixelData);
                var onLuminance = LuminanceStats.Compute(onFrame.PixelData);
                Assert.True(
                    onLuminance.BrightFraction >= 0.005d,
                    $"ON render is black: only {onLuminance.BrightFraction:P2} of pixels have luminance > 8.");

                // The bridges occupy a visible share of the frame; the OFF/ON difference
                // quantifies it.
                Assert.True(
                    diff.Fraction > 0.02d,
                    $"The OFF and ON captures are nearly identical ({diff.Fraction:P2} differing pixels); the bridges did not visibly change the frame.");

                Console.WriteLine(
                    "r3-g7 bridges pixel diff: differing pixels "
                    + $"{diff.Differing}/{diff.Count} ({diff.Fraction:P2}, mean absolute channel delta {diff.MeanAbsoluteDelta:F2}); "
                    + $"bridges {bridges.Length}, towers {towers.Length}");

                PublishCapture(offPng, portableCaptures, "r3-g7-bridges-off-v1.png");
                PublishCapture(onPng, portableCaptures, "r3-g7-bridges-on-v1.png");
            }
        }
        finally
        {
            // Scratch root intentionally preserved for capture evidence.
        }
    }

    private static void AimCamera(Game game)
    {
        var view = game.Scene3D.TacticalView;
        view.InitHeightForMap();
        view.SetAngleAndPitchToDefault();
        view.SnapHeightAboveGround(CameraHeight);
        view.LookAt(new Vector3(BridgeALookAt.X, BridgeALookAt.Y, 0f));
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

        // Two straight two-point bridge lines crossing the gorge, in payload game units.
        var features = new List<GeneratedFeature>
        {
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(BridgeAEndpoints[0].X, BridgeAEndpoints[0].Y)), new GeoPosition(new Vector2d(BridgeAEndpoints[1].X, BridgeAEndpoints[1].Y))]),
                "vectors",
                "bridge-a",
                "vectors/bridge-a#0",
                -1),
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(BridgeBEndpoints[0].X, BridgeBEndpoints[0].Y)), new GeoPosition(new Vector2d(BridgeBEndpoints[1].X, BridgeBEndpoints[1].Y))]),
                "vectors",
                "bridge-b",
                "vectors/bridge-b#0",
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
        // Plane terrain with a north-south gorge strip the bridges cross.
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
            "r3-g7-bridges",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors")] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    /// <summary>
    /// Copies the capture to a portable per-run directory (always works, no external
    /// dependency), and additionally to the external capture root when it is mounted
    /// (host-local runs). The external copy never overwrites an existing published file;
    /// without the mount the portable copy alone backs the evidence report.
    /// </summary>
    private static void PublishCapture(string pngPath, string portableRoot, string publishedName)
    {
        Directory.CreateDirectory(portableRoot);
        var portablePath = Path.Combine(portableRoot, publishedName);
        File.Copy(pngPath, portablePath);
        Console.WriteLine($"portable capture: {portablePath} ({new FileInfo(portablePath).Length} bytes)");

        if (!Directory.Exists(ExternalCaptureRoot))
        {
            Console.WriteLine($"external capture root not mounted, skipped: {ExternalCaptureRoot}");
            return;
        }

        var publishedPath = Path.Combine(ExternalCaptureRoot, publishedName);
        if (File.Exists(publishedPath))
        {
            // Never overwrite published evidence; the existing file is the historical copy.
            Console.WriteLine($"published capture already exists, kept: {publishedPath}");
            return;
        }

        File.Copy(pngPath, publishedPath);
        Console.WriteLine($"published capture: {publishedPath} ({new FileInfo(publishedPath).Length} bytes)");
    }

    private static float PlanarDistance(Vector3 left, Vector3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;

        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static string NewScratchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g7-bridges", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed record PixelDiffStats(int Count, int Differing, double Fraction, double MeanAbsoluteDelta)
    {
        // PixelData is tightly packed 4 bytes per pixel in BGRA order (FrameCapturer).
        public static PixelDiffStats Compute(byte[] left, byte[] right)
        {
            Assert.Equal(left.Length, right.Length);
            var count = left.Length / 4;
            var differing = 0;
            long absoluteDelta = 0;

            for (var i = 0; i < left.Length; i++)
            {
                var delta = Math.Abs(left[i] - right[i]);
                absoluteDelta += delta;

                if (delta > 8)
                {
                    differing++;
                }
            }

            return new PixelDiffStats(count, differing, (double)differing / count, (double)absoluteDelta / left.Length);
        }
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

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
