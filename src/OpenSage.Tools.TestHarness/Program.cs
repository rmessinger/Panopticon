using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        Run(config, reporter);
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

        if (config.Scenario == BuiltInScenario.UnitValidationGround)
        {
            var groundPlan = ScenarioBuilder.BuildValidationGroundRun(config);
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
