using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;
using HudChromeAudit = Panopticon.Gui.Wnd.Images.HudChromeAudit;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Runs the embedded visual regression scene set for both presets, captures every scene at
/// scale 1 with the fixed render clock, builds supersampled references (scale 2,
/// box-downsampled) for scenes with silhouette regions, scores the image metrics, runs the
/// silhouette aliasing check, and gates each capture against the golden store. Goldens are
/// pinned to the reference host. They live outside the repository (renders of install content,
/// see the licensing note in the G3 implementation plan) and are described by a checked-in
/// manifest carrying a SHA-256 per golden. Exit codes: 0 pass, 1 setup error (missing manifest
/// or golden, store drift), 2 regression, 5 silhouette check failure, 6 golden host mismatch.
/// TESTHARNESS_VR_UPDATE_GOLDENS=1 writes the store and a new manifest instead of comparing;
/// the manifest is written only when the whole run passed.
/// </summary>
public static class VisualRegressionRunner
{
    private static readonly (string Name, GraphicsSettings Settings)[] Presets =
    [
        // The label stays "faithful": it keys the golden store, manifest and reports, which
        // predate the Faithful -> Legacy rename.
        ("faithful", GraphicsSettings.Legacy()),
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

        var updateMode = Environment.GetEnvironmentVariable("TESTHARNESS_VR_UPDATE_GOLDENS") == "1";

        string backend;
        string deviceName;
        try
        {
            using var probe = GraphicsDeviceUtility.CreateGraphicsDevice(config.Renderer, null);
            backend = probe.BackendType.ToString();
            deviceName = probe.DeviceName;
        }
        catch (Exception ex)
        {
            reporter.Error($"Could not resolve the graphics backend and device: {ex.Message}");
            return 1;
        }

        var host = Environment.MachineName;

        var baseDirectory = Environment.GetEnvironmentVariable("TESTHARNESS_VR_OUTPUT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "panopticon_visual_regression");
        Directory.CreateDirectory(baseDirectory);

        var storeRoot = Environment.GetEnvironmentVariable("TESTHARNESS_VR_GOLDEN_STORE")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".panopticon",
                "visual-regression-goldens");
        var manifestPath = Environment.GetEnvironmentVariable("TESTHARNESS_VR_MANIFEST")
            ?? Path.Combine("visual-regression", "golden-manifest.json");

        GoldenManifest? manifest = null;
        if (!updateMode)
        {
            if (!File.Exists(manifestPath))
            {
                reporter.Error(
                    $"Golden manifest '{manifestPath}' not found; capture goldens once on the reference host with TESTHARNESS_VR_UPDATE_GOLDENS=1.");
                return 1;
            }

            try
            {
                manifest = JsonSerializer.Deserialize<GoldenManifest>(File.ReadAllText(manifestPath), ReportJsonOptions);
            }
            catch (Exception ex)
            {
                reporter.Error($"Could not read the golden manifest '{manifestPath}': {ex.Message}");
                return 1;
            }

            if (manifest == null || manifest.Goldens.Count == 0)
            {
                reporter.Error($"Golden manifest '{manifestPath}' contains no goldens.");
                return 1;
            }
        }

        var records = new List<SceneReportRecord>();
        var goldenEntries = new List<GoldenManifestEntry>();
        var silhouetteFailure = false;
        var chromeAuditFailure = false;
        var regression = false;
        var hostMismatch = false;
        var manifestWidth = 0;
        var manifestHeight = 0;

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

                var scale1Directory = Path.Combine(baseDirectory, presetName, scene.Name, $"scale{scene.ViewportScale}");
                var settings = new CaptureRunSettings(
                    config.Renderer,
                    scene.FullHud,
                    scale1Directory,
                    preset,
                    ViewportScale: scene.ViewportScale,
                    FixedRenderClock: true);
                HudChromeAudit.Reset();
                var result = ScenarioCaptureRunner.Run(installation, profile, settings, reporter);
                if (result.ExitCode != 0)
                {
                    reporter.Error($"Scene '{scene.Name}' ({presetName}) capture failed with exit code {result.ExitCode}.");
                    return 1;
                }

                if (scene.FullHud && preset.UiMode == UiMode.DynamicHud)
                {
                    // WI-G9-03 chrome audit: dynamic HUD scenes must not sample legacy chrome
                    // bitmaps, and the procedural path must have engaged at all.
                    if (HudChromeAudit.ChromeBitmapDraws > 0)
                    {
                        chromeAuditFailure = true;
                        reporter.Error(
                            $"Scene '{scene.Name}' ({presetName}): dynamic HUD drew {HudChromeAudit.ChromeBitmapDraws} " +
                            "chrome-tagged legacy bitmap(s); expected procedural chrome only.");
                    }
                    else if (HudChromeAudit.DynamicChromePaints == 0)
                    {
                        chromeAuditFailure = true;
                        reporter.Error(
                            $"Scene '{scene.Name}' ({presetName}): dynamic HUD engaged zero procedural chrome paints; " +
                            "the dynamic chrome path did not run.");
                    }
                }

                var capturePath = Path.Combine(scale1Directory, $"frame_{scene.CaptureFrame:D4}.png");
                if (!File.Exists(capturePath))
                {
                    reporter.Error($"Scene '{scene.Name}' ({presetName}): expected capture '{capturePath}' is missing.");
                    return 1;
                }

                var capture = LoadPngAsFrame(capturePath);
                var scores = ScoreRegions(capture, scene, capture.Width, capture.Height);

                if (manifestWidth == 0 && scene.ViewportScale == 1)
                {
                    manifestWidth = capture.Width;
                    manifestHeight = capture.Height;
                }
                else if (!updateMode && scene.ViewportScale == 1 && (capture.Width != manifest!.Width || capture.Height != manifest.Height))
                {
                    reporter.Error(
                        $"Capture is {capture.Width}x{capture.Height} but the goldens are {manifest.Width}x{manifest.Height}; " +
                        "the harness viewport changed, recapture goldens through update mode.");
                    return 1;
                }

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

                var setupFailure = false;
                var goldenOutcome = ProcessGolden(
                    updateMode, manifest, storeRoot, manifestPath, presetName, scene,
                    capturePath, capture, scores, backend, deviceName, host,
                    goldenEntries, reporter,
                    ref hostMismatch,
                    ref regression,
                    ref setupFailure);

                records.Add(new SceneReportRecord(presetName, scene.Name, capturePath)
                {
                    Sharpness = scores.Sharpness,
                    EdgeAliasing = scores.EdgeAliasing,
                    ReferenceEdgeAliasing = referenceEdgeAliasing,
                    SilhouetteImproved = silhouetteImproved,
                    GoldenStatus = goldenOutcome.Status,
                    Golden = goldenOutcome.GoldenPath,
                    DifferingPixels = goldenOutcome.DifferingPixels,
                    DifferingPixelFraction = goldenOutcome.DifferingPixelFraction,
                    MaxChannelDeltaObserved = goldenOutcome.MaxChannelDeltaObserved,
                    SharpnessDelta = goldenOutcome.SharpnessDelta,
                    EdgeAliasingDelta = goldenOutcome.EdgeAliasingDelta,
                    FailureReason = goldenOutcome.FailureReason,
                });

                if (setupFailure)
                {
                    return 1;
                }

                reporter.Info(
                    $"Scene '{scene.Name}' ({presetName}): sharpness {scores.Sharpness:0.000}, edge aliasing {scores.EdgeAliasing:0.000}, golden {goldenOutcome.Status}.");
            }
        }

        var report = new VisualRegressionReport(
            SchemaVersion: 1,
            Renderer: config.Renderer?.ToString() ?? "Default",
            Presets: [.. Presets.Select(p => p.Name)],
            Records: records)
        {
            GoldenManifestPath = Path.GetFullPath(manifestPath),
            GoldenStore = storeRoot,
            Backend = backend,
            DeviceName = deviceName,
            Host = host,
        };
        var reportPath = Path.Combine(baseDirectory, "report.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, ReportJsonOptions));
        reporter.Info($"Visual regression report written to {reportPath}.");

        if (updateMode)
        {
            if (silhouetteFailure || chromeAuditFailure)
            {
                reporter.Error("Golden manifest not written: the update run reported a silhouette or chrome audit failure.");
                return 5;
            }

            var manifestRecord = new GoldenManifest(
                SchemaVersion: 1,
                StoreKind: "external",
                Backend: backend,
                DeviceName: deviceName,
                Host: host,
                Width: manifestWidth,
                Height: manifestHeight,
                UpdatedUtc: DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                Goldens: goldenEntries);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestRecord, ReportJsonOptions));
            reporter.Info($"Golden manifest written to {Path.GetFullPath(manifestPath)} ({goldenEntries.Count} goldens).");
            return 0;
        }

        if (regression || chromeAuditFailure)
        {
            return 2;
        }

        if (hostMismatch)
        {
            return 6;
        }

        return silhouetteFailure ? 5 : 0;
    }

    private sealed record GoldenOutcome(
        string Status,
        string? GoldenPath = null,
        long? DifferingPixels = null,
        double? DifferingPixelFraction = null,
        int? MaxChannelDeltaObserved = null,
        double? SharpnessDelta = null,
        double? EdgeAliasingDelta = null,
        string? FailureReason = null);

    private static GoldenOutcome ProcessGolden(
        bool updateMode,
        GoldenManifest? manifest,
        string storeRoot,
        string manifestPath,
        string presetName,
        VisualRegressionScene scene,
        string capturePath,
        CapturedFrame capture,
        ImageQualityScores scores,
        string backend,
        string deviceName,
        string host,
        List<GoldenManifestEntry> goldenEntries,
        ITestHarnessReporter reporter,
        ref bool hostMismatch,
        ref bool regression,
        ref bool setupFailure)
    {
        var defaults = new GoldenTolerance();
        var tolerance = new GoldenTolerance
        {
            MaxChannelDelta = scene.MaxChannelDelta ?? defaults.MaxChannelDelta,
            MaxDifferingPixelFraction = scene.MaxDifferingPixelFraction ?? defaults.MaxDifferingPixelFraction,
            MaxMetricDelta = scene.MaxMetricDelta ?? defaults.MaxMetricDelta,
        };

        var goldenPath = Path.Combine(storeRoot, presetName, $"{scene.Name}.png");

        if (updateMode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.Copy(capturePath, goldenPath, overwrite: true);
            goldenEntries.Add(new GoldenManifestEntry(
                presetName,
                scene.Name,
                $"{presetName}/{scene.Name}.png",
                Sha256File(goldenPath),
                scores.Sharpness,
                scores.EdgeAliasing,
                tolerance.MaxChannelDelta,
                tolerance.MaxDifferingPixelFraction,
                tolerance.MaxMetricDelta));
            return new GoldenOutcome("Updated", goldenPath);
        }

        if (manifest!.Backend != backend || manifest.DeviceName != deviceName || manifest.Host != host)
        {
            hostMismatch = true;
            reporter.Error(
                $"Golden host mismatch: goldens were captured on {manifest.Backend}/{manifest.DeviceName}/{manifest.Host}, " +
                $"this run is {backend}/{deviceName}/{host}. Captures and scores are still written; pixel comparison is skipped.");
            return new GoldenOutcome("HostMismatch");
        }

        var entry = manifest.Goldens.FirstOrDefault(g => g.Preset == presetName && g.Scene == scene.Name);
        if (entry == null)
        {
            reporter.Error(
                $"Golden manifest '{manifestPath}' has no entry for scene '{scene.Name}' preset '{presetName}'; " +
                "recapture through update mode.");
            setupFailure = true;
            return new GoldenOutcome("MissingGolden");
        }

        if (!File.Exists(goldenPath))
        {
            reporter.Error($"Golden '{goldenPath}' is missing; recapture the store through update mode.");
            setupFailure = true;
            return new GoldenOutcome("MissingGolden");
        }

        if (Sha256File(goldenPath) != entry.Sha256)
        {
            reporter.Error($"Golden '{goldenPath}' does not match the SHA-256 recorded in the manifest; the store drifted.");
            setupFailure = true;
            return new GoldenOutcome("StoreMismatch");
        }

        var goldenFrame = LoadPngAsFrame(goldenPath);
        var goldenScores = new ImageQualityScores
        {
            Sharpness = entry.Sharpness,
            EdgeAliasing = entry.EdgeAliasing,
        };
        var comparison = GoldenFrameComparer.Compare(capture, goldenFrame, scores, goldenScores, tolerance);
        if (!comparison.Passed)
        {
            regression = true;
            reporter.Error($"Scene '{scene.Name}' ({presetName}) golden regression: {comparison.FailureReason}");
        }

        return new GoldenOutcome(
            comparison.Passed ? "Passed" : "Failed",
            goldenPath,
            comparison.DifferingPixels,
            comparison.DifferingPixelFraction,
            comparison.MaxChannelDeltaObserved,
            comparison.SharpnessDelta,
            comparison.EdgeAliasingDelta,
            comparison.FailureReason);
    }

    private static string Sha256File(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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
    IReadOnlyList<SceneReportRecord> Records)
{
    public string? GoldenManifestPath { get; init; }

    public string? GoldenStore { get; init; }

    public string? Backend { get; init; }

    public string? DeviceName { get; init; }

    public string? Host { get; init; }
}

