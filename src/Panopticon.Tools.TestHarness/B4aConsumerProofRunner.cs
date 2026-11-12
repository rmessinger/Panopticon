using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Panopticon.Benchmarks.Simulation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// B4a-G18 consumer proof worker. Runs <see cref="B4aConsumerBenchmark"/> over the WorldBuilder play
/// runtime and writes evidence under a unique per-run directory. Stage markers are written atomically
/// at each stage start for the outer watchdog. Exit codes: 0 passed, 1 verification failed,
/// 2 stage budget overrun, 3 setup failed, 4 error. The fake worker exists only for watchdog tests.
/// </summary>
internal static class B4aConsumerProofRunner
{
    internal const string ProofFlag = "--b4a-consumer-proof";
    internal const string FakeWorkerFlag = "--b4a-fake-worker";
    internal const string StageMarkerFileName = "stage-marker.json";
    internal const string WorkerResultFileName = "worker-result.json";
    internal const int ExitPassed = 0;
    internal const int ExitVerificationFailed = 1;
    internal const int ExitGuardFailed = 2;
    internal const int ExitSetupFailed = 3;
    internal const int ExitError = 4;

    private const string TestHarnessConfigFileName = "TestHarness.json";
    private const string AuthorStage = "b4a.author";
    private const string FakeStage = "fake.stage";
    private const int FakeAllocBlockBytes = 16 * 1024 * 1024;
    private const long FakeAllocCapBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan StageBudget = TimeSpan.FromSeconds(120);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static int Dispatch(string[] args, ITestHarnessReporter reporter)
    {
        return Array.Exists(args, a => a.Equals(FakeWorkerFlag, StringComparison.OrdinalIgnoreCase))
            ? RunFakeWorker(args)
            : RunProof(args, reporter);
    }

