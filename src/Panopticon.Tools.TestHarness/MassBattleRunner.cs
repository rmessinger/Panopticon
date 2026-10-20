using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Logic;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Network;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Model.Capture;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-M5-04: the mass battle scenario. One battle profile spawns both roster manifests
/// (WI-M5-02) under army AI on each side (WI-M5-01) and runs to a P3 outcome (WI-M5-03): a side
/// is eliminated when it owns no living non-structure attacker, and the frame limit decides
/// stalled battles by unit count. Headless runs step through <see cref="SimulationHost"/> with a
/// <see cref="RunOutputRecorder"/> and the B1 recorder; windowed runs step the same world
/// through the probe's frame loop with the same stop condition.
/// </summary>
public static class MassBattleRunner
{
    private const string DefaultReportFileName = "mass-battle-report.json";

    private const string DefaultRunOutputFileName = "mass-battle-run-output.json";

    public const string ScenarioId = "mass-battle";

    private const int SettleFrames = 30;

    // Exit codes follow the PhysicsStack convention: 1 setup error, 5 unmet acceptance check.
    private const int ExitSetupError = 1;
    private const int ExitCheckFailed = 5;

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var profilePath = ResolveProfilePath(config, reporter);
        if (profilePath == null)
        {
            return ExitSetupError;
        }

        MassBattleDeploymentProfile profile;
        RosterManifest manifest;
        try
        {
            profile = MassBattleDeploymentProfile.Load(profilePath);
            manifest = RosterBudgetEvaluator.Load(ResolveManifestPath(profile, profilePath));
        }
        catch (Exception ex)
        {
            reporter.Error($"Could not load battle profile '{profilePath}': {ex.Message}");
            return ExitSetupError;
        }

        var validationError = MassBattleRosterSpawner.Validate(profile, manifest);
        if (validationError != null)
        {
            reporter.Error(validationError);
            return ExitSetupError;
        }

        var seed = (uint)(config.MassBattleSeed ?? 1);
        var frameLimit = config.MassBattleFrameLimit ?? 54_000;

