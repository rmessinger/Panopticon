using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.Graphics.Cameras;
using OpenSage.IO;
using OpenSage.Mods.BuiltIn;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.TestHarness;

public static class Program
{
    private const string ConfigFileName = "TestHarness.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Main(string[] args)
    {
        if (Array.Exists(args, a => a.Equals("--run-assert", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RunAndAssertCommand(args));
            return;
        }

        var config = LoadConfig();

        // --headless and --scenario override the config file (useful for VS Code launch configs and CI).
        if (Array.Exists(args, a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase)))
        {
            config.Headless = true;
        }

        var scenarioPath = GetArgValue(args, "--scenario");
        if (!string.IsNullOrEmpty(scenarioPath))
        {
            config.ScenarioPath = scenarioPath;
        }

        Run(config);
    }

    private static int RunAndAssertCommand(string[] args)
    {
        var scenarioPath = GetArgValue(args, "--scenario");
        if (string.IsNullOrEmpty(scenarioPath))
        {
            Console.Error.WriteLine("Missing required --scenario <path.json> argument.");
            return 1;
        }

        var gamePath = GetArgValue(args, "--game-path");
        var installationService = new GeneralsInstallationService();
        if (!string.IsNullOrEmpty(gamePath))
        {
            installationService.SetPath(gamePath);
        }

        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            Console.Error.WriteLine("Could not locate a Generals installation.");
            Console.Error.WriteLine(installationService.GetDiagnostics());
            return 1;
        }

