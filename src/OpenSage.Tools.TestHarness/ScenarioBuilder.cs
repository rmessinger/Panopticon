using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenSage.Data;
using OpenSage.Mods.BuiltIn;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Constructs the objects a TestHarness run needs before any game loop starts: resolving the game
/// installation, loading/exporting a replay scenario, and building the Unit Validation Ground's
/// spawn plan. Pure construction — no game loop, no rendering. Failures are reported via
/// <see cref="ITestHarnessReporter.Error"/> and signalled with a <see langword="null"/> return so
/// callers can decide how to exit.
/// </summary>
public static class ScenarioBuilder
{
    /// <summary>
    /// Resolves the <see cref="GameInstallation"/> named by <paramref name="config"/>, either from
    /// <see cref="TestHarnessConfig.GamePath"/> or by auto-detection. Returns <see langword="null"/>
    /// (having reported the failure) if none is found.
    /// </summary>
    public static GameInstallation? ResolveInstallation(TestHarnessConfig config, ITestHarnessReporter reporter, string configFileName)
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
            reporter.Error($"Could not find an installation of {definition.DisplayName}.");
            reporter.Error($"Set the {definition.Identifier.ToUpper()}_PATH environment variable or set GamePath in {configFileName}.");
            return null;
        }

        return installation;
    }

    /// <summary>
    /// Loads the configured scenario (built-in or from <see cref="TestHarnessConfig.ScenarioPath"/>),
    /// resolves its map, and exports it to a temporary <c>.rep</c> file. Returns
    /// <see langword="null"/> (having reported the failure) if the scenario fails to load or export.
    /// </summary>
    public static ReplayRunPlan? BuildReplayRun(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var scenario = LoadScenario(config.Scenario, config.ScenarioPath, reporter);
        if (scenario == null)
        {
            return null;
        }

        var map = MapMetadataService.Load(installation, scenario.MapPath);
        var replayPath = Path.Combine(Path.GetTempPath(), $"opensage_scenario_{Guid.NewGuid()}.rep");
        var resultsPath = Path.ChangeExtension(replayPath, null) + ".results.txt";

        reporter.Info($"Exporting scenario to {replayPath} ...");
        var exportError = ReplayExporter.Export(scenario, map, replayPath, out var deferredActions);
        if (exportError != null)
        {
            reporter.Error($"Export failed: {exportError}");
            return null;
        }

        return new ReplayRunPlan(replayPath, resultsPath, deferredActions);
    }

    /// <summary>
    /// Builds the Unit Validation Ground's scenario, spawn plan, and roster player settings.
    /// </summary>
    public static ValidationGroundRunPlan BuildValidationGroundRun(TestHarnessConfig config)
    {
        var scenario = new ValidationGroundScenario
        {
            MapPath = string.IsNullOrEmpty(config.ValidationGroundMapPath)
                ? "maps/alpine assault/alpine assault.map"
                : config.ValidationGroundMapPath,
            SettleFrames = config.ValidationGroundSettleFrames,
            Seed = config.ValidationGroundSeed,
        };
        var plan = ValidationGroundBuilder.Build(scenario);

        // Three competitive slots (USA/China/GLA) become player indices 2/3/4 after neutral/civilian.
        // SideName uses the Faction-prefixed PlayerTemplate name, matching every other call site;
        // the skirmish-AI script path resolves PlayerTemplates by this name (SidesListUtility).
        var playerSettings = new Logic.PlayerSetting[]
        {
            new(0, "FactionAmerica", new Mathematics.ColorRgb(255, 0, 0), 0, Logic.PlayerOwner.EasyAi, "USA"),
            new(1, "FactionChina", new Mathematics.ColorRgb(0, 255, 0), 0, Logic.PlayerOwner.EasyAi, "China"),
            new(2, "FactionGLA", new Mathematics.ColorRgb(0, 0, 255), 0, Logic.PlayerOwner.EasyAi, "GLA"),
        };

        return new ValidationGroundRunPlan(scenario, plan, playerSettings);
    }

    private static ReplayScenario? LoadScenario(BuiltInScenario builtInScenario, string? path, ITestHarnessReporter reporter)
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
            reporter.Error($"Failed to deserialize scenario from '{path}'.");
            return null;
        }

        return scenario;
    }
}