    private static int RunProof(string[] args, ITestHarnessReporter reporter)
    {
        var scale = ReadOption(args, "--scale") ?? "full";
        var plan = scale switch
        {
            "full" => B4aConsumerPlan.FullScale,
            "smoke" => B4aConsumerPlan.SmokeScale,
            _ => null,
        };
        if (plan is null)
        {
            reporter.Error($"--scale must be 'smoke' or 'full', got '{scale}'.");
            return ExitSetupFailed;
        }

        TestHarnessConfig config;
        try
        {
            config = Program.LoadConfig(reporter);
        }
        catch (FileNotFoundException exception)
        {
            reporter.Error(exception.Message);
            return ExitSetupFailed;
        }

        var installation = ScenarioBuilder.ResolveInstallation(config, reporter, TestHarnessConfigFileName);
        if (installation is null)
        {
            reporter.Error("B4a consumer proof aborted: no Generals installation was found.");
            return ExitSetupFailed;
        }

        var artifactRoot = Path.GetFullPath(ReadOption(args, "--artifact-root") ?? Path.GetTempPath());
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var artifactDirectory = Path.Combine(artifactRoot, $"b4a-consumer-proof-{stamp}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactDirectory);
        reporter.Info($"B4a consumer proof scale: {scale} ({plan.ObjectCount} objects, {plan.PlayFrames} play frames, {plan.Repeats} repeats).");
        reporter.Info($"B4a consumer proof artifacts: {artifactDirectory}");

        var stageMarkers = new List<StageMarker>();
        var repeat = -1;
        void OnStage(string stage)
        {
            if (stage == AuthorStage)
            {
                repeat++;
            }

            stageMarkers.Add(WriteStageMarker(artifactDirectory, stage, repeat < 0 ? null : repeat));
        }

        var runtime = new B4aWorldBuilderPlayRuntime(installation, Path.Combine(artifactDirectory, "captures"), OnStage);
        var failingChecks = new List<string>();
        var guardFailures = new List<string>();
        string? error = null;
        try
        {
            var run = B4aConsumerBenchmark.Run(plan, runtime, OnStage);
            var recordJsonPath = Path.Combine(artifactDirectory, "b4a-consumer-record.json");
            var summaryPath = Path.Combine(artifactDirectory, "stage-summary.json");
            BenchmarkReportWriter.WriteJson(run.Record, recordJsonPath);
            BenchmarkReportWriter.WriteCsv(run.Record, Path.Combine(artifactDirectory, "b4a-consumer-record.csv"));
            B4aStageSummaryJson.WriteTo(run, plan, summaryPath);

            failingChecks.AddRange(FailingChecks(summaryPath, run, runtime.Runs, plan));
            guardFailures.AddRange(BudgetOverruns(run, runtime.Runs));

            var frames = runtime.Runs[0].CapturePaths;
            var gifPath = Path.Combine(artifactDirectory, "play-repeat-0.gif");
            AssembleGif(frames, gifPath);
            if (ReadOption(args, "--capture-root") is { } captureRoot)
            {
                PublishCaptures(captureRoot, gifPath, frames, reporter);
            }

            reporter.Info($"B4a consumer proof record: {recordJsonPath}");
            reporter.Info($"B4a consumer proof stage summary: {summaryPath}");
            reporter.Info($"B4a consumer proof GIF: {gifPath}");
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            reporter.Error($"B4a consumer proof failed: {exception.Message}");
        }

        var outcome = error is not null ? "Error"
            : guardFailures.Count > 0 ? "GuardFail"
            : failingChecks.Count > 0 ? "VerificationFailed"
            : "Passed";
        var exitCode = outcome switch
        {
            "Passed" => ExitPassed,
            "VerificationFailed" => ExitVerificationFailed,
            "GuardFail" => ExitGuardFailed,
            _ => ExitError,
        };

        var result = new WorkerResult(
            outcome,
            exitCode,
            scale,
            plan.PlayFrames,
            artifactDirectory,
            failingChecks,
            guardFailures,
            stageMarkers,
            runtime.Runs,
            error);
        File.WriteAllText(Path.Combine(artifactDirectory, WorkerResultFileName), JsonSerializer.Serialize(result, JsonOptions));

        reporter.Info($"B4a consumer proof outcome: {outcome} (exit {exitCode}).");
        foreach (var check in failingChecks)
        {
            reporter.Error($"Failing check: {check}");
        }

        foreach (var overrun in guardFailures)
        {
            reporter.Error($"Stage budget overrun: {overrun}");
        }

        return exitCode;
    }

    private static List<string> FailingChecks(
        string summaryPath,
        B4aConsumerRun run,
        IReadOnlyList<B4aWorldBuilderPlayRuntime.RunInfo> playRuns,
        B4aConsumerPlan plan)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
        var failing = new List<string>();
        foreach (var check in document.RootElement.GetProperty("verification").EnumerateObject())
        {
            if (check.Value.ValueKind != JsonValueKind.True)
            {
                failing.Add(check.Name);
            }
        }

        var observations = run.Observations;
        if (observations.Any(o => o.PlayFramesCompleted != plan.PlayFrames))
        {
            failing.Add("playFramesCompleted");
        }

        if (playRuns.Count != observations.Count)
        {
            failing.Add($"playRunCount (play runs {playRuns.Count}, repeats {observations.Count})");
        }

        for (var repeat = 0; repeat < Math.Min(playRuns.Count, observations.Count); repeat++)
        {
            var scene = playRuns[repeat].LoadedGameObjectCount;
            var package = observations[repeat].LoadedObjectCount;
            if (scene != package)
            {
                failing.Add($"loadedGameObjectCountMatchesPackage (repeat {repeat}: scene {scene}, package {package})");
            }
        }

        return failing;
    }

    // A stage cannot be preempted from inside, so overruns are reported after each stage returns.
    // The outer watchdog is the preemptive guard.
    private static List<string> BudgetOverruns(B4aConsumerRun run, IReadOnlyList<B4aWorldBuilderPlayRuntime.RunInfo> playRuns)
    {
        var budgetMs = StageBudget.TotalMilliseconds;
        var overruns = new List<string>();
        foreach (var stage in run.Record.SystemSamples.GroupBy(s => s.System))
        {
            var worstMs = stage.Max(s => s.SpanMs);
            if (worstMs > budgetMs)
            {
                overruns.Add($"{stage.Key} took {worstMs:F0} ms (budget {budgetMs:F0} ms)");
            }
        }

        foreach (var play in playRuns)
        {
            if (play.DisposeMs > budgetMs)
            {
                overruns.Add($"{B4aWorldBuilderPlayRuntime.CleanupStage} took {play.DisposeMs:F0} ms (budget {budgetMs:F0} ms)");
            }
        }

        return overruns;
    }