        // Same log flood control as the probe: army spawn Info lines drown stdout.
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
        GameWindow? window = null;
        TextureCopier? textureCopier = null;
        HarnessOverlay? overlay = null;
        try
        {
            if (config.Headless)
            {
                game = new Game(
                    installation,
                    config.Renderer,
                    new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud },
                    null);
            }
            else
            {
                Platform.Start();
                window = new GameWindow("Panopticon - Mass Battle", 100, 100, 1280, 800, config.Fullscreen);
                game = new Game(
                    installation,
                    config.Renderer,
                    new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud },
                    window);
                textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription);
                overlay = new HarnessOverlay(game, window);
            }

            // The battle owns every order: hierarchical army AI per side (WI-M5-01), hostile
            // teams, and the army-eliminated outcome with a unit-count frame limit (WI-M5-03).
            var playerSettings = profile.Sides.Select((side, index) => new PlayerSetting(
                index,
                side.Side,
                MassBattlePlacement.ParseColor(side.Color, index),
                index,
                PlayerOwner.EasyAi,
                side.Id)
            ).ToArray();
            ArmyAiSides.ApplyPolicies(playerSettings);

            game.StartSkirmishOrMultiPlayerGame(profile.Map.Path, new EchoConnection(), playerSettings, (int)seed, isMultiPlayer: false);
            game.StartRun();
            CameraZoomInitializer.ApplyOnce(game, config.MaxCameraZoomMultiplier);
            game.VictoryOptions = new VictoryOptions(frameLimit, VictoryScoreMetric.UnitCount, VictoryConditionKind.ArmyEliminated);
            FrameDeployment(game, profile);

            var armyAiSides = ArmyAiSides.Resolve(game.Scene3D.Players, profile.Sides.Select(side => side.Id).ToArray());

            if (!MassBattleRosterSpawner.ImportEe2Content(game, profile, manifest, reporter))
            {
                return ExitSetupError;
            }

            var spawnResults = new List<RosterSideSpawnResult>();
            var navigation = game.Scene3D.Navigation;
            for (var i = 0; i < profile.Sides.Count; i++)
            {
                var manifestSide = manifest.Sides.Single(s => s.Id == profile.Sides[i].Roster);
                spawnResults.Add(MassBattleRosterSpawner.SpawnSide(
                    game, profile, i, armyAiSides[i].Player, manifestSide, navigation, reporter));
            }

            string? screenshotPath = null;
            if (!string.IsNullOrEmpty(config.MassBattleScreenshotPath))
            {
                screenshotPath = CaptureScreenshot(game, config.MassBattleScreenshotPath!, reporter);
            }

            var (framesRun, runOutput, benchmarkRecord) = config.Headless
                ? RunHeadless(game, frameLimit, seed)
                : RunWindowed(game, window!, textureCopier!, overlay!, frameLimit, seed);

            string? runOutputPath = null;
            if (runOutput is not null)
            {
                runOutputPath = string.IsNullOrEmpty(config.MassBattleReportPath)
                    ? Path.Combine(AppContext.BaseDirectory, DefaultRunOutputFileName)
                    : Path.ChangeExtension(config.MassBattleReportPath!, ".run-output.json");
                runOutput.SaveToFile(runOutputPath);
                reporter.Info($"Run output saved to: {runOutputPath}");
            }

            var reportPath = string.IsNullOrEmpty(config.MassBattleReportPath)
                ? Path.Combine(AppContext.BaseDirectory, DefaultReportFileName)
                : config.MassBattleReportPath!;
            var report = BuildReport(
                profile, profilePath, config.Headless, seed, frameLimit, framesRun, game, armyAiSides, spawnResults,
                screenshotPath, runOutputPath);
            MassBattleReportIo.WriteJson(report, reportPath);
            reporter.Info(MassBattleReportIo.WriteText(report));
            reporter.Info($"Report JSON saved to: {reportPath}");

            if (benchmarkRecord is not null)
            {
                var benchmarkJsonPath = Path.ChangeExtension(reportPath, ".benchmark.json");
                BenchmarkReportWriter.WriteJson(benchmarkRecord, benchmarkJsonPath);
                reporter.Info($"B1 benchmark JSON saved to: {benchmarkJsonPath}");
            }

            return report.AllPassed ? 0 : ExitCheckFailed;
        }
        finally
        {
            overlay?.Dispose();
            textureCopier?.Dispose();
            game?.Dispose();
            window?.Dispose();
            if (!config.Headless)
            {
                Platform.Stop();
            }
        }
    }

    private static (int FramesRun, RunOutput? RunOutput, BenchmarkRunRecord? Benchmark) RunHeadless(Game game, int frameLimit, uint seed)
    {
        game.EnableFixedLogicTimestep();

        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var recorder = new RunOutputRecorder(host, game, seed, ScenarioId);
        using var benchmark = new BenchmarkMetricRecorder();
        host.LogicSystemSpan += benchmark.RecordSystemSpan;
        host.TickStarting += OnBenchmarkTickStarting;
        host.TickComplete += OnBenchmarkTickComplete;

        // Settle is unmeasured, mirroring the probe's warm-up (spawn transients before the AI's
        // first decision tick); the battle then runs to the decided outcome or the frame limit.
        host.Run(SettleFrames);
        var startFrame = host.Frame;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        RunOutput output;
        try
        {
            host.Run(
                frameLimit,
                h => h.Game.CurrentOutcome.Status == OutcomeStatus.Decided || !h.Game.IsRunning);
        }
        finally
        {
            stopwatch.Stop();
            output = recorder.Complete();
            recorder.Dispose();
            host.LogicSystemSpan -= benchmark.RecordSystemSpan;
            host.TickStarting -= OnBenchmarkTickStarting;
            host.TickComplete -= OnBenchmarkTickComplete;
        }

        var framesRun = (int)(host.Frame - startFrame);
        return (framesRun, output, BuildBenchmarkRecord(game, benchmark, framesRun, frameLimit, seed, stopwatch.Elapsed.TotalMilliseconds, headless: true));

        void OnBenchmarkTickStarting(object? sender, SimulationHostTickEventArgs e) => benchmark.BeginTick(e.Frame);

        void OnBenchmarkTickComplete(object? sender, SimulationHostTickEventArgs e) => benchmark.EndTick(e.Frame);
    }

    private static (int FramesRun, RunOutput? RunOutput, BenchmarkRunRecord? Benchmark) RunWindowed(
        Game game,
        GameWindow window,
        TextureCopier textureCopier,
        HarnessOverlay overlay,
        int frameLimit,
        uint seed)
    {
        // Windowed runs step the same world through the presentation loop (the probe's step
        // driver); the stop condition matches headless, so only the driver differs.
        using var benchmark = new BenchmarkMetricRecorder();
        game.LogicStepper.SystemSpan += benchmark.RecordSystemSpan;
        var step = () => HarnessWindowedFrame.Step(game, window, textureCopier, overlay);

        for (var i = 0; i < SettleFrames; i++)
        {
            step();
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var framesRun = 0;
        try
        {
            while (framesRun < frameLimit
                && game.IsRunning
                && game.CurrentOutcome.Status != OutcomeStatus.Decided)
            {
                benchmark.BeginTick((uint)framesRun + 1);
                step();
                benchmark.EndTick((uint)framesRun + 1);
                framesRun++;
            }
        }
        finally
        {
            stopwatch.Stop();
            game.LogicStepper.SystemSpan -= benchmark.RecordSystemSpan;
        }

        return (framesRun, null, BuildBenchmarkRecord(game, benchmark, framesRun, frameLimit, seed, stopwatch.Elapsed.TotalMilliseconds, headless: false));
    }

    private static BenchmarkRunRecord BuildBenchmarkRecord(
        Game game,
        BenchmarkMetricRecorder recorder,
        int framesRun,
        int frameLimit,
        uint seed,
        double wallMs,
        bool headless)
    {
        var environment = EnvironmentRecord.Capture();
        environment.InstallId = game.Definition.Identifier;
        environment.Env["PANOPTICON_PROBE_MODE"] = headless ? "headless" : "windowed";
        var installPath = Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
        if (!string.IsNullOrEmpty(installPath))
        {
            environment.Env["CNC_GENERALS_PATH"] = installPath;
        }

        var record = new BenchmarkRunRecord(
            ScenarioId,
            seed,
            SettleFrames,
            frameLimit,
            framesRun,
            stopPredicateTriggered: game.CurrentOutcome.Status == OutcomeStatus.Decided,
            measuredWallMs: wallMs)
        {
            TickSamples = [.. recorder.TickSamples],
            SystemSamples = [.. recorder.SystemSamples],
            Environment = environment,
        };
        record.Statistics.AddRange(SimulationBenchmarkRunner.BuildStatistics(record, seed, baseline: null));
        return record;
    }

    private static MassBattleReport BuildReport(
        MassBattleDeploymentProfile profile,
        string profilePath,
        bool headless,
        uint seed,
        int frameLimit,
        int framesRun,
        Game game,
        IReadOnlyList<ArmyAiSide> armyAiSides,
        List<RosterSideSpawnResult> spawnResults,
        string? screenshotPath,
        string runOutputPath)
    {
        var outcome = game.CurrentOutcome;
        string? winningSideId = null;
        if (outcome.WinningPlayer is { } winner)
        {
            var side = armyAiSides.FirstOrDefault(s => ReferenceEquals(s.Player, winner));
            winningSideId = side?.SideId ?? winner.Name;
        }

        var sides = new List<MassBattleSideReport>();
        foreach (var spawn in spawnResults)
        {
            var entries = spawn.GroundEntries
                .Select(entry => new MassBattleEntryReport
                {
                    Definition = entry.Definition,
                    Aircraft = false,
                    Spawned = entry.Spawned,
                    AliveAtEnd = spawn.Units.Count(
                        unit => !unit.IsEffectivelyDead && unit.Definition.Name == NativeName(entry.Definition)),
                })
                .Concat(spawn.AircraftEntries.Select(entry => new MassBattleEntryReport
                {
                    Definition = entry.Definition,
                    Aircraft = true,
                    Spawned = entry.Spawned,
                    AliveAtEnd = spawn.Units.Count(
                        unit => !unit.IsEffectivelyDead && unit.Definition.Name == NativeName(entry.Definition)),
                }))
                .ToList();

            sides.Add(new MassBattleSideReport
            {
                SideId = spawn.SideId,
                RosterId = spawn.RosterId,
                UnitsSpawned = entries.Sum(entry => entry.Spawned),
                UnitsAliveAtEnd = entries.Sum(entry => entry.AliveAtEnd),
                AirfieldsPlaced = spawn.AirfieldsPlaced,
                Entries = entries,
            });
        }

        var reasons = new List<string>(spawnResults.SelectMany(result => result.FailureReasons).Distinct());
        if (outcome.Status != OutcomeStatus.Decided)
        {
            reasons.Add($"battle did not decide within {frameLimit} frames (status {outcome.Status})");
        }

        var report = new MassBattleReport
        {
            Map = profile.Map.Name,
            ProfilePath = profilePath,
            Seed = seed,
            Mode = headless ? "headless" : "windowed",
            FrameLimit = frameLimit,
            FramesRun = framesRun,
            OutcomeStatus = outcome.Status.ToString(),
            OutcomeReason = outcome.Reason.ToString(),
            WinningSideId = winningSideId,
            DecidedOnFrame = outcome.DecidedOnFrame,
            Sides = sides,
            ScreenshotPath = screenshotPath,
            RunOutputPath = runOutputPath,
            FailureReasons = reasons,
        };
        return report with { AllPassed = reasons.Count == 0 };
    }

    private static string NativeName(string definitionName)
    {
        var separator = definitionName.IndexOf(':');
        return separator > 0 ? definitionName[(separator + 1)..] : definitionName;
    }

    private static void FrameDeployment(Game game, MassBattleDeploymentProfile profile)
    {
        var bounds = MassBattlePlacement.PlacementBounds(profile);
        var framingHeight = MathF.Max(
            4000f,
            1.25f * MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height));

        // Whole-map views outrun the default far plane; without this bump the battlefield
        // clips out of view entirely.
        game.Scene3D.Camera.FarPlaneDistance = MathF.Max(12_000f, 3f * framingHeight);
        var view = game.Scene3D.TacticalView;
        view.SetAngleAndPitchToDefault();
        view.SnapHeightAboveGround(framingHeight);
        view.LookAt(new System.Numerics.Vector3(
            bounds.X + bounds.Width / 2f,
            bounds.Y + bounds.Height / 2f,
            0f));
    }

    private static string? ResolveProfilePath(TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        if (!string.IsNullOrWhiteSpace(config.MassBattleProfilePath))
        {
            if (File.Exists(config.MassBattleProfilePath))
            {
                return config.MassBattleProfilePath;
            }

            reporter.Error($"MassBattleProfilePath '{config.MassBattleProfilePath}' does not exist.");
            return null;
        }

        var embedded = Path.Combine(AppContext.BaseDirectory, MassBattleRosterSpawner.DefaultBattleProfileFileName);
        if (File.Exists(embedded))
        {
            return embedded;
        }

        reporter.Error(
            $"No battle profile: set MassBattleProfilePath or ship '{MassBattleRosterSpawner.DefaultBattleProfileFileName}' beside the executable.");
        return null;
    }

    /// <summary>
    /// The manifest path is absolute, or relative to the profile's directory (a temp-dir scaled
    /// profile carries its own manifest), or the copy beside the executable.
    /// </summary>
    private static string ResolveManifestPath(MassBattleDeploymentProfile profile, string profilePath)
    {
        var configured = profile.RosterManifestPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(AppContext.BaseDirectory, RosterBudgetEvaluator.DefaultManifestFileName);
        }

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilePath))!, configured);
    }

    private static string? CaptureScreenshot(Game game, string path, ITestHarnessReporter reporter)
    {
        try
        {
            game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, 1920, 1080));
            game.Render();
            FrameCapturer.CaptureToPng(
                game.GraphicsDevice,
                game.Panel.Framebuffer.ColorTargets[0].Target,
                path);
            reporter.Info($"Mass battle screenshot saved to: {path}");
            return path;
        }
        catch (Exception ex)
        {
            reporter.Error($"Screenshot capture failed: {ex.Message}");
            return null;
        }
    }
}
