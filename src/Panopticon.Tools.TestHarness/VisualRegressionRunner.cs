using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-G3-03: runs the embedded visual regression scene set for both presets, captures every
/// scene at scale 1 with the fixed render clock, builds supersampled references (scale 2,
/// box-downsampled) for scenes with silhouette regions, scores the image metrics, and runs the
/// silhouette aliasing check. Capture-only: golden comparison and host gating land with
/// WI-G3-04. Exit codes: 0 pass, 1 setup failure, 5 silhouette check failure.
/// </summary>
public static class VisualRegressionRunner
{
    private static readonly (string Name, GraphicsSettings Settings)[] Presets =
    [
        ("faithful", GraphicsSettings.Faithful()),
        ("enhanced", GraphicsSettings.Enhanced()),
    ];

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        VisualRegressionSceneSet sceneSet;
        try
        {
            sceneSet = VisualRegressionSceneSetLoader.LoadEmbedded();
        }
        catch (Exception ex)
        {
            reporter.Error($"Could not load the visual regression scene set: {ex.Message}");
            return 1;
        }

        if (sceneSet.Scenes.Count == 0)
        {
            reporter.Error("The visual regression scene set is empty.");
            return 1;
        }

        var baseDirectory = Environment.GetEnvironmentVariable("TESTHARNESS_VR_OUTPUT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "panopticon_visual_regression");
        Directory.CreateDirectory(baseDirectory);

        var records = new List<SceneReportRecord>();
        var silhouetteFailure = false;

        foreach (var (presetName, preset) in Presets)
        {
            foreach (var scene in sceneSet.Scenes)
            {
                CaptureProfile profile;
                try
                {
                    profile = CaptureProfileLoader.LoadEmbeddedCapture(scene.Profile);
                }
                catch (Exception ex)
                {
                    reporter.Error($"Scene '{scene.Name}': could not load profile '{scene.Profile}': {ex.Message}");
                    return 1;
                }

                var scale1Directory = Path.Combine(baseDirectory, presetName, scene.Name, "scale1");
                var settings = new CaptureRunSettings(
                    config.Renderer,
                    scene.FullHud,
                    scale1Directory,
                    preset,
                    ViewportScale: 1,
                    FixedRenderClock: true);
                var result = ScenarioCaptureRunner.Run(installation, profile, settings, reporter);
                if (result.ExitCode != 0)
                {
                    reporter.Error($"Scene '{scene.Name}' ({presetName}) capture failed with exit code {result.ExitCode}.");
                    return 1;
                }

                var capturePath = Path.Combine(scale1Directory, $"frame_{scene.CaptureFrame:D4}.png");
                if (!File.Exists(capturePath))
                {
                    reporter.Error($"Scene '{scene.Name}' ({presetName}): expected capture '{capturePath}' is missing.");
                    return 1;
                }

                var capture = LoadPngAsFrame(capturePath);
                var scores = ScoreRegions(capture, scene, capture.Width, capture.Height);

                double? referenceEdgeAliasing = null;
                var silhouetteImproved = (bool?)null;
                if (scene.SilhouetteRegions.Count > 0)
                {
                    var referenceScores = CaptureAndScoreReference(
                        installation, config, scene, profile, preset, presetName, scale1Directory, capture.Width, capture.Height, reporter);
                    if (referenceScores == null)
                    {
                        return 1;
                    }

                    referenceEdgeAliasing = referenceScores.EdgeAliasing;

                    // A near-oracle supersampled render must alias less than the direct capture
                    // over the designated silhouette regions, or the check cannot discriminate
                    // antialiasing improvements later.
                    silhouetteImproved = referenceScores.EdgeAliasing < scores.EdgeAliasing;
                    if (silhouetteImproved != true)
                    {
                        silhouetteFailure = true;
                        reporter.Error(
                            $"Scene '{scene.Name}' ({presetName}): supersampled reference edge aliasing " +
                            $"{referenceScores.EdgeAliasing:0.000} is not lower than the capture's {scores.EdgeAliasing:0.000}.");
                    }
                }

                records.Add(new SceneReportRecord(presetName, scene.Name, capturePath)
                {
                    Sharpness = scores.Sharpness,
                    EdgeAliasing = scores.EdgeAliasing,
                    ReferenceEdgeAliasing = referenceEdgeAliasing,
                    SilhouetteImproved = silhouetteImproved,
                });

                reporter.Info(
                    $"Scene '{scene.Name}' ({presetName}): sharpness {scores.Sharpness:0.000}, edge aliasing {scores.EdgeAliasing:0.000}.");
            }
        }

        var report = new VisualRegressionReport(
            SchemaVersion: 1,
            Renderer: config.Renderer?.ToString() ?? "Default",
            Presets: [.. Presets.Select(p => p.Name)],
            Records: records);
        var reportPath = Path.Combine(baseDirectory, "report.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, ReportJsonOptions));
        reporter.Info($"Visual regression report written to {reportPath}.");

        return silhouetteFailure ? 5 : 0;
    }

    private static ImageQualityScores? CaptureAndScoreReference(
        GameInstallation installation,
        TestHarnessConfig config,
        VisualRegressionScene scene,
        CaptureProfile profile,
        GraphicsSettings preset,
        string presetName,
        string scale1Directory,
        int captureWidth,
        int captureHeight,
        ITestHarnessReporter reporter)
    {
        var scale2Directory = Path.Combine(Path.GetDirectoryName(scale1Directory)!, "scale2");
        var settings = new CaptureRunSettings(
            config.Renderer,
            scene.FullHud,
            scale2Directory,
            preset,
            ViewportScale: 2,
            FixedRenderClock: true);
        var result = ScenarioCaptureRunner.Run(installation, profile, settings, reporter);
        if (result.ExitCode != 0)
        {
            reporter.Error($"Scene '{scene.Name}' ({presetName}) scale-2 reference capture failed with exit code {result.ExitCode}.");
            return null;
        }

        var referencePath = Path.Combine(scale2Directory, $"frame_{scene.CaptureFrame:D4}.png");
        if (!File.Exists(referencePath))
        {
            reporter.Error($"Scene '{scene.Name}' ({presetName}): scale-2 reference capture '{referencePath}' is missing.");
            return null;
        }

        var reference = FrameDownsampler.Downsample2x2(LoadPngAsFrame(referencePath));
        if (reference.Width != captureWidth || reference.Height != captureHeight)
        {
            reporter.Error(
                $"Scene '{scene.Name}' ({presetName}): downsampled reference is {reference.Width}x{reference.Height}, capture is {captureWidth}x{captureHeight}.");
            return null;
        }

        using var image = FrameCapturer.ToImage(reference);
        image.SaveAsPng(Path.Combine(scale2Directory, "reference_downsampled.png"));

        return ScoreRegions(reference, scene, reference.Width, reference.Height);
    }

    /// <summary>Scores the silhouette regions (mean over regions); the whole frame when none.</summary>
    private static ImageQualityScores ScoreRegions(CapturedFrame frame, VisualRegressionScene scene, int width, int height)
    {
        if (scene.SilhouetteRegions.Count == 0)
        {
            return ImageQualityMetrics.Score(frame);
        }

        var sharpness = 0.0;
        var edgeAliasing = 0.0;
        foreach (var region in scene.SilhouetteRegions)
        {
            var rect = new Panopticon.Mathematics.Rectangle(
                (int)(region.X * width),
                (int)(region.Y * height),
                (int)(region.Width * width),
                (int)(region.Height * height));
            var scores = ImageQualityMetrics.Score(frame, rect);
            sharpness += scores.Sharpness;
            edgeAliasing += scores.EdgeAliasing;
        }

        return new ImageQualityScores
        {
            Sharpness = sharpness / scene.SilhouetteRegions.Count,
            EdgeAliasing = edgeAliasing / scene.SilhouetteRegions.Count,
        };
    }

    private static CapturedFrame LoadPngAsFrame(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        var pixels = new byte[image.Width * image.Height * 4];
        for (var y = 0; y < image.Height; y++)
        {
            var row = image.Frames[0].PixelBuffer.DangerousGetRowSpan(y);
            for (var x = 0; x < image.Width; x++)
            {
                var i = (y * image.Width + x) * 4;
                pixels[i] = row[x].R;
                pixels[i + 1] = row[x].G;
                pixels[i + 2] = row[x].B;
                pixels[i + 3] = row[x].A;
            }
        }

        return new CapturedFrame(pixels, image.Width, image.Height, PixelFormat.R8_G8_B8_A8_UNorm);
    }
}

public sealed record VisualRegressionReport(
    int SchemaVersion,
    string Renderer,
    IReadOnlyList<string> Presets,
    IReadOnlyList<SceneReportRecord> Records);

public sealed record SceneReportRecord(
    string Preset,
    string Scene,
    string Capture)
{
    public double? Sharpness { get; init; }

    public double? EdgeAliasing { get; init; }

    public double? ReferenceEdgeAliasing { get; init; }

    public bool? SilhouetteImproved { get; init; }
}
