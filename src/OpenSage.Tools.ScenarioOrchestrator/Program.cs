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

namespace OpenSage.Tools.ScenarioOrchestrator;

public static class Program
{
    private const string ConfigFileName = "ScenarioOrchestrator.json";

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
        Run(config);
    }

    private static ScenarioOrchestratorConfig LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Config file not found: {path}");
            Console.Error.WriteLine("Creating a default config file.");
            var defaults = new ScenarioOrchestratorConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ScenarioOrchestratorConfig>(json, JsonOptions)
               ?? new ScenarioOrchestratorConfig();
    }

    private static void Run(ScenarioOrchestratorConfig config)
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

    private static void RunReplay(GameInstallation installation, ScenarioOrchestratorConfig config, string replayPath)
    {
        using var window = new GameWindow("OpenSAGE - ScenarioOrchestrator", 100, 100, 1024, 768, config.Fullscreen);
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
