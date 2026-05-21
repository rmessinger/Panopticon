using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSage.Data;
using OpenSage.Graphics;
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
        var config = LoadConfig();

        // --headless overrides the config file (useful for VS Code launch configs and CI).
        if (Array.Exists(args, a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase)))
        {
            config.Headless = true;
        }

        Run(config);
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
        var scenario = LoadScenario(config.ScenarioPath);

        var definition = GameDefinition.FromGame(config.Game);
        var installation = config.GamePath != null
            ? new GameInstallation(definition, config.GamePath)
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

        try
        {
            Console.WriteLine($"Exporting scenario to {outputPath} ...");
            var exportError = ReplayExporter.Export(scenario, map, outputPath);
            if (exportError != null)
            {
                Console.Error.WriteLine($"Export failed: {exportError}");
                Environment.Exit(1);
                return;
            }

            if (config.Headless)
            {
                RunReplay(installation, config, outputPath);
            }
            else
            {
                Platform.Start();
                try
                {
                    RunReplay(installation, config, outputPath);
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
        }
    }

    private static void RunReplay(GameInstallation installation, TestHarnessConfig config, string replayPath)
    {
        if (config.Headless)
        {
            RunHeadless(installation, config, replayPath);
        }
        else
        {
            RunWindowed(installation, config, replayPath);
        }
    }

    private static void RunWindowed(GameInstallation installation, TestHarnessConfig config, string replayPath)
    {
        using var window = new GameWindow("OpenSAGE - TestHarness", 100, 100, 1024, 768, config.Fullscreen);
        using var game = new Game(installation, config.Renderer, new Configuration { NoAudio = true }, window);
        using var textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription);

        using var repFileSystem = new DiskFileSystem(Path.GetDirectoryName(replayPath)!);
        var repEntry = repFileSystem.GetFile(Path.GetFileName(replayPath));

        game.LoadReplayFile(repEntry);
        game.StartRun();

        using var monitor = new ReplayActionMonitor(game);
        monitor.ActionDispatched += (_, e) =>
            Console.WriteLine($"  [dispatched] {e.ActionType,-20} player: {e.Player?.Name ?? "?"}");
        monitor.ActionCompleted += (_, e) =>
            Console.WriteLine($"  [completed]  {e.ActionType,-20} player: {e.Player?.Name ?? "?"}");

        Console.WriteLine("Running replay — close the window or wait for it to finish.");

        while (game.IsRunning)
        {
            if (!window.PumpEvents())
            {
                break;
            }

            game.Update(window.MessageQueue);
            game.Panel.EnsureFrame(window.ClientBounds);
            game.Render();
            textureCopier.Execute(
                game.Panel.Framebuffer.ColorTargets[0].Target,
                window.Swapchain.Framebuffer);
            window.MessageQueue.Clear();
            game.GraphicsDevice.SwapBuffers(window.Swapchain);
        }

        Console.WriteLine("Done.");
    }

    private static void RunHeadless(GameInstallation installation, TestHarnessConfig config, string replayPath)
    {
        using var game = new Game(installation, config.Renderer, new Configuration { NoAudio = true }, null);

        using var repFileSystem = new DiskFileSystem(Path.GetDirectoryName(replayPath)!);
        var repEntry = repFileSystem.GetFile(Path.GetFileName(replayPath));

        game.LoadReplayFile(repEntry);
        game.StartRun();

        var capture = new FrameCaptureService();

        using var monitor = new ReplayActionMonitor(game);
        monitor.ActionDispatched += (_, e) =>
            Console.WriteLine($"  [dispatched] {e.ActionType,-20} player: {e.Player?.Name ?? "?"}");
        monitor.ActionCompleted += (_, e) =>
        {
            Console.WriteLine($"  [completed]  {e.ActionType,-20} player: {e.Player?.Name ?? "?"}");

            // Schedule a capture after every completed action so agents can verify
            // the visual state immediately after each game event.
            // The [CAPTURE] log line is emitted after the next render (FlushPending below).
            capture.ScheduleCapture(
                $"Action '{e.ActionType}' completed for player '{e.Player?.Name ?? "unknown"}'. " +
                "Terrain should be visible. Units and buildings should be 3-D models (not invisible). " +
                "No solid-colour triangle artefacts. UI chrome should be present at the bottom.");
        };

        Console.WriteLine($"Running headless replay. Captures written to: {FrameCaptureService.CaptureDirectory}");
        Console.WriteLine("Agent: monitor stdout for [CAPTURE] lines; open the PNG; validate; delete the file.");

        var frameRect = new OpenSage.Mathematics.Rectangle(0, 0, 1024, 768);

        while (game.IsRunning)
        {
            game.Update(Array.Empty<OpenSage.Input.InputMessage>());
            game.Panel.EnsureFrame(frameRect);
            game.Render();
            capture.FlushPending(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
        }

        Console.WriteLine("Done.");
    }

    private static ReplayScenario LoadScenario(string? path)
    {
        if (path == null)
        {
            return ReplayScenario.CreateAlpineAssaultUSAvGLA();
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
