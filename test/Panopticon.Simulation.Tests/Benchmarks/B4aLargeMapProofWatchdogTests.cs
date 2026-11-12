using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Xunit;

namespace Panopticon.Tests.Benchmarks;

/// <summary>
/// Exercises <see cref="B4aProofWatchdog"/> against the TestHarness fake worker. Linux only: the
/// guard reads tree RSS from /proc and the process-tree kill assumes Linux semantics. Non-Linux
/// hosts return early, so these tests pass vacuously there.
/// </summary>
public sealed class B4aLargeMapProofWatchdogTests
{
    private const long MiB = 1024L * 1024;
    private const long GiB = 1024L * MiB;
    private const string WorkerAssemblyName = "Panopticon.Tools.TestHarness.dll";
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GenerousStage = TimeSpan.FromSeconds(120);

    [Fact]
    public void OkWorker_CompletesWithinLimits()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var scratch = new Scratch();
        var verdict = B4aProofWatchdog.Run(
            FakeWorker(scratch.Root, "ok", 0),
            scratch.Root,
            new B4aProofWatchdog.Limits(GenerousStage, 2 * GiB, Poll));

        Assert.Equal(B4aProofWatchdog.Outcome.Completed, verdict.Outcome);
        Assert.Equal(0, verdict.ExitCode);
        Assert.True(verdict.WorkerExited);
    }

    [Fact]
    public void StaleWallClockMarker_DoesNotKillWorkerThatCompletesWithinStageLimit()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var scratch = new Scratch();
        var verdict = B4aProofWatchdog.Run(
            FakeWorker(scratch.Root, "stale-marker", 2),
            scratch.Root,
            new B4aProofWatchdog.Limits(TimeSpan.FromSeconds(15), 2 * GiB, Poll));

        Assert.Equal(B4aProofWatchdog.Outcome.Completed, verdict.Outcome);
        Assert.Equal(0, verdict.ExitCode);
        Assert.True(verdict.WorkerExited);
    }

    [Fact]
    public void SleepWorker_ExceedsStageLimit_GuardFailsAndKillsWorker()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var scratch = new Scratch();
        var verdict = B4aProofWatchdog.Run(
            FakeWorker(scratch.Root, "sleep", 60),
            scratch.Root,
            new B4aProofWatchdog.Limits(TimeSpan.FromSeconds(2), 2 * GiB, Poll));

        Assert.Equal(B4aProofWatchdog.Outcome.GuardFail, verdict.Outcome);
        Assert.Contains("fake.stage", verdict.Reason, StringComparison.Ordinal);
        Assert.True(verdict.WorkerExited);
    }

    [Fact]
    public void AllocWorker_ExceedsTreeRssLimit_GuardFailsAndKillsWorker()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var scratch = new Scratch();
        var limit = 128 * MiB;
        var verdict = B4aProofWatchdog.Run(
            FakeWorker(scratch.Root, "alloc", 60),
            scratch.Root,
            new B4aProofWatchdog.Limits(GenerousStage, limit, Poll));

        Assert.Equal(B4aProofWatchdog.Outcome.GuardFail, verdict.Outcome);
        Assert.Contains("tree RSS", verdict.Reason, StringComparison.Ordinal);
        Assert.True(verdict.PeakTreeRssBytes > limit);
        Assert.True(verdict.WorkerExited);
    }

    [Fact]
    public void AllocWorker_WithinLimits_Completes()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var scratch = new Scratch();
        var verdict = B4aProofWatchdog.Run(
            FakeWorker(scratch.Root, "alloc", 1),
            scratch.Root,
            new B4aProofWatchdog.Limits(GenerousStage, 2 * GiB, Poll));

        Assert.Equal(B4aProofWatchdog.Outcome.Completed, verdict.Outcome);
        Assert.Equal(0, verdict.ExitCode);
        Assert.True(verdict.WorkerExited);
    }

    private static ProcessStartInfo FakeWorker(string root, string mode, int seconds)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, WorkerAssemblyName);
        Assert.True(File.Exists(dll), $"TestHarness worker assembly was not copied to {dll}.");

        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
        };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--b4a-fake-worker");
        start.ArgumentList.Add("--artifact-root");
        start.ArgumentList.Add(root);
        start.ArgumentList.Add("--mode");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("--seconds");
        start.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
        return start;
    }

    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Root = Path.Combine(Path.GetTempPath(), $"b4a-watchdog-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
