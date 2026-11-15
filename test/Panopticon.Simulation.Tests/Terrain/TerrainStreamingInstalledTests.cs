#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Graphics;
using Panopticon.Rendering;
using Panopticon.Terrain;
using Panopticon.Terrain.Cdlod;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.TestHarness;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Terrain;

[Collection("SerialGameTests")]
public sealed class TerrainStreamingInstalledTests
{
    private const int PlayableSize = 4096;
    private const int BorderWidth = 6;
    private const int LatticeSize = PlayableSize + (2 * BorderWidth) + 1; // 4109

    private readonly ITestOutputHelper _output;

    public TerrainStreamingInstalledTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void BootedSmallMap_HasStreamingDisabled()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        var profile = CaptureProfileLoader.LoadEmbeddedCapture("vr-terrain-scorch.json");
        profile.DurationFrames = 1;
        profile.SettleFrames = 1;

        var runId = Guid.NewGuid().ToString("N");
        var logDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            "log", "b4a-g14-streaming-2026-11-11", $"small-map-{runId}");

        var settings = new CaptureRunSettings(null, false, logDir, GraphicsSettings.Legacy(), FixedRenderClock: true)
        {
            BeforeRender = (game, frame) =>
            {
                var terrain = game.Scene3D.Terrain;
                Assert.NotNull(terrain);
                Assert.False(terrain.StreamingEnabled, "Small Alpine Assault map should not have streaming enabled.");
                Assert.Equal(0, terrain.StreamedPatchCount);
                Assert.Equal(0L, terrain.StreamedVertexBytes);
                _output.WriteLine($"Small map verified: StreamingEnabled={terrain.StreamingEnabled}, StreamedPatchCount={terrain.StreamedPatchCount}");
            }
        };

        var result = ScenarioCaptureRunner.Run(installation, profile, settings, new ConsoleTestHarnessReporter());
        Assert.Equal(0, result.ExitCode);
    }

    [GameFact(SageGame.CncGenerals)]
    public void Playable4096Package_StreamsVisiblePatches_WithDisposalBoundedResidencyTimingAndCapture()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"streaming-proof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var swBuild = Stopwatch.StartNew();
            var packagePath = CreatePlayable4096DirectoryPackage(tempDir);
            swBuild.Stop();
            _output.WriteLine($"Built 4096 playable package at '{packagePath}' in {swBuild.ElapsedMilliseconds} ms.");

            var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
            var profile = CaptureProfileLoader.LoadEmbeddedCapture("capture-b4a-streaming-4096.json");
            profile.MapPath = packagePath;
            profile.DurationFrames = 4;
            profile.SettleFrames = 2;

            var runId = Guid.NewGuid().ToString("N");
            var logDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                "log", "b4a-g14-streaming-2026-11-11", $"streaming-sweep-{runId}");
            Directory.CreateDirectory(logDir);

            // Camera target trajectory over disjoint regions of the 4096 map:
            // Frame 0: (1000, 1000) - southwest quadrant
            // Frame 1: (2500, 2500) - northeast center
            // Frame 2: (3500, 3500) - far northeast
            // Frame 3: (1000, 1000) - return to southwest to verify bounded residency / no leak across loop
            var cameraTargets = new Vector3[]
            {
                new(1000f, 1000f, 0f),
                new(2000f, 2000f, 0f),
                new(3000f, 3000f, 0f),
                new(1000f, 1000f, 0f),
            };

            var patchCounts = new int[cameraTargets.Length];
            double firstUpdateMs = 0;
            double cachedUpdateMs = 0;
            long peakWorkingSet = 0;
            long peakTotalMemory = 0;

            var settings = new CaptureRunSettings(null, false, logDir, GraphicsSettings.Legacy(), FixedRenderClock: true)
            {
                BeforeRender = (game, frame) =>
                {
                    if (frame >= cameraTargets.Length)
                    {
                        return;
                    }

                    var scene = game.Scene3D;
                    var terrain = scene.Terrain;
                    var tacticalView = scene.TacticalView;
                    var camera = scene.Camera;

                    Assert.NotNull(terrain);
                    Assert.True(terrain.StreamingEnabled, "4096 map must have streaming enabled.");

                    // Aim camera at target position and lock height above ground
                    var target = cameraTargets[frame];
                    tacticalView.LookAt(target);
                    tacticalView.SnapHeightAboveGround(600f);
                    tacticalView.Update(game.MapTime);

                    if (frame == 0)
                    {
                        // Verify potential level-0 patches calculation
                        const int tilesPerPatch = Panopticon.Terrain.Terrain.PatchSize - 1;
                        var patchesX = ((long)terrain.HeightMap.Width - 1 + tilesPerPatch - 1) / tilesPerPatch;
                        var patchesY = ((long)terrain.HeightMap.Height - 1 + tilesPerPatch - 1) / tilesPerPatch;
                        var potentialLevel0Patches = patchesX * patchesY;
                        Assert.True(potentialLevel0Patches > 1024, $"Expected potential level-0 patches > 1024, got {potentialLevel0Patches}.");

                        // Timing: measure first selection + update cost
                        var swFirst = Stopwatch.StartNew();
                        terrain.Update(scene.Waters, game.MapTime, game.EffectiveGraphicsSettings, camera);
                        swFirst.Stop();
                        firstUpdateMs = swFirst.Elapsed.TotalMilliseconds;

                        // Timing: repeat update with unchanged camera (cached path: provider cache hit)
                        var swCached = Stopwatch.StartNew();
                        terrain.Update(scene.Waters, game.MapTime, game.EffectiveGraphicsSettings, camera);
                        swCached.Stop();
                        cachedUpdateMs = swCached.Elapsed.TotalMilliseconds;

                        peakTotalMemory = GC.GetTotalMemory(false);
                        peakWorkingSet = Process.GetCurrentProcess().WorkingSet64;

                        // Assert cached update is not slower by an order of magnitude
                        Assert.True(cachedUpdateMs <= (firstUpdateMs * 10.0) + 1.0,
                            $"Cached update ({cachedUpdateMs:F3} ms) exceeded first update ({firstUpdateMs:F3} ms) by an order of magnitude.");
                    }
                    else
                    {
                        terrain.Update(scene.Waters, game.MapTime, game.EffectiveGraphicsSettings, camera);
                    }

                    var residentCount = terrain.StreamedPatchCount;
                    patchCounts[frame] = residentCount;

                    // Core streaming contract assertions
                    Assert.InRange(residentCount, 1, 4096);
                    Assert.Equal((long)residentCount * TerrainPatchStreamer.BytesPerResidentNode, terrain.StreamedVertexBytes);
                    Assert.True(terrain.StreamedVertexBytes <= 4096L * TerrainPatchStreamer.BytesPerResidentNode);

                    // Selection coverage / no-holes assertion:
                    // Compare StreamedPatchCount to the selection count computed by an independent CdlodQuadtree
                    // using CachedHeightRangeProvider over the same HeightMap and identical camera transform logic.
                    var testProvider = new CachedHeightRangeProvider(terrain.HeightMap);
                    var testQuadtree = new CdlodQuadtree(terrain.HeightMap.Width, terrain.HeightMap.Height, testProvider, 4096, 2f);
                    var border = (float)terrain.Map.HeightMapData.BorderWidth * HeightMap.HorizontalScale;
                    var cdlodCamera = new CdlodCamera(
                        camera.Position + new Vector3(border, border, 0),
                        new Panopticon.Mathematics.BoundingFrustum(Matrix4x4.CreateTranslation(-border, -border, 0) * camera.BoundingFrustum.Matrix),
                        0.5f * camera.ScreenSize.Y * camera.Projection.M22);
                    var expectedSelection = testQuadtree.Select(cdlodCamera);
                    Assert.Equal(expectedSelection.Nodes.Count, residentCount);

                    _output.WriteLine($"Frame {frame} (LookAt {target.X},{target.Y}): resident patches={residentCount}, vertex bytes={terrain.StreamedVertexBytes}, expected selection={expectedSelection.Nodes.Count}");
                }
            };

            var runResult = ScenarioCaptureRunner.Run(installation, profile, settings, new ConsoleTestHarnessReporter());
            Assert.Equal(0, runResult.ExitCode);

            // Verify bounded residency and release across disjoint moves:
            // Frame 0: count at (1000, 1000)
            // Frame 1: count at (2000, 2000)
            // Frame 2: count at (3000, 3000)
            // Frame 3: count returning to (1000, 1000)
            _output.WriteLine($"Patch counts across moves: [{string.Join(", ", patchCounts)}]");
            for (var i = 0; i < patchCounts.Length; i++)
            {
                Assert.InRange(patchCounts[i], 1, 4096);
            }

            // If patches were not released/disposed when exiting the frustum, the count would accumulate monotonically
            // across frames (summing past 300+ patches). Because each frustum selection is bounded to ~80-120 patches,
            // counts remaining bounded confirms entry/exit disposal.
            Assert.True(Math.Abs(patchCounts[0] - patchCounts[3]) <= 2,
                $"Expected return count at frame 3 ({patchCounts[3]}) to agree with initial count frame 0 ({patchCounts[0]}) within tolerance.");
            _output.WriteLine($"Entry/exit disposal verified: count at (1000,1000) frame 0 ({patchCounts[0]}) and return frame 3 ({patchCounts[3]}) remain bounded and stable; no accumulation across moves.");

            // Output timing and memory metrics
            _output.WriteLine($"Performance measurements:");
            _output.WriteLine($"  First selection+update: {firstUpdateMs:F3} ms");
            _output.WriteLine($"  Cached update:          {cachedUpdateMs:F3} ms");
            _output.WriteLine($"  GC Total Memory:        {peakTotalMemory / (1024 * 1024.0):F2} MB");
            _output.WriteLine($"  Process WorkingSet:     {peakWorkingSet / (1024 * 1024.0):F2} MB");

            // Frame readback and assertion: non-black and non-uniform
            var frameFiles = new string[profile.DurationFrames];
            for (var f = 0; f < profile.DurationFrames; f++)
            {
                var framePath = Path.Combine(logDir, $"frame_{f:D4}.png");
                Assert.True(File.Exists(framePath), $"Captured frame {framePath} does not exist.");
                frameFiles[f] = framePath;

                using var image = Image.Load<Rgba32>(framePath);
                Assert.True(image.Width > 0 && image.Height > 0);

                var distinctColors = new HashSet<Rgba32>();
                var sumR = 0.0;
                var sumG = 0.0;
                var sumB = 0.0;
                var sampledPixels = 0;

                for (var y = 0; y < image.Height; y += 8)
                {
                    for (var x = 0; x < image.Width; x += 8)
                    {
                        var pixel = image[x, y];
                        distinctColors.Add(pixel);
                        sumR += pixel.R;
                        sumG += pixel.G;
                        sumB += pixel.B;
                        sampledPixels++;
                    }
                }

                var meanR = sumR / sampledPixels;
                var meanG = sumG / sampledPixels;
                var meanB = sumB / sampledPixels;

                // Non-black assertion
                Assert.True(meanR > 0 || meanG > 0 || meanB > 0, $"Frame {f} is completely black!");

                // Non-uniform assertion: distinct colours must reflect terrain variation
                Assert.True(distinctColors.Count > 10, $"Frame {f} is uniform! Distinct sampled colours: {distinctColors.Count}");

                _output.WriteLine($"Frame {f} ({Path.GetFileName(framePath)}): size {image.Width}x{image.Height}, sampled {sampledPixels} pixels, distinct colours {distinctColors.Count}, mean RGB=({meanR:F1}, {meanG:F1}, {meanB:F1})");
            }

            // Assemble ordered frames into GIF
            var gifPath = Path.Combine(logDir, "streaming-4096-camera-sweep.gif");
            using (var gif = Image.Load<Rgba32>(frameFiles[0]))
            {
                for (var f = 1; f < frameFiles.Length; f++)
                {
                    using var nextFrame = Image.Load<Rgba32>(frameFiles[f]);
                    gif.Frames.AddFrame(nextFrame.Frames.RootFrame);
                }

                gif.SaveAsGif(gifPath);
            }

            Assert.True(File.Exists(gifPath), $"Animated GIF '{gifPath}' was not created.");
            var gifInfo = new FileInfo(gifPath);
            Assert.True(gifInfo.Length > 0, $"Animated GIF '{gifPath}' is empty.");
            _output.WriteLine($"Animated GIF assembled: {gifPath} ({gifInfo.Length / 1024} KB)");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static string CreatePlayable4096DirectoryPackage(string tempDir)
    {
        var packagePath = Path.Combine(tempDir, "streaming-4096-package");
        var heights = new ushort[LatticeSize, LatticeSize];
        for (var y = 0; y < LatticeSize; y++)
        {
            var fy = y * 0.02f;
            for (var x = 0; x < LatticeSize; x++)
            {
                var fx = x * 0.02f;
                var h = 2500f + 1200f * MathF.Sin(fx) * MathF.Cos(fy) + 400f * MathF.Sin(fx * 2.3f + fy * 1.7f);
                heights[x, y] = (ushort)Math.Clamp((int)h, 100, 60000);
            }
        }

        var mapFile = MapFile.CreateFromHeights(heights, borderWidth: BorderWidth, textureName: BlendTileData.DefaultTextureName);
        var interchange = MapPackageConverter.ToInterchange(mapFile);
        var writeResult = LegacyMapPackageImporter.Write(interchange, packagePath);
        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));
        return packagePath;
    }
}