        var scenario = ScenarioProfileLoader.LoadFromFile(scenarioPath);
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"opensage_runassert_{Guid.NewGuid():N}");
        try
        {
            var report = HarnessRunner.RunAndAssert(scenario, installation, tempDirectory);
            Console.WriteLine(report.ToJson());
            return report.AllPassed ? 0 : 1;
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static TestHarnessConfig LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Config file not found: {path}");
            Console.Error.WriteLine("Creating a default config file.");
            var defaults = new TestHarnessConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TestHarnessConfig>(json, JsonOptions)
               ?? new TestHarnessConfig();
    }

    private static void Run(TestHarnessConfig config)
    {
        var definition = GameDefinition.FromGame(config.Game);
        var effectiveGamePath = config.GamePath != null && Directory.Exists(config.GamePath)
            ? config.GamePath
            : null;
        var installation = effectiveGamePath != null
            ? new GameInstallation(definition, effectiveGamePath)
            : GameInstallation.FindAll(new[] { definition }).FirstOrDefault();

        if (installation == null)
        {
            Console.Error.WriteLine($"Could not find an installation of {definition.DisplayName}.");
            Console.Error.WriteLine($"Set the {definition.Identifier.ToUpper()}_PATH environment variable or set GamePath in {ConfigFileName}.");
            Environment.Exit(1);
            return;
        }

        if (config.Scenario == BuiltInScenario.UnitValidationGround)
        {
            RunValidationGround(installation, config);
            return;
        }

        var scenario = LoadScenario(config.Scenario, config.ScenarioPath);
        var map = MapMetadataService.Load(installation, scenario.MapPath);
        var outputPath = Path.Combine(Path.GetTempPath(), $"opensage_scenario_{Guid.NewGuid()}.rep");
        var resultsPath = Path.ChangeExtension(outputPath, null) + ".results.txt";

        try
        {
            Console.WriteLine($"Exporting scenario to {outputPath} ...");
            var exportError = ReplayExporter.Export(scenario, map, outputPath, out var deferredActions);
            if (exportError != null)
            {
                Console.Error.WriteLine($"Export failed: {exportError}");
                Environment.Exit(1);
                return;
            }

            using var results = new ActionResultsWriter(resultsPath, config.Scenario.ToString(), config.ScenarioPath ?? "(built-in)", config.Game.ToString());

            if (config.Headless)
            {
                RunReplay(installation, config, outputPath, deferredActions, results);
            }
            else
            {
                Platform.Start();
                try
                {
                    RunReplay(installation, config, outputPath, deferredActions, results);
                }
                finally
                {
                    Platform.Stop();
                }
            }
        }
        finally
        {
            if (!config.KeepReplay && File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            else if (config.KeepReplay)
            {
                Console.WriteLine($"Replay saved to: {outputPath}");
            }

            Console.WriteLine($"Action results saved to: {resultsPath}");
        }
    }

    private static void RunReplay(GameInstallation installation, TestHarnessConfig config, string replayPath,
        IReadOnlyList<DeferredAction> deferredActions, ActionResultsWriter results)
    {
        if (config.Headless)
        {
            RunHeadless(installation, config, replayPath, deferredActions, results);
        }
        else
        {
            RunWindowed(installation, config, replayPath, deferredActions, results);
        }
    }

    private static void RunValidationGround(GameInstallation installation, TestHarnessConfig config)
    {
        // The validation ground runs thousands of frames with the full roster fighting; the engine's
        // per-frame Info logs (weapon/animation state transitions) would otherwise produce gigabytes
        // of output and dominate run time. The measurement is logic-frame counts, unaffected by log
        // verbosity, so drop the Info level (Warn/Error still flow).
        foreach (var rule in NLog.LogManager.Configuration.LoggingRules)
        {
            rule.DisableLoggingForLevel(NLog.LogLevel.Info);
        }
        NLog.LogManager.ReconfigExistingLoggers();

        var scenario = new ValidationGroundScenario
        {
            MapPath = string.IsNullOrEmpty(config.ValidationGroundMapPath)
                ? "maps/alpine assault/alpine assault.map"
                : config.ValidationGroundMapPath,
            SettleFrames = config.ValidationGroundSettleFrames,
            Seed = config.ValidationGroundSeed,
        };
        var plan = ValidationGroundBuilder.Build(scenario);
        var totalUnits = plan.UnitPlacements.Count;

        // Three competitive slots (USA/China/GLA) become player indices 2/3/4 after neutral/civilian.
        // SideName uses the Faction-prefixed PlayerTemplate name, matching every other call site;
        // the skirmish-AI script path resolves PlayerTemplates by this name (SidesListUtility).
        var playerSettings = new OpenSage.Logic.PlayerSetting[]
        {
            new(0, "FactionAmerica", new Mathematics.ColorRgb(255, 0, 0), 0, OpenSage.Logic.PlayerOwner.EasyAi, "USA"),
            new(1, "FactionChina", new Mathematics.ColorRgb(0, 255, 0), 0, OpenSage.Logic.PlayerOwner.EasyAi, "China"),
            new(2, "FactionGLA", new Mathematics.ColorRgb(0, 0, 255), 0, OpenSage.Logic.PlayerOwner.EasyAi, "GLA"),
        };

        Game? game = null;
        GameWindow? window = null;
        try
        {
            if (config.Headless)
            {
                game = new Game(installation, config.Renderer, new Configuration { NoAudio = true }, null);
            }
            else
            {
                Platform.Start();
                window = new GameWindow("OpenSAGE - Unit Validation Ground", 100, 100, 1280, 800, config.Fullscreen);
                game = new Game(installation, config.Renderer, new Configuration { NoAudio = true }, window);
            }

            game.StartSkirmishOrMultiPlayerGame(scenario.MapPath, new Network.EchoConnection(), playerSettings, scenario.Seed, isMultiPlayer: false);
            game.StartRun();

            // The validation ground spawns and orders units directly; an active skirmish AI would
            // both dominate per-frame cost and override those orders. Per deep dive §2.1 ("no AI,
            // no economy"), disable AI on the three roster players (neutral/civilian at 0/1 have none).
            foreach (var idx in new[] { 2, 3, 4 })
            {
                game.Scene3D.Players[idx].DisableAI();
            }

            // Fixed-timestep: each Update advances exactly one logic frame, independent of wall-clock,
            // so logic-frame measurements are reproducible (deep dive §2.9).
            game.EnableFixedLogicTimestep();

            var host = new ValidationGroundHost();
            host.Spawn(plan, game, scenario.TargetHp);
            host.SpawnKeepAliveStructures(game);

            var timer = new UnitActionTimer();
            foreach (var gap in host.Gaps)
            {
                timer.RecordGap(gap.DefinitionName, gap.FactionIndex, gap.ActionKind, gap.Reason);
            }

            // Settle stage: no orders until this many frames have elapsed. Logic-only stepping
            // (no Render) keeps the ground fast; the measurement is logic-frame counts, unaffected
            // by rendering.
            for (var i = 0; i < scenario.SettleFrames; i++)
            {
                StepLogicOnly(game);
            }

            host.IssueOrders(game, timer);
            var observe = host.BuildObservationSeam(game);

            // Run until every unit has resolved (timed out, completed, or gapped) or the cap hits.
            const uint MaxRunFrames = 5_000;
            var startFrame = CurrentLogicFrame(game);
            while (game.IsRunning)
            {
                StepLogicOnly(game);
                var frame = CurrentLogicFrame(game);
                timer.Poll(frame, observe);
                // A unit that destroys its target has finished; clear its weapon so it stops firing
                // (and its turret stops auto-acquiring new enemies), keeping the run bounded.
                host.DisarmUnitsWhoseTargetIsDestroyed(game);

                var report = timer.BuildReport();
                if (report.TotalUnitCount >= totalUnits || frame - startFrame > MaxRunFrames)
                {
                    break;
                }
            }

            FinishValidationGround(timer, config);
        }
        finally
        {
            game?.Dispose();
            window?.Dispose();
            if (!config.Headless)
            {
                Platform.Stop();
            }
        }
    }

    private static void FinishValidationGround(UnitActionTimer timer, TestHarnessConfig config)
    {
        var report = timer.BuildReport();
        UnitTimingBaseline? baseline = null;
        if (!string.IsNullOrEmpty(config.ValidationGroundBaselinePath))
        {
            baseline = UnitTimingBaseline.Load(config.ValidationGroundBaselinePath!);
        }

        Console.Write(ValidationGroundReportWriter.WriteTextReport(report, baseline));

        if (!string.IsNullOrEmpty(config.ValidationGroundReportPath))
        {
            ValidationGroundReportWriter.WriteJson(report, baseline, config.ValidationGroundReportPath!);
            Console.WriteLine($"Report JSON saved to: {config.ValidationGroundReportPath}");
        }

        if (baseline != null)
        {
            if (config.ValidationGroundRefreshBaseline)
            {
                RefreshBaseline(baseline, report, config.ValidationGroundBaselinePath!);
                return;
            }

            if (baseline.HasRegression(report) || report.Timeouts.Count > 0)
            {
                Environment.ExitCode = 2;
            }
        }
        else if (report.Timeouts.Count > 0)
        {
            Environment.ExitCode = 3;
        }
    }

    private static void RefreshBaseline(UnitTimingBaseline baseline, ValidationGroundReport report, string path)
    {
        // Capture measured timings as the new golden values. Only run under the explicit refresh flag.
        baseline.Entries.Clear();
        foreach (var timing in report.Timings)
        {
            baseline.Entries.Add(new UnitTimingBaselineEntry
            {
                DefinitionName = timing.DefinitionName,
                FactionIndex = timing.FactionIndex,
                ActionKind = timing.ActionKind.ToString(),
                GoldenFrames = timing.Frames,
                TolerancePercent = 10.0,
                MinimumFrameFloor = 3,
            });
        }
        baseline.Save(path);
        Console.WriteLine($"Baseline refreshed at: {path}");
    }

    /// <summary>
    /// Advances the simulation without rendering. Used by the validation ground, whose measurement
    /// is logic-frame counts: rendering a full scene with the roster fighting dominates run time
    /// and adds nothing to the timing. MapTime still advances via the game's delta timer, so logic
    /// frames tick as Update is called.
    /// </summary>
    private static void StepLogicOnly(Game game)
    {
        game.Update(Array.Empty<Input.InputMessage>());
    }

    private static void StepOnce(Game game, GameWindow? window)
    {
        if (window != null)
        {
            if (!window.PumpEvents())
            {
                game.Exit();
                return;
            }

            game.Update(window.MessageQueue);
            game.Panel.EnsureFrame(window.ClientBounds);
            game.Render();
            game.GraphicsDevice.SwapBuffers(window.Swapchain);
            window.MessageQueue.Clear();
        }
        else
        {
            game.Update(Array.Empty<Input.InputMessage>());
            game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, 1280, 800));
            game.Render();
        }
    }

    private static uint CurrentLogicFrame(Game game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));

    private static void RunWindowed(GameInstallation installation, TestHarnessConfig config, string replayPath,
        IReadOnlyList<DeferredAction> deferredActions, ActionResultsWriter results)
    {
        using var window = new GameWindow("OpenSAGE - TestHarness", 100, 100, 1024, 768, config.Fullscreen);
        using var game = new Game(installation, config.Renderer, new Configuration { NoAudio = true, CameraControlScheme = config.CameraControlScheme, MouseClickMode = config.MouseClickMode }, window);
        using var textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription);

        using var repFileSystem = new DiskFileSystem(Path.GetDirectoryName(replayPath)!);
        var repEntry = repFileSystem.GetFile(Path.GetFileName(replayPath));

        game.LoadReplayFile(repEntry);
        game.StartRun();
        ApplyCameraZoomMultiplierOnce(game, config.MaxCameraZoomMultiplier);

        using var monitor = new ReplayActionMonitor(game);
        monitor.ActionDispatched += (_, e) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            Console.WriteLine($"  [dispatched] {e.ActionType,-20} player: {e.Player?.Name ?? "?"} frame: {frame}");
        };
        monitor.ActionCompleted += (_, e) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            Console.WriteLine($"  [completed]  {e.ActionType,-20} player: {e.Player?.Name ?? "?"} frame: {frame}");
        };
        using var recorder = new EngineActionRecorder(game, results);
        var pendingInjections = BuildDeferredInjector(game, monitor, deferredActions);

        Console.WriteLine("Running replay — close the window or wait for it to finish.");

        while (game.IsRunning)
        {
            if (!window.PumpEvents())
            {
                break;
            }

            DrainPendingInjections(game, pendingInjections);
            game.Update(window.MessageQueue);
            game.Panel.EnsureFrame(window.ClientBounds);
            game.Render();
            textureCopier.Execute(
                game.Panel.Framebuffer.ColorTargets[0].Target,
                window.Swapchain.Framebuffer);
            window.MessageQueue.Clear();
            game.GraphicsDevice.SwapBuffers(window.Swapchain);

            recorder.PollPendingMoves();
            recorder.PollHealthChanges();
            recorder.PollSupplyCycles();
            results.MaybeFlush();
        }

        Console.WriteLine("Done.");
    }

    private static void RunHeadless(GameInstallation installation, TestHarnessConfig config, string replayPath,
        IReadOnlyList<DeferredAction> deferredActions, ActionResultsWriter results)
    {
        using var game = new Game(installation, config.Renderer, new Configuration { NoAudio = true, CameraControlScheme = config.CameraControlScheme, MouseClickMode = config.MouseClickMode }, null);

        using var repFileSystem = new DiskFileSystem(Path.GetDirectoryName(replayPath)!);
        var repEntry = repFileSystem.GetFile(Path.GetFileName(replayPath));

        game.LoadReplayFile(repEntry);
        game.StartRun();
        ApplyCameraZoomMultiplierOnce(game, config.MaxCameraZoomMultiplier);

        using var capture = new FrameCaptureService();

        // Once the match reaches a decided outcome, run a short tail period then exit.
        const uint RunoutLogicFrames = 300; // 10 game-seconds at 30 fps
        var runoutEndLogicFrame = uint.MaxValue;

        using var monitor = new ReplayActionMonitor(game);
        monitor.ActionDispatched += (_, e) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            Console.WriteLine($"  [dispatched] {e.ActionType,-20} player: {e.Player?.Name ?? "?"} frame: {frame}");
        };
        monitor.ActionCompleted += (_, e) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            Console.WriteLine($"  [completed]  {e.ActionType,-20} player: {e.Player?.Name ?? "?"} frame: {frame}");

            if (config.CaptureFrames)
            {
                // Schedule a capture after every completed action so agents can verify
                // the visual state immediately after each game event.
                // The [CAPTURE] log line is emitted after the next render (FlushPending below).
                capture.ScheduleCapture(
                    $"Action '{e.ActionType}' completed for player '{e.Player?.Name ?? "unknown"}'. " +
                    "Terrain should be visible. Units and buildings should be 3-D models (not invisible). " +
                    "No solid-colour triangle artefacts. UI chrome should be present at the bottom.");
            }
        };

        game.MatchOutcomeDecided += (_, _) =>
        {
            var frame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
            var outcome = game.CurrentOutcome;
            if (runoutEndLogicFrame == uint.MaxValue)
            {
                runoutEndLogicFrame = frame + RunoutLogicFrames;
            }
            Console.WriteLine($"  [match-decided] status: {outcome.Status} reason: {outcome.Reason} " +
                              $"winner: {outcome.WinningPlayer?.Name ?? "(shared)"} frame: {frame}");
        };

        var pendingInjections = BuildDeferredInjector(game, monitor, deferredActions);
        using var recorder = new EngineActionRecorder(game, results);

        Console.WriteLine("Running headless replay.");
        if (config.CaptureFrames)
        {
            Console.WriteLine($"Captures written to: {FrameCaptureService.CaptureDirectory}");
            Console.WriteLine("Agent: monitor stdout for [CAPTURE] lines and inspect during the run; files are deleted on exit.");
        }

        var frameRect = new OpenSage.Mathematics.Rectangle(0, 0, 1024, 768);

        while (game.IsRunning)
        {
            DrainPendingInjections(game, pendingInjections);
            game.Update(Array.Empty<OpenSage.Input.InputMessage>());
            game.Panel.EnsureFrame(frameRect);
            game.Render();
            if (config.CaptureFrames)
            {
                capture.FlushPending(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
            }

            recorder.PollPendingMoves();
            recorder.PollHealthChanges();
            recorder.PollSupplyCycles();
            results.MaybeFlush();

            if (runoutEndLogicFrame != uint.MaxValue)
            {
                var logicFrame = (uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame);
                if (logicFrame >= runoutEndLogicFrame)
                {
                    game.Exit();
                }
            }
        }

        Console.WriteLine("Done.");
    }

    /// <summary>
    /// Subscribes to <see cref="ReplayActionMonitor.ActionDispatched"/> and
    /// <see cref="ReplayActionMonitor.ActionCompleted"/> events and, for each
    /// <see cref="DeferredAction"/> whose trigger and <see cref="DeferredAction.Phase"/> match,
    /// enqueues its orders for injection on the next game-loop iteration (via
    /// <see cref="DrainPendingInjections"/>).
    /// </summary>
    private static List<(int RemainingFrames, IReadOnlyList<Logic.Orders.Order> Orders)> BuildDeferredInjector(
        Game game,
        ReplayActionMonitor monitor,
        IReadOnlyList<DeferredAction> deferredActions)
    {
        var pending = new List<(int RemainingFrames, IReadOnlyList<Logic.Orders.Order> Orders)>();

        monitor.ActionDispatched += (_, e) =>
        {
            foreach (var deferred in deferredActions)
            {
                if (deferred.Phase == ActionPhase.Dispatched
                    && deferred.Trigger == e.ActionType
                    && deferred.PlayerIndex == (int?)e.Player?.Id
                    && (deferred.TriggerDefinitionName == null || deferred.TriggerDefinitionName == e.DefinitionName))
                {
                    pending.Add(((int)deferred.ExtraFrames, deferred.Orders));
                }
            }
        };

        monitor.ActionCompleted += (_, e) =>
        {
            foreach (var deferred in deferredActions)
            {
                if (deferred.Phase == ActionPhase.Completed
                    && deferred.Trigger == e.ActionType
                    && deferred.PlayerIndex == (int?)e.Player?.Id
                    && (deferred.TriggerDefinitionName == null || deferred.TriggerDefinitionName == e.DefinitionName))
                {
                    pending.Add(((int)deferred.ExtraFrames, deferred.Orders));
                }
            }
        };

        return pending;
    }

    /// <summary>
    /// Injects any pending deferred orders whose frame delay has elapsed.
    /// Call once per game-loop iteration, BEFORE <c>game.Update()</c>.
    /// </summary>
    private static void DrainPendingInjections(
        Game game,
        List<(int RemainingFrames, IReadOnlyList<Logic.Orders.Order> Orders)> pending)
    {
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var (remaining, orders) = pending[i];
            if (remaining <= 0)
            {
                game.InjectOrders(orders);
                pending.RemoveAt(i);
            }
            else
            {
                pending[i] = (remaining - 1, orders);
            }
        }
    }

    /// <summary>
    /// Subscribes to <see cref="Game.Updating"/> and, on the first frame where
    /// <see cref="IGame.Scene3D"/> is available, multiplies the tactical camera's
    /// <see cref="Graphics.Cameras.TacticalView.MaxHeightAboveGround"/> by
    /// <paramref name="multiplier"/> then unsubscribes.
    /// </summary>
    private static void ApplyCameraZoomMultiplierOnce(Game game, float multiplier)
    {
        if (multiplier == 1.0f)
        {
            return;
        }

        EventHandler<GameUpdatingEventArgs> handler = null!;
        handler = (_, _) =>
        {
            var view = game.Scene3D?.TacticalView;
            if (view == null)
            {
                return;
            }

            view.MaxHeightAboveGround *= multiplier;
            game.Updating -= handler;
        };
        game.Updating += handler;
    }

    private static ReplayScenario LoadScenario(BuiltInScenario builtInScenario, string? path)
    {
        if (path == null)
        {
            return builtInScenario switch
            {
                BuiltInScenario.AlpineAssaultUSAvGLA => ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA(),
                BuiltInScenario.AlpineAssaultUSAvGLAFullBuild => ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild(),
                _ => throw new ArgumentOutOfRangeException(nameof(builtInScenario), builtInScenario, "Unsupported built-in scenario."),
            };
        }

        var json = File.ReadAllText(path);
        var scenario = JsonSerializer.Deserialize<ReplayScenario>(json,
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });

        if (scenario == null)
        {
            Console.Error.WriteLine($"Failed to deserialize scenario from '{path}'.");
            Environment.Exit(1);
        }

        return scenario!;
    }
}
