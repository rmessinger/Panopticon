using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Tools.TestHarness;

public static class TerrainZoomProofRunner
{
    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var root = Environment.GetEnvironmentVariable("TESTHARNESS_TERRAIN_ZOOM_OUTPUT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "panopticon_terrain_zoom");
        var output = Path.Combine(root, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        reporter.Info($"Terrain zoom evidence: {output}");

        try
        {
            var profileName = config.ScenarioPath ?? "vr-terrain-grazing.json";
            var profile = File.Exists(profileName)
                ? CaptureProfileLoader.LoadCaptureFromFile(profileName)
                : CaptureProfileLoader.LoadEmbeddedCapture(profileName);
            if (profile.Camera.Mode != CaptureCameraMode.Explicit || profile.Camera.LookAt == null
                || profile.Actors.Count > 0 || profile.Commands.Count > 0 || profile.Expectations.Count > 0)
            {
                reporter.Error("TerrainZoomProof requires an explicit-camera terrain profile without actors, commands or expectations.");
                return 1;
            }

            profile.DurationFrames = TerrainZoomPath.FrameCount;
            profile.CaptureStride = 1;
            profile.HideHud = true;
            profile.OutputDirectory = null;
            profile.Camera.Height = TerrainZoomPath.NearHeight;
            var measurementRegion = profile.Name == "vr-terrain-default"
                ? new MeasurementRegion(0.4, 0.82, 0.25, 0.14)
                : new MeasurementRegion(0.4, 0.7, 0.25, 0.18);
            var enhanced = GraphicsSettings.Enhanced();
            var control = GraphicsSettings.Create(enhanced.SampleCount, enhanced.AlphaTestMode, enhanced.ColourSpaceMode,
                4, enhanced.MipLodBias, enhanced.ShadowTier, enhanced.UiMode, enhanced.RealisticProportions);
            var captures = new List<CaptureEvidence>();
            foreach (var (name, settings, scale) in new[]
            {
                ("control-4x", control, 1),
                ("baseline-16x", enhanced.WithPrefilteredTerrainTextures(false), 1),
                ("enhanced-16x", enhanced, 1),
                ("reference-2x", enhanced.WithPrefilteredTerrainTextures(false), 2),
            })
            {
                var directory = Path.Combine(output, name);
                CaptureEvidence? evidence = null;
                var run = ScenarioCaptureRunner.Run(installation, profile,
                    new CaptureRunSettings(config.Renderer, false, directory, settings,
                        ViewportScale: scale, FixedRenderClock: true, FreezeLogicDuringCapture: true,
                        BeforeRender: (game, frame) =>
                        {
                            var lookAt = profile.Camera.LookAt!;
                            game.Scene3D.TacticalView.SnapHeightAboveGround(TerrainZoomPath.HeightAt(frame));
                            game.Scene3D.TacticalView.LookAt(new Vector3(lookAt.X, lookAt.Y, 0));
                            evidence ??= new CaptureEvidence(name, game.GraphicsDevice.BackendType.ToString(),
                                game.GraphicsDevice.DeviceName, game.EffectiveGraphicsSettings.Anisotropy,
                                game.EffectiveGraphicsSettings.SampleCount, game.EffectiveGraphicsSettings.PrefilteredTerrainTextures,
                                game.Scene3D.Terrain.PrefilteredTextureCount, game.Scene3D.Terrain.MissingPrefilteredTextureCount);
                        }), reporter);
                if (run.ExitCode != 0 || run.CapturedFrames != TerrainZoomPath.FrameCount || evidence == null)
                {
                    reporter.Error($"Terrain zoom capture '{name}' failed; artifacts preserved in {output}.");
                    return 1;
                }

                captures.Add(evidence);
            }

            var scores = Score(output, measurementRegion);
            var controlTemporal = scores.Average(s => s.ControlTemporal);
            var enhancedTemporal = scores.Average(s => s.EnhancedTemporal);
            var baselineTemporal = scores.Average(s => s.BaselineTemporal);
            // Relative regression tolerance plus a small absolute floor for near-zero error.
            var limit = baselineTemporal * 1.05 + 0.00001;
            var comparable = captures[0].Anisotropy == 4 && captures.Skip(1).All(c => c.Anisotropy == 16)
                && captures[2].PrefilteredTextureCount > 0 && captures[2].MissingPrefilteredTextureCount == 0;
            var passed = comparable && enhancedTemporal <= limit;
            var report = new
            {
                SchemaVersion = 3,
                Profile = profile.Name,
                profile.MapPath,
                profile.Camera,
                Metric = "RMS change in native-minus-reference luminance error / 2; screen-space proxy, not world reprojection",
                Region = measurementRegion,
                Heights = Enumerable.Range(0, TerrainZoomPath.FrameCount).Select(TerrainZoomPath.HeightAt).ToArray(),
                Captures = captures,
                ControlTemporal = controlTemporal,
                EnhancedTemporal = enhancedTemporal,
                BaselineTemporal = baselineTemporal,
                TemporalLimit = limit,
                Comparable = comparable,
                Passed = passed,
                Pairs = scores,
            };
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "validation_guide.md"),
                "# Terrain zoom validation\n\n" +
                "Sequences: original-data control-4x and baseline-16x, baked-data enhanced-16x, reference-downsampled. All use single-sample texturing. Frames 0000..0032 zoom out and back.\n" +
                "Inspect every matched frame and the central-lower measurement region for ground detail, tile/cliff seams, and non-terrain content.\n" +
                "Water, foliage, roads or moving shadows in the region invalidate a terrain-only interpretation.\n" +
                "Compare sequences in motion for crawling texture detail. Sharpness is reported per pair to expose blur tradeoffs.\n" +
                "Passing requires complete baked-data coverage and no more than 5 percent relative temporal-error increase plus 0.00001 over the original-data 16x baseline, not zero shimmer.\n" +
                "The reference uses a 2x viewport with 2x2 box downsampling; it is not a perfect ground truth. No goldens are updated.\n");
            reporter.Info($"Temporal texture error: 4x={controlTemporal:F6}, baseline16x={baselineTemporal:F6}, filtered16x={enhancedTemporal:F6}, limit={limit:F6}. Report: {Path.Combine(output, "report.json")}");
            if (!passed)
            {
                reporter.Error(comparable ? "Terrain temporal error worsened against the original-data 16x baseline."
                    : "Device fallback or missing offline terrain data prevents the comparison. Run TerrainTextureBake first.");
            }

            return !comparable ? 1 : passed ? 0 : 2;
        }
        catch (Exception ex)
        {
            reporter.Error($"Terrain zoom proof failed: {ex.Message}. Artifacts preserved in {output}.");
            return 1;
        }
    }

    private static List<PairScore> Score(string output, MeasurementRegion measurementRegion)
    {
        var scores = new List<PairScore>();
        CapturedFrame? previousControl = null;
        CapturedFrame? previousEnhanced = null;
        CapturedFrame? previousBaseline = null;
        CapturedFrame? previousReference = null;
        var referenceDirectory = Path.Combine(output, "reference-downsampled");
        Directory.CreateDirectory(referenceDirectory);
        for (var frame = 0; frame < TerrainZoomPath.FrameCount; frame++)
        {
            var file = $"frame_{frame:D4}.png";
            var control = Load(Path.Combine(output, "control-4x", file));
            var enhanced = Load(Path.Combine(output, "enhanced-16x", file));
            var baseline = Load(Path.Combine(output, "baseline-16x", file));
            var reference = FrameDownsampler.Downsample2x2(Load(Path.Combine(output, "reference-2x", file)));
            using var image = FrameCapturer.ToImage(reference);
            image.SaveAsPng(Path.Combine(referenceDirectory, file));
            var region = new Rectangle((int)(control.Width * measurementRegion.X), (int)(control.Height * measurementRegion.Y),
                (int)(control.Width * measurementRegion.Width), (int)(control.Height * measurementRegion.Height));
            if (previousControl != null)
            {
                scores.Add(new PairScore(frame,
                    TemporalImageQualityMetrics.Score(previousControl, control, previousReference!, reference, region),
                    TemporalImageQualityMetrics.Score(previousEnhanced!, enhanced, previousReference!, reference, region),
                    ImageQualityMetrics.Score(control, region).Sharpness,
                    ImageQualityMetrics.Score(enhanced, region).Sharpness,
                    ImageQualityMetrics.Score(reference, region).Sharpness,
                    TemporalImageQualityMetrics.Score(previousBaseline!, baseline, previousReference!, reference, region),
                    ImageQualityMetrics.Score(baseline, region).Sharpness));
            }

            previousControl = control;
            previousEnhanced = enhanced;
            previousBaseline = baseline;
            previousReference = reference;
        }

        return scores;
    }

    private static CapturedFrame Load(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return new CapturedFrame(pixels, image.Width, image.Height, PixelFormat.R8_G8_B8_A8_UNorm);
    }

    private sealed record CaptureEvidence(string Name, string Backend, string Device, int Anisotropy, int SampleCount,
        bool PrefilteredTerrainTextures, int PrefilteredTextureCount, int MissingPrefilteredTextureCount);
    private sealed record MeasurementRegion(double X, double Y, double Width, double Height);
    private sealed record PairScore(int Frame, double ControlTemporal, double EnhancedTemporal,
        double ControlSharpness, double EnhancedSharpness, double ReferenceSharpness,
        double BaselineTemporal, double BaselineSharpness);
}
