using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSage.Tools.ReplaySketch.Model.Capture;
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
        ITestHarnessReporter reporter = new ConsoleTestHarnessReporter();

        if (Array.Exists(args, a => a.Equals("--run-assert", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RunAndAssertCommand(args, reporter));
            return;
        }

        var config = LoadConfig(reporter);

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

        var builtInScenario = GetArgValue(args, "--built-in-scenario");
        if (!string.IsNullOrEmpty(builtInScenario))
        {
            if (!Enum.TryParse<BuiltInScenario>(builtInScenario, ignoreCase: true, out var parsed))
            {
                reporter.Error($"Unknown built-in scenario '{builtInScenario}'. Valid values: {string.Join(", ", Enum.GetNames<BuiltInScenario>())}.");
                Environment.Exit(1);
                return;
            }

            config.Scenario = parsed;
        }

        Run(config, reporter);
    }

    /// <summary>
    /// Runs a generic capture profile: <see cref="TestHarnessConfig.ScenarioPath"/> (or
    /// <c>--scenario</c>) holds either a path to a capture profile JSON or the bare filename of an
    /// embedded one (e.g. <c>capture-construction.json</c>).
    /// </summary>
    private static void RunScenarioCapture(TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var installation = ScenarioBuilder.ResolveInstallation(config, reporter, ConfigFileName);
        if (installation == null)
        {
            Environment.Exit(1);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ScenarioPath))
        {
            var embedded = string.Join(", ", CaptureProfileLoader.EmbeddedCaptureProfileNames());
            reporter.Error(
                "ScenarioCapture needs a capture profile: set ScenarioPath (or pass --scenario) to a capture profile JSON file or an embedded profile name.");
            reporter.Error($"Embedded capture profiles: {embedded}");
            Environment.Exit(1);
            return;
        }

        var path = config.ScenarioPath!;
        CaptureProfile profile;
        try
        {
            profile = File.Exists(path)
                ? CaptureProfileLoader.LoadCaptureFromFile(path)
                : CaptureProfileLoader.LoadEmbeddedCapture(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            reporter.Error($"Could not load capture profile '{path}': {ex.Message}");
            Environment.Exit(1);
            return;
        }

        var settings = new CaptureRunSettings(config.Renderer, config.FullHud, null);
        var result = ScenarioCaptureRunner.Run(installation, profile, settings, reporter);
        Environment.ExitCode = result.ExitCode;
    }

    private static int RunAndAssertCommand(string[] args, ITestHarnessReporter reporter)
    {
        var scenarioPath = GetArgValue(args, "--scenario");
        if (string.IsNullOrEmpty(scenarioPath))
        {
            reporter.Error("Missing required --scenario <path.json> argument.");
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
            reporter.Error("Could not locate a Generals installation.");
            reporter.Error(installationService.GetDiagnostics());
            return 1;
        }

        var scenario = ScenarioProfileLoader.LoadFromFile(scenarioPath);
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"opensage_runassert_{Guid.NewGuid():N}");
        try
        {
            var report = HarnessRunner.RunAndAssert(scenario, installation, tempDirectory);
            reporter.Info(report.ToJson());
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

    private static TestHarnessConfig LoadConfig(ITestHarnessReporter reporter)
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(path))
        {
            reporter.Error($"Config file not found: {path}");
            reporter.Error("Creating a default config file.");
            var defaults = new TestHarnessConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TestHarnessConfig>(json, JsonOptions)
               ?? new TestHarnessConfig();
    }

    /// <summary>
    /// Top-level orchestration: construct the scenario (<see cref="ScenarioBuilder"/>), run it
    /// (<see cref="ReplayScenarioRunner"/> / <see cref="ValidationGroundRunner"/>), and report
    /// final status through <paramref name="reporter"/>.
    /// </summary>
    private static void Run(TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var installation = ScenarioBuilder.ResolveInstallation(config, reporter, ConfigFileName);
        if (installation == null)
        {
            Environment.Exit(1);
            return;
        }

        if (config.Scenario == BuiltInScenario.EngagementCapture)
        {
            EngagementCaptureRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.ScenarioCapture)
        {
            RunScenarioCapture(config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.UnitValidationGround)
        {
            var groundPlan = ScenarioBuilder.BuildValidationGroundRun(config, installation, reporter);
            ValidationGroundRunner.Run(installation, config, groundPlan, reporter);
            return;
        }

        var plan = ScenarioBuilder.BuildReplayRun(installation, config, reporter);
        if (plan == null)
        {
            Environment.Exit(1);
            return;
        }

        try
        {
            ReplayScenarioRunner.Run(installation, config, plan, reporter);
        }
        finally
        {
            if (!config.KeepReplay && File.Exists(plan.ReplayPath))
            {
                File.Delete(plan.ReplayPath);
            }
            else if (config.KeepReplay)
            {
                reporter.Info($"Replay saved to: {plan.ReplayPath}");
            }

            reporter.Info($"Action results saved to: {plan.ResultsPath}");
        }
    }
}
