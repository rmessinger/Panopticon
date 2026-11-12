using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Benchmarks;

/// <summary>
/// Installed outer proof for B4a-G18. Runs the TestHarness proof worker at full scale under
/// <see cref="B4aProofWatchdog"/> and checks its worker result. Requires CNC_GENERALS_PATH. A missing
/// Release worker DLL is built as bounded setup; PANOPTICON_B4A_WORKER_DLL overrides the DLL path.
/// </summary>
[Collection("SerialGameTests")]
public sealed class B4aLargeMapInstalledProofTests
{
    private const string InstallPathVariable = "CNC_GENERALS_PATH";
    private const string WorkerDllVariable = "PANOPTICON_B4A_WORKER_DLL";
    private const string CaptureRootVariable = "PANOPTICON_B4A_CAPTURE_ROOT";
    private const string WorkerProjectFile = "src/Panopticon.Tools.TestHarness/Panopticon.Tools.TestHarness.csproj";
    private const string WorkerDllRelativePath = "src/Panopticon.Tools.TestHarness/bin/Release/net10.0/Panopticon.Tools.TestHarness.dll";
    private const string WorkerResultFileName = "worker-result.json";
    private const string StageSummaryFileName = "stage-summary.json";
    private const string StageMarkerFileName = "stage-marker.json";
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly TimeSpan StageLimit = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly ITestOutputHelper _output;

    public B4aLargeMapInstalledProofTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BootSeed_DefaultsToNullWithoutBoot()
    {
        using var renderer = new GameViewportRenderer { Installation = null };

        Assert.Null(renderer.BootSeed);
    }

    [GameFact(SageGame.CncGenerals)]
    public void FullScaleProof_WorkerPassesUnderWatchdog()
    {
        Assert.True(
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(InstallPathVariable)),
            $"{InstallPathVariable} must point to the Generals installation for the B4a installed proof.");

        var artifactRoot = Path.Combine(Path.GetTempPath(), $"b4a-large-map-proof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactRoot);
        _output.WriteLine($"B4a installed proof artifact root: {artifactRoot}");

        var setup = ResolveWorkerDll(artifactRoot);
        _output.WriteLine(setup.BuildDuration is { } buildDuration
            ? $"B4a worker setup built {setup.WorkerDll} (Release) in {buildDuration.TotalSeconds:F1} s, outside the stage budgets; build log {setup.BuildLogPath}."
            : $"B4a worker setup used existing {setup.WorkerDll}.");

        var verdict = B4aProofWatchdog.Run(
            WorkerStartInfo(setup.WorkerDll, artifactRoot),
            artifactRoot,
            new B4aProofWatchdog.Limits(StageLimit, 16 * GiB, PollInterval));

        Assert.True(
            verdict.Outcome == B4aProofWatchdog.Outcome.Completed,
            $"B4a installed proof {verdict.Outcome}: {verdict.Reason}. Last stage marker: '{LastStage(artifactRoot)}'. " +
            $"{WorkerResultSummary(artifactRoot)} Artifacts preserved at {artifactRoot}.");

        var resultPath = Directory.EnumerateFiles(artifactRoot, WorkerResultFileName, SearchOption.AllDirectories).FirstOrDefault();
        Assert.True(resultPath is not null, $"Worker exited 0 without {WorkerResultFileName}. Artifacts preserved at {artifactRoot}.");

        var workerDirectory = Path.GetDirectoryName(resultPath!)!;
        using var result = JsonDocument.Parse(File.ReadAllText(resultPath!));
        var root = result.RootElement;
        var plan = B4aConsumerPlan.FullScale;

        Assert.True(
            root.GetProperty("outcome").GetString() == "Passed",
            $"Worker outcome was '{root.GetProperty("outcome").GetString()}', expected 'Passed'. {WorkerResultSummary(artifactRoot)}");

        var playRuns = root.GetProperty("playRuns").EnumerateArray().ToList();
        Assert.True(
            playRuns.Count == plan.Repeats,
            $"Expected {plan.Repeats} play runs, found {playRuns.Count}. Artifacts preserved at {artifactRoot}.");

        var seeds = playRuns.Select(run => run.GetProperty("bootSeed").GetInt32()).Distinct().ToArray();
        Assert.True(seeds.Length == 1, $"Repeats booted with differing seeds: {string.Join(", ", seeds)}.");

        for (var repeat = 0; repeat < playRuns.Count; repeat++)
        {
            var run = playRuns[repeat];
            Assert.True(
                run.GetProperty("runIndex").GetInt32() == repeat,
                $"Play run {repeat} recorded runIndex {run.GetProperty("runIndex").GetInt32()}.");
            Assert.True(
                run.GetProperty("framesCompleted").GetInt32() == plan.PlayFrames,
                $"Repeat {repeat} logic-frame delta {run.GetProperty("framesCompleted").GetInt32()}, expected {plan.PlayFrames}.");
            var loaded = run.GetProperty("loadedGameObjectCount").GetInt32();
            _output.WriteLine($"Repeat {repeat}: logic-frame delta {plan.PlayFrames}, loaded game objects {loaded}, boot seed {seeds[0]}.");
        }

        Assert.True(
            root.GetProperty("failingChecks").GetArrayLength() == 0,
            $"Worker reported failing checks: {root.GetProperty("failingChecks").GetRawText()}.");
        Assert.True(
            root.GetProperty("guardFailures").GetArrayLength() == 0,
            $"Worker reported guard failures: {root.GetProperty("guardFailures").GetRawText()}.");

        var summaryPath = Path.Combine(workerDirectory, StageSummaryFileName);
        Assert.True(File.Exists(summaryPath), $"Missing {StageSummaryFileName} at {summaryPath}.");
        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
        Assert.True(
            summary.RootElement.GetProperty("repeatCount").GetInt32() == plan.Repeats,
            $"stage-summary repeatCount was {summary.RootElement.GetProperty("repeatCount").GetInt32()}, expected {plan.Repeats}.");
        Assert.True(
            summary.RootElement.GetProperty("repeats").GetArrayLength() == plan.Repeats,
            $"stage-summary repeats array length was {summary.RootElement.GetProperty("repeats").GetArrayLength()}, expected {plan.Repeats}.");

        _output.WriteLine($"B4a installed proof worker result: {resultPath}");
        _output.WriteLine($"B4a installed proof stage summary: {summaryPath}");
    }