public sealed record SceneReportRecord(
    string Preset,
    string Scene,
    string Capture)
{
    public double? Sharpness { get; init; }

    public double? EdgeAliasing { get; init; }

    public double? ReferenceEdgeAliasing { get; init; }

    public bool? SilhouetteImproved { get; init; }

    public string? GoldenStatus { get; init; }

    public string? Golden { get; init; }

    public long? DifferingPixels { get; init; }

    public double? DifferingPixelFraction { get; init; }

    public int? MaxChannelDeltaObserved { get; init; }

    public double? SharpnessDelta { get; init; }

    public double? EdgeAliasingDelta { get; init; }

    public string? FailureReason { get; init; }
}

/// <summary>
/// Checked-in description of the external golden store: one entry per preset and scene, the
/// reference host the goldens were captured on, and a SHA-256 per golden so a drifted store is
/// detected instead of silently compared.
/// </summary>
public sealed record GoldenManifest(
    int SchemaVersion,
    string StoreKind,
    string Backend,
    string DeviceName,
    string Host,
    int Width,
    int Height,
    string UpdatedUtc,
    IReadOnlyList<GoldenManifestEntry> Goldens);

public sealed record GoldenManifestEntry(
    string Preset,
    string Scene,
    string File,
    string Sha256,
    double Sharpness,
    double EdgeAliasing,
    int MaxChannelDelta,
    double MaxDifferingPixelFraction,
    double MaxMetricDelta);
