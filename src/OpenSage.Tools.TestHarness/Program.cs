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
        var scenario = LoadScenario(config.Scenario, config.ScenarioPath);

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

        var capture = new FrameCaptureService();

        // Once the first enemy Command Center is destroyed, run a short tail period then exit.
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

            if (e.ActionType == ActionType.EnemyBaseDestroyed && runoutEndLogicFrame == uint.MaxValue)
            {
                runoutEndLogicFrame = frame + RunoutLogicFrames;
            }
        };

        var pendingInjections = BuildDeferredInjector(game, monitor, deferredActions);
        using var recorder = new EngineActionRecorder(game, results);

        Console.WriteLine("Running headless replay.");
        if (config.CaptureFrames)
        {
            Console.WriteLine($"Captures written to: {FrameCaptureService.CaptureDirectory}");
            Console.WriteLine("Agent: monitor stdout for [CAPTURE] lines; open the PNG; validate; delete the file.");
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