    private static B4aWorkerSetup.Result ResolveWorkerDll(string artifactRoot)
    {
        var overridden = Environment.GetEnvironmentVariable(WorkerDllVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            Assert.True(File.Exists(overridden), $"{WorkerDllVariable} points to a missing file: {overridden}");
            return new B4aWorkerSetup.Result(overridden, null, null);
        }

        var repoRoot = FindRepoRoot();
        return B4aWorkerSetup.EnsureWorkerDll(
            repoRoot,
            WorkerDllRelativePath,
            artifactRoot,
            SetupTimeout,
            B4aWorkerSetup.DrainTimeout,
            () => B4aWorkerSetup.BuildStartInfo(repoRoot, WorkerProjectFile));
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, WorkerProjectFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No ancestor of {AppContext.BaseDirectory} contains {WorkerProjectFile}.");
    }

    private static ProcessStartInfo WorkerStartInfo(string workerDll, string artifactRoot)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
        };
        start.ArgumentList.Add(workerDll);
        start.ArgumentList.Add("--b4a-consumer-proof");
        start.ArgumentList.Add("--artifact-root");
        start.ArgumentList.Add(artifactRoot);
        start.ArgumentList.Add("--scale");
        start.ArgumentList.Add("full");
        if (Environment.GetEnvironmentVariable(CaptureRootVariable) is { Length: > 0 } captureRoot)
        {
            start.ArgumentList.Add("--capture-root");
            start.ArgumentList.Add(captureRoot);
        }

        return start;
    }

    private static string LastStage(string root)
    {
        var marker = Directory.EnumerateFiles(root, StageMarkerFileName, SearchOption.AllDirectories).FirstOrDefault();
        if (marker is null)
        {
            return "before first stage marker";
        }

        using var document = JsonDocument.Parse(File.ReadAllText(marker));
        return document.RootElement.GetProperty("stage").GetString() ?? "unknown";
    }

    private static string WorkerResultSummary(string root)
    {
        var path = Directory.EnumerateFiles(root, WorkerResultFileName, SearchOption.AllDirectories).FirstOrDefault();
        if (path is null)
        {
            return $"No {WorkerResultFileName} was written.";
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var element = document.RootElement;
        return $"Worker result {path}: outcome {element.GetProperty("outcome").GetString()}, " +
            $"failingChecks {element.GetProperty("failingChecks").GetRawText()}, " +
            $"guardFailures {element.GetProperty("guardFailures").GetRawText()}, " +
            $"error {element.GetProperty("error").GetRawText()}.";
    }
}
