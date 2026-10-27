using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Network;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// B5 render-scale probe: spawns a large repeated-model field, frames it with a fixed
/// camera, then measures per-frame wall time of the logic update, the render submit
/// (Game.Render), and the GPU drain (WaitForIdle) over a fixed frame count, headlessly on
/// an offscreen device. Render-path spans and statistics come from RenderPipeline (B5-01);
/// the record uses the B1 writers so A2/A5 tooling consumes it unchanged. Idle units only:
/// this measures presentation, not combat.
/// </summary>
public static class RenderingScaleProbeRunner
{
    private const int SettleFrames = 10;

    private const uint ProbeSeed = 260501;

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var unitCount = config.RenderingScaleProbeUnitCount ?? 10000;
        var definition = string.IsNullOrWhiteSpace(config.RenderingScaleProbeDefinition)
            ? "AmericaTankCrusader"
            : config.RenderingScaleProbeDefinition!;
        var warmupFrames = config.RenderingScaleProbeWarmupFrames ?? 30;
        var measuredFrames = config.RenderingScaleProbeMeasuredFrames ?? 120;
        var mapPath = string.IsNullOrWhiteSpace(config.RenderingScaleProbeMapPath)
            ? "maps/deepdivide/deepdivide.map"
            : config.RenderingScaleProbeMapPath!;

        // Spawning thousands of units drowns stdout in Info lines; measure quietly.
        var loggingConfig = NLog.LogManager.Configuration;
        if (loggingConfig != null)
        {
            foreach (var rule in loggingConfig.LoggingRules)
            {
                rule.DisableLoggingForLevel(NLog.LogLevel.Info);
            }

            NLog.LogManager.ReconfigExistingLoggers();
        }

        Game? game = null;
        try
        {
            game = new Game(
                installation,
                config.Renderer,
                new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud },
                null,
                GraphicsSettings.Enhanced());
            game.StartSkirmishOrMultiPlayerGame(
                mapPath,
                new EchoConnection(),
                CreatePlayerSettings(game),
                (int)ProbeSeed,
                isMultiPlayer: false);
            game.StartRun();
            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
            foreach (var player in game.Scene3D.Players)
            {
                player.DisableAI();
            }

            game.EnableFixedLogicTimestep();
            game.EnableFixedRenderTimestep();

            if (config.RenderingScaleProbeLegacyShadows)
            {
                // B5-4 A/B: single-cascade 1024 Original tier instead of Enhanced's
                // four-cascade 2048, applied at the frame boundary like a preset swap.
                game.GraphicsSettings = GraphicsSettings.Create(
                    4,
                    AlphaTestMode.CoveragePreserving,
                    ColourSpaceMode.Linear,
                    16,
                    0f,
                    ShadowTier.Original,
                    UiMode.DynamicHud,
                    realisticProportions: false,
                    prefilteredTerrainTextures: game.GraphicsSettings.PrefilteredTerrainTextures,
                    terrainDecalLimit: game.GraphicsSettings.TerrainDecalLimit,
                    terrainDecalLighting: game.GraphicsSettings.TerrainDecalLighting);
            }

            var spawned = SpawnField(game, definition, unitCount, reporter);
            if (spawned == 0)
            {
                reporter.Error($"No units spawned; definition '{definition}' resolved to nothing.");
                return 1;
            }