    private static void AssembleGif(IReadOnlyList<string> framePaths, string gifPath)
    {
        using var gif = Image.Load<Rgba32>(framePaths[0]);
        for (var f = 1; f < framePaths.Count; f++)
        {
            using var next = Image.Load<Rgba32>(framePaths[f]);
            gif.Frames.AddFrame(next.Frames.RootFrame);
        }

        gif.SaveAsGif(gifPath);
    }

    private static void PublishCaptures(string captureRoot, string gifPath, IReadOnlyList<string> frames, ITestHarnessReporter reporter)
    {
        if (!Directory.Exists(captureRoot))
        {
            throw new DirectoryNotFoundException($"Shared capture root '{captureRoot}' does not exist.");
        }

        var version = 1;
        while (CaptureVersionTaken(captureRoot, CaptureStem(version)))
        {
            version++;
        }

        var stem = CaptureStem(version);
        var framesDirectory = Path.Combine(captureRoot, "frames", stem);
        File.Copy(gifPath, Path.Combine(captureRoot, stem + ".gif"), overwrite: false);
        File.Copy(frames[0], Path.Combine(captureRoot, stem + ".png"), overwrite: false);
        Directory.CreateDirectory(framesDirectory);
        foreach (var frame in frames)
        {
            File.Copy(frame, Path.Combine(framesDirectory, Path.GetFileName(frame)), overwrite: false);
        }

        reporter.Info($"Shared capture GIF: {Path.Combine(captureRoot, stem + ".gif")}");
        reporter.Info($"Shared capture frames dir: {framesDirectory}");
    }

    private static string CaptureStem(int version)
    {
        return $"b4a-g18-play-v{version}";
    }

    private static bool CaptureVersionTaken(string captureRoot, string stem)
    {
        return File.Exists(Path.Combine(captureRoot, stem + ".gif"))
            || File.Exists(Path.Combine(captureRoot, stem + ".png"))
            || Directory.Exists(Path.Combine(captureRoot, "frames", stem));
    }

    private static int RunFakeWorker(string[] args)
    {
        var root = ReadOption(args, "--artifact-root");
        if (root is null)
        {
            Console.Error.WriteLine("--artifact-root is required for the fake worker.");
            return ExitSetupFailed;
        }

        var mode = ReadOption(args, "--mode") ?? "ok";
        var seconds = int.TryParse(ReadOption(args, "--seconds"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        Directory.CreateDirectory(root);
        WriteStageMarker(root, FakeStage, repeat: null);
        switch (mode)
        {
            case "ok":
                return 0;

            case "sleep":
                Thread.Sleep(TimeSpan.FromSeconds(seconds));
                return 0;

            case "alloc":
                return HoldAllocation(seconds);

            default:
                Console.Error.WriteLine($"Unknown fake worker mode '{mode}'.");
                return ExitSetupFailed;
        }
    }

    private static int HoldAllocation(int seconds)
    {
        var blocks = new List<byte[]>();
        for (long allocated = 0; allocated < FakeAllocCapBytes; allocated += FakeAllocBlockBytes)
        {
            var block = new byte[FakeAllocBlockBytes];
            Array.Fill(block, (byte)1);
            blocks.Add(block);
        }

        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        GC.KeepAlive(blocks);
        return 0;
    }

    private static StageMarker WriteStageMarker(string directory, string stage, int? repeat)
    {
        var marker = new StageMarker(
            SchemaVersion: 1,
            Stage: stage,
            Repeat: repeat,
            StartedUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Pid: Environment.ProcessId);

        var path = Path.Combine(directory, StageMarkerFileName);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(marker, JsonOptions));
        File.Move(temp, path, overwrite: true);
        return marker;
    }

    private static string? ReadOption(string[] args, string name)
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

    /// <summary>
    /// Current stage, written to <c>stage-marker.json</c> at each stage start.
    /// <c>StartedUtc</c> is round-trip ("O") UTC; <c>Repeat</c> is null outside the repeat loop.
    /// </summary>
    internal sealed record StageMarker(int SchemaVersion, string Stage, int? Repeat, string StartedUtc, int Pid);

    internal sealed record WorkerResult(
        string Outcome,
        int ExitCode,
        string Scale,
        int PlayFramesPlanned,
        string ArtifactDirectory,
        IReadOnlyList<string> FailingChecks,
        IReadOnlyList<string> GuardFailures,
        IReadOnlyList<StageMarker> StageMarkers,
        IReadOnlyList<B4aWorldBuilderPlayRuntime.RunInfo> PlayRuns,
        string? Error);
}
