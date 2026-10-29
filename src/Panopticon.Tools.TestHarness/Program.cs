using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Graphics.Rendering.Instancing;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.TestHarness;

public static class Program
{
    private const string ConfigFileName = "TestHarness.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Main(string[] args)
    {
        ITestHarnessReporter reporter = new ConsoleTestHarnessReporter();

        // External-model mode is fully self-contained: dispatch before ordinary config
        // loading and scenario construction.
        if (Array.Exists(args, a => a.Equals("--external-model-request", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(ExternalModels.ExternalModelHost.RunArguments(
                args,
                reporter,
                new ExternalModels.ExternalModelSceneRenderer()).ExitCode);
            return;
        }

        if (Array.Exists(args, a => a.Equals("--run-assert", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(RunAndAssertCommand(args, reporter));
            return;
        }

        if (Array.Exists(args, a => a.Equals("--list-scenarios", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(ListScenariosJson());
            return;
        }

        TestHarnessConfig config;
        try
        {
            config = LoadConfig(reporter, GetArgValue(args, "--config"));
        }
        catch (FileNotFoundException exception)
        {
            reporter.Error(exception.Message);
            Environment.ExitCode = 1;
            return;
        }

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

        var gltfFixtureRoot = GetArgValue(args, "--gltf-fixture-root");
        if (!string.IsNullOrEmpty(gltfFixtureRoot))
        {
            config.GltfFixtureRoot = gltfFixtureRoot;
        }

        // G9 scale validation: non-unity viewport scale for scenario captures.
        var viewportScale = GetArgValue(args, "--viewport-scale");
        if (!string.IsNullOrEmpty(viewportScale))
        {
            if (!int.TryParse(viewportScale, out var parsedScale) || parsedScale < 1)
            {
                reporter.Error($"--viewport-scale must be a positive integer, got '{viewportScale}'.");
                Environment.Exit(1);
                return;
            }

            config.ViewportScale = parsedScale;
        }

        if (Array.Exists(args, a => a.Equals("--full-hud", StringComparison.OrdinalIgnoreCase)))
        {
            config.FullHud = true;
        }

        // G10 evidence: pin the render clock so separate scenario captures (with and without
        // an actor, or with an option toggled) diff pixel-for-pixel except for the variable.
        if (Array.Exists(args, a => a.Equals("--fixed-render-clock", StringComparison.OrdinalIgnoreCase)))
        {
            config.FixedRenderClock = true;
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

        // B5 render-scale probe overrides (see RenderingScaleProbeRunner).
        ParseOptionalInt(args, "--b5-unit-count", value => config.RenderingScaleProbeUnitCount = value);
        ParseOptionalString(args, "--b5-definition", value => config.RenderingScaleProbeDefinition = value);
        ParseOptionalString(args, "--b5-map", value => config.RenderingScaleProbeMapPath = value);
        ParseOptionalInt(args, "--b5-warmup", value => config.RenderingScaleProbeWarmupFrames = value);
        ParseOptionalInt(args, "--b5-measured", value => config.RenderingScaleProbeMeasuredFrames = value);
        ParseOptionalString(args, "--b5-report-dir", value => config.RenderingScaleProbeReportDirectory = value);
        ParseOptionalString(args, "--b5-screenshot", value => config.RenderingScaleProbeScreenshotPath = value);

        if (Array.Exists(args, a => a.Equals("--b5-no-instancing", StringComparison.OrdinalIgnoreCase)))
        {
            RenderScaleOptions.Instancing = false;
        }

        if (Array.Exists(args, a => a.Equals("--b5-no-lod", StringComparison.OrdinalIgnoreCase)))
        {
            RenderScaleOptions.SmallFeatureCulling = false;
        }

        if (Array.Exists(args, a => a.Equals("--b5-legacy-shadows", StringComparison.OrdinalIgnoreCase)))
        {
            config.RenderingScaleProbeLegacyShadows = true;
        }

        // N0-2 pathfinding-load scenario overrides (see PathfindingLoadRunner).
        ParseOptionalString(args, "--n0-profile", value => config.PathfindingLoadProfilePath = value);
        ParseOptionalString(args, "--n0-report", value => config.PathfindingLoadReportPath = value);
        ParseOptionalInt(args, "--n0-seed", value => config.PathfindingLoadSeed = value);
        var n0Sizes = GetArgValue(args, "--n0-sizes");
        if (!string.IsNullOrEmpty(n0Sizes))
        {
            var parsedSizes = n0Sizes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            config.PathfindingLoadSelectionSizes = new int[parsedSizes.Length];
            for (var i = 0; i < parsedSizes.Length; i++)
            {
                if (!int.TryParse(parsedSizes[i], out var size) || size < 1)
                {
                    throw new InvalidOperationException($"'--n0-sizes' expects positive integers, got '{n0Sizes}'.");
                }

                config.PathfindingLoadSelectionSizes[i] = size;
            }
        }

        var n0GroupMoves = GetArgValue(args, "--n0-group-moves");
        if (!string.IsNullOrEmpty(n0GroupMoves))
        {
            config.PathfindingLoadGroupMoves = bool.Parse(n0GroupMoves);
        }

        ParseOptionalInt(args, "--n0-cluster-cells", value => config.PathfindingLoadClusterCells = value);

        var n0AsyncOrderPaths = GetArgValue(args, "--n0-async-order-paths");
        if (!string.IsNullOrEmpty(n0AsyncOrderPaths))
        {
            config.PathfindingLoadAsyncOrderPaths = bool.Parse(n0AsyncOrderPaths);
        }

        var probeAsyncOrderPaths = GetArgValue(args, "--probe-async-order-paths");
        if (!string.IsNullOrEmpty(probeAsyncOrderPaths))
        {
            config.MassBattleCapacityProbeAsyncOrderPaths = bool.Parse(probeAsyncOrderPaths);
        }

        var n0ValidateOffsets = GetArgValue(args, "--n0-validate-offsets");
        if (!string.IsNullOrEmpty(n0ValidateOffsets))
        {
            config.PathfindingLoadValidateOffsetRoutes = bool.Parse(n0ValidateOffsets);
        }

        var probeValidateOffsets = GetArgValue(args, "--probe-validate-offsets");
        if (!string.IsNullOrEmpty(probeValidateOffsets))
        {
            config.MassBattleCapacityProbeValidateOffsetRoutes = bool.Parse(probeValidateOffsets);
        }

        var probeBlockedResolution = GetArgValue(args, "--probe-blocked-resolution");
        if (!string.IsNullOrEmpty(probeBlockedResolution))
        {
            config.MassBattleCapacityProbeBlockedResolution = bool.Parse(probeBlockedResolution);
        }

        ParseOptionalInt(args, "--probe-seed", value => config.MassBattleCapacityProbeSeed = value);

        var n6ValidateOffsets = GetArgValue(args, "--n6-validate-offsets");
        if (!string.IsNullOrEmpty(n6ValidateOffsets))
        {
            config.NavigationDeadlockValidateOffsetRoutes = bool.Parse(n6ValidateOffsets);
        }

        var n6BlockedResolution = GetArgValue(args, "--n6-blocked-resolution");
        if (!string.IsNullOrEmpty(n6BlockedResolution))
        {
            config.NavigationDeadlockBlockedResolution = bool.Parse(n6BlockedResolution);
        }

        var n0DestinationSlots = GetArgValue(args, "--n0-destination-slots");
        if (!string.IsNullOrEmpty(n0DestinationSlots))
        {
            config.PathfindingLoadDestinationSlots = bool.Parse(n0DestinationSlots);
        }

        var probeDestinationSlots = GetArgValue(args, "--probe-destination-slots");
        if (!string.IsNullOrEmpty(probeDestinationSlots))
        {
            config.MassBattleCapacityProbeDestinationSlots = bool.Parse(probeDestinationSlots);
        }

        var n6DestinationSlots = GetArgValue(args, "--n6-destination-slots");
        if (!string.IsNullOrEmpty(n6DestinationSlots))
        {
            config.NavigationDeadlockDestinationSlots = bool.Parse(n6DestinationSlots);
        }

        ParseOptionalString(args, "--n6-cases", value => config.NavigationDeadlockCaseFilter = value);
        ParseOptionalInt(args, "--n6-budget", value => config.NavigationDeadlockFrameBudget = value);
        ParseOptionalInt(args, "--n6-seed", value => config.NavigationDeadlockSeed = value);
        ParseOptionalString(args, "--n6-capture-dir", value => config.NavigationDeadlockCaptureDirectory = value);
        ParseOptionalInt(args, "--n6-capture-stride", value => config.NavigationDeadlockCaptureStride = value);
        ParseOptionalString(args, "--n6-report", value => config.NavigationDeadlockReportPath = value);

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

        var settings = new CaptureRunSettings(
            config.Renderer,
            config.FullHud,
            null,
            ViewportScale: config.ViewportScale,
            FixedRenderClock: config.FixedRenderClock);
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
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"panopticon_runassert_{Guid.NewGuid():N}");
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

    private static void ParseOptionalInt(string[] args, string name, Action<int> apply)
    {
        var value = GetArgValue(args, name);
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 1)
        {
            throw new InvalidOperationException($"'{name}' expects a positive integer, got '{value}'.");
        }

        apply(parsed);
    }

    private static void ParseOptionalString(string[] args, string name, Action<string> apply)
    {
        var value = GetArgValue(args, name);
        if (!string.IsNullOrEmpty(value))
        {
            apply(value!);
        }
    }

    internal static string ListScenariosJson()
    {
        return HarnessCatalogJson.Serialize(HarnessScenarioCatalog.Build());
    }

    internal static TestHarnessConfig LoadConfig(ITestHarnessReporter reporter, string? configPath = null)
    {
        var path = configPath ?? Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(path))
        {
            if (configPath is not null)
            {
                throw new FileNotFoundException($"Config file not found: {path}", path);
            }

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

        if (config.Scenario == BuiltInScenario.TerrainTextureBake)
        {
            Environment.ExitCode = TerrainTextureBakeRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.TerrainZoomProof)
        {
            Environment.ExitCode = TerrainZoomProofRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.VisualRegression)
        {
            Environment.ExitCode = VisualRegressionRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.UnitValidationGround)
        {
            var groundPlan = ScenarioBuilder.BuildValidationGroundRun(config, installation, reporter);
            ValidationGroundRunner.Run(installation, config, groundPlan, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.MassBattleDeployment)
        {
            Environment.ExitCode = MassBattleDeploymentRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.MassBattleCapacityProbe)
        {
            Environment.ExitCode = MassBattleCapacityProbeRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.MassBattle)
        {
            Environment.ExitCode = MassBattleRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.MassBattleBatch)
        {
            Environment.ExitCode = MassBattleBatchRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.RoutingChoke)
        {
            Environment.ExitCode = RoutingChokeRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.PathfindingLoad)
        {
            Environment.ExitCode = PathfindingLoadRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.NavigationDeadlock)
        {
            Environment.ExitCode = NavigationDeadlockRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.RenderingScaleProbe)
        {
            Environment.ExitCode = RenderingScaleProbeRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.PhysicsStack)
        {
            Environment.ExitCode = PhysicsStackRunner.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.PbrMaterialProof)
        {
            Environment.ExitCode = PbrMaterialProofScenario.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.GltfRuntimeProof)
        {
            Environment.ExitCode = GltfRuntimeProofScenario.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.Aoe3GrannyProof)
        {
            Environment.ExitCode = Aoe3GrannyProofScenario.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.Ee2NifProof)
        {
            Environment.ExitCode = Ee2NifProofScenario.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.Ee2PropertyProof)
        {
            Environment.ExitCode = Ee2NifProofScenario.RunPropertyProof(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.Ee2ScaleProof)
        {
            Environment.ExitCode = Ee2NifProofScenario.RunScaleProof(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.Ee2MorphProof)
        {
            Environment.ExitCode = Ee2NifProofScenario.RunMorphProof(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.Ee2ParticleProof)
        {
            Environment.ExitCode = Ee2ParticleProofScenario.Run(installation, config, reporter);
            return;
        }

        if (config.Scenario == BuiltInScenario.Ee2LegacyKfProof)
        {
            Environment.ExitCode = Ee2NifProofScenario.RunLegacyKfProof(installation, config, reporter);
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