            // Size the panel before any frame: the constructor's 1x1 fallback would make
            // every mesh sub-pixel for the LOD gate and mis-shape the camera frustum.
            game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, 1280, 800));

            FrameCamera(game, unitCount);

            var record = Measure(game, definition, unitCount, warmupFrames, measuredFrames);
            var statistics = game.Graphics.RenderStatistics;
            reporter.Info($"Render statistics (last measured frame): {statistics}");
            reporter.Info(Summarize(record));

            var reportDirectory = string.IsNullOrWhiteSpace(config.RenderingScaleProbeReportDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "rendering-scale-probe")
                : config.RenderingScaleProbeReportDirectory!;
            Directory.CreateDirectory(reportDirectory);
            var jsonPath = Path.Combine(reportDirectory, ProbeFileName(unitCount, definition, "json"));
            var csvPath = Path.ChangeExtension(jsonPath, ".csv");
            BenchmarkReportWriter.WriteJson(record, jsonPath);
            BenchmarkReportWriter.WriteCsv(record, csvPath);
            reporter.Info($"Render-scale probe JSON saved to: {jsonPath}");
            reporter.Info($"Render-scale probe CSV saved to: {csvPath}");

            if (!string.IsNullOrEmpty(config.RenderingScaleProbeScreenshotPath))
            {
                CaptureScreenshot(game, config.RenderingScaleProbeScreenshotPath!, reporter);
            }

            return 0;
        }
        finally
        {
            game?.Dispose();
        }
    }

    private static PlayerSetting[] CreatePlayerSettings(Game game)
    {
        // Passive owners (no human, no AI) on the first two playable sides, matching the B1
        // benchmark scenarios; the field owner is Players[2], the first map player slot.
        var sides = game.GetPlayableSides().ToArray();
        return
        [
            new PlayerSetting(
                0,
                sides[0].Name,
                game.AssetStore.MultiplayerColors.GetByIndex(0).RgbColor,
                0,
                PlayerOwner.None,
                "probe-a"),
            new PlayerSetting(
                1,
                sides[sides.Length > 1 ? 1 : 0].Name,
                game.AssetStore.MultiplayerColors.GetByIndex(1).RgbColor,
                1,
                PlayerOwner.None,
                "probe-b"),
        ];
    }

    private static int SpawnField(Game game, string definition, int unitCount, ITestHarnessReporter reporter)
    {
        const float Spacing = 24f;
        var columns = (int)Math.Ceiling(Math.Sqrt(unitCount));
        var owner = game.Scene3D.Players[2];
        var (originX, originY) = BenchmarkUnitSpawner.OriginInUsableArea(game, 0.35f, 0.35f);
        var fieldDefinition = game.AssetStore.ObjectDefinitions.GetByName(definition);
        if (fieldDefinition == null)
        {
            reporter.Error($"Definition '{definition}' not found in the asset store.");
            return 0;
        }

        var spawned = 0;
        for (var i = 0; i < unitCount; i++)
        {
            var (x, y) = BenchmarkUnitSpawner.GridPosition(i, columns, originX, originY, Spacing);
            if (BenchmarkUnitSpawner.SpawnAt(game, definition, owner, x, y) is not null)
            {
                spawned++;
            }
        }

        reporter.Info($"Spawned {spawned}/{unitCount} '{definition}' in a {columns}-column grid at spacing {Spacing}.");
        return spawned;
    }

    private static void FrameCamera(Game game, int unitCount)
    {
        var heightMap = game.Scene3D.Terrain.HeightMap;
        var span = MathF.Sqrt(unitCount) * 24f;
        var centerX = heightMap.MaxXCoordinate * 0.35f + span * 0.5f;
        var centerY = heightMap.MaxYCoordinate * 0.35f + span * 0.5f;
        var view = game.Scene3D.TacticalView;
        view.LookAt(new Vector3(centerX, centerY, 0f));
        // Tactical pitch ~55 degrees: the height that fits a field of this half-diagonal
        // leaves the whole population on screen, so frustum culling cannot trivially win.
        view.SnapHeightAboveGround(span * 1.6f);
    }

    private static BenchmarkRunRecord Measure(Game game, string definition, int unitCount, int warmupFrames, int measuredFrames)
    {
        var graphics = game.Graphics;
        var frames = new List<TickMetricSample>(measuredFrames);
        var spans = new List<SystemMetricSample>(measuredFrames * 4);
        graphics.RenderSpan += RecordSpan;

        var updateSamples = new List<double>(measuredFrames);
        var renderSamples = new List<double>(measuredFrames);
        var gpuSamples = new List<double>(measuredFrames);

        try
        {
            for (var frame = 0; frame < warmupFrames; frame++)
            {
                Step(game);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (var frame = 0; frame < measuredFrames; frame++)
            {
                var update = MeasureMs(() => game.Update(Array.Empty<InputMessage>()));
                var render = MeasureMs(game.Render);
                var gpu = MeasureMs(() => game.GraphicsDevice.WaitForIdle());

                updateSamples.Add(update);
                renderSamples.Add(render);
                gpuSamples.Add(gpu);
                var sample = new TickMetricSample((uint)frame + 1)
                {
                    WallMs = update + render + gpu,
                };
                frames.Add(sample);
                RecordSpanSamples(spans, (uint)frame + 1, update, render, gpu);
            }

            stopwatch.Stop();
            return BuildRecord(game, definition, unitCount, warmupFrames, measuredFrames, frames, spans,
                updateSamples, renderSamples, gpuSamples, stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            graphics.RenderSpan -= RecordSpan;
        }

        void RecordSpan(string name, TimeSpan span)
        {
            // Spans arrive during Render; the current frame is the last recorded sample.
            if (frames.Count > 0)
            {
                spans.Add(new SystemMetricSample(frames[^1].Frame, $"render.{name}", span.TotalMilliseconds));
            }
        }
    }

    private static void RecordSpanSamples(List<SystemMetricSample> spans, uint frame, double update, double render, double gpu)
    {
        spans.Add(new SystemMetricSample(frame, "update", update));
        spans.Add(new SystemMetricSample(frame, "renderSubmit", render));
        spans.Add(new SystemMetricSample(frame, "gpuDrain", gpu));
    }

    private static void Step(Game game)
    {
        game.Update(Array.Empty<InputMessage>());
        game.Render();
        game.GraphicsDevice.WaitForIdle();
    }

    private static double MeasureMs(Action action)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        action();
        return (System.Diagnostics.Stopwatch.GetTimestamp() - start)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private static BenchmarkRunRecord BuildRecord(
        Game game,
        string definition,
        int unitCount,
        int warmupFrames,
        int measuredFrames,
        List<TickMetricSample> frames,
        List<SystemMetricSample> spans,
        List<double> updateSamples,
        List<double> renderSamples,
        List<double> gpuSamples,
        double measuredWallMs)
    {
        var record = new BenchmarkRunRecord(
            $"b5.render-scale.{definition}.{unitCount}",
            ProbeSeed,
            warmupFrames,
            measuredFrames,
            measuredFrames,
            stopPredicateTriggered: false,
            measuredWallMs)
        {
            TickSamples = [.. frames],
            SystemSamples = [.. spans],
            Environment = CaptureEnvironment(game),
        };

        record.Statistics.Add(BenchmarkStatistics.FromSamples("frame.ms", [.. frames.Select(f => f.WallMs)], ProbeSeed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples("update.ms", [.. updateSamples], ProbeSeed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples("renderSubmit.ms", [.. renderSamples], ProbeSeed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples("gpuDrain.ms", [.. gpuSamples], ProbeSeed));
        record.Statistics.Add(BenchmarkStatistics.FromSamples(
            "frame.fps",
            [.. frames.Select(f => f.WallMs > 0 ? 1000.0 / f.WallMs : 0.0)],
            ProbeSeed));

        foreach (var name in new[] { "buildRenderList", "shadowPass", "forwardPass", "render" })
        {
            var samples = spans.Where(s => s.System == $"render.{name}").Select(s => s.SpanMs).ToArray();
            if (samples.Length > 0)
            {
                record.Statistics.Add(BenchmarkStatistics.FromSamples($"renderSpan.{name}.ms", samples, ProbeSeed));
            }
        }

        return record;
    }

    private static EnvironmentRecord CaptureEnvironment(Game game)
    {
        var environment = EnvironmentRecord.Capture();
        environment.InstallId = game.Definition.Identifier;
        environment.Env["PANOPTICON_PROBE_MODE"] = "render-scale";
        environment.Env["graphicsBackend"] = game.GraphicsDevice.BackendType.ToString();
        environment.Env["graphicsDevice"] = game.GraphicsDevice.DeviceName;
        var installPath = Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
        if (!string.IsNullOrEmpty(installPath))
        {
            environment.Env["CNC_GENERALS_PATH"] = installPath;
        }

        return environment;
    }

    private static string Summarize(BenchmarkRunRecord record)
    {
        static string Stat(BenchmarkRunRecord r, string metric) =>
            r.Statistics.FirstOrDefault(s => s.Metric == metric) is { } s
                ? $"{s.Median:F2} ms (CI {s.ConfidenceLow:F2}..{s.ConfidenceHigh:F2})"
                : "n/a";

        return
            $"Render-scale probe: frame {Stat(record, "frame.ms")}, " +
            $"render submit {Stat(record, "renderSubmit.ms")}, " +
            $"gpu drain {Stat(record, "gpuDrain.ms")}, " +
            $"fps {record.Statistics.FirstOrDefault(s => s.Metric == "frame.fps")?.Median:F1}";
    }

    private static string ProbeFileName(int unitCount, string definition, string extension) =>
        $"rendering-scale-{definition}-{unitCount}.{extension}";

    private static void CaptureScreenshot(Game game, string path, ITestHarnessReporter reporter)
    {
        try
        {
            game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, 1280, 800));
            game.Render();
            FrameCapturer.CaptureToPng(
                game.GraphicsDevice,
                game.Panel.Framebuffer.ColorTargets[0].Target,
                path);
            reporter.Info($"Render-scale probe screenshot saved to: {path}");
        }
        catch (Exception ex)
        {
            reporter.Error($"Screenshot capture failed: {ex.Message}");
        }
    }
}
