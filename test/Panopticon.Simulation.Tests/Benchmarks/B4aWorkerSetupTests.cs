using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Panopticon.Tests.Benchmarks;

/// <summary>
/// Drives <see cref="B4aWorkerSetup"/> with the TestHarness fake worker standing in for the build.
/// The fake worker exits on its own, so these tests need no Generals install.
/// </summary>
public sealed class B4aWorkerSetupTests
{
    private const string WorkerDllRelativePath = "bin/Release/net10.0/Panopticon.Tools.TestHarness.dll";
    private const string WorkerAssemblyName = "Panopticon.Tools.TestHarness.dll";
    private static readonly TimeSpan TinyTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ShortDrain = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PromptReturn = TimeSpan.FromSeconds(15);

    [Fact]
    public void MissingDllAndUnbuildableProject_FailsSetupAndLeavesNoDll()
    {
        using var scratch = new Scratch();
        var logPath = Path.Combine(scratch.Root, B4aWorkerSetup.BuildLogFileName);

        var exception = Assert.Throws<InvalidOperationException>(() => B4aWorkerSetup.EnsureWorkerDll(
            scratch.Root,
            WorkerDllRelativePath,
            scratch.Root,
            GenerousTimeout,
            ShortDrain,
            () => B4aWorkerSetup.BuildStartInfo(scratch.Root, "missing/Missing.csproj")));

        Assert.Contains("B4a worker setup failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains(logPath, exception.Message, StringComparison.Ordinal);
        Assert.True(new FileInfo(logPath).Length > 0, $"Build log {logPath} is empty.");
        Assert.False(File.Exists(Path.Combine(scratch.Root, WorkerDllRelativePath)));
    }

    [Fact]
    public void HangingBuild_FailsSetupAsTimeoutWithLogPath()
    {
        using var scratch = new Scratch();
        var logPath = Path.Combine(scratch.Root, B4aWorkerSetup.BuildLogFileName);

        var exception = Assert.Throws<InvalidOperationException>(() => B4aWorkerSetup.EnsureWorkerDll(
            scratch.Root,
            WorkerDllRelativePath,
            scratch.Root,
            TinyTimeout,
            ShortDrain,
            () => FakeWorker(scratch.Root, "sleep", 60)));

        Assert.Contains("B4a worker setup timeout", exception.Message, StringComparison.Ordinal);
        Assert.Contains(logPath, exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(scratch.Root, WorkerDllRelativePath)));
    }

    [Fact]
    public void HangingBuild_KillsOwnedProcessWithinTimeout()
    {
        using var scratch = new Scratch();
        var logPath = Path.Combine(scratch.Root, B4aWorkerSetup.BuildLogFileName);

        var run = B4aWorkerSetup.RunBuild(FakeWorker(scratch.Root, "sleep", 60), logPath, TinyTimeout, ShortDrain);

        Assert.True(run.TimedOut);
        Assert.True(run.Exited, $"Owned build process {run.ProcessId} did not exit after the kill.");
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(run.ProcessId));
    }

    [Fact]
    public void ZeroExitWithoutDll_FailsAsMissingAfterBuild()
    {
        using var scratch = new Scratch();
        var dll = Path.Combine(scratch.Root, WorkerDllRelativePath);

        var exception = Assert.Throws<InvalidOperationException>(() => B4aWorkerSetup.EnsureWorkerDll(
            scratch.Root,
            WorkerDllRelativePath,
            scratch.Root,
            GenerousTimeout,
            ShortDrain,
            () => FakeWorker(scratch.Root, "ok", 0)));

        Assert.Contains("worker DLL is missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains(dll, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildThatProducesDll_ReturnsDurationAndLogPath()
    {
        using var scratch = new Scratch();
        var dll = Path.Combine(scratch.Root, WorkerDllRelativePath);
        var logPath = Path.Combine(scratch.Root, B4aWorkerSetup.BuildLogFileName);

        var result = B4aWorkerSetup.EnsureWorkerDll(
            scratch.Root,
            WorkerDllRelativePath,
            scratch.Root,
            GenerousTimeout,
            ShortDrain,
            () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
                File.WriteAllText(dll, string.Empty);
                return FakeWorker(scratch.Root, "ok", 0);
            });

        Assert.Equal(dll, result.WorkerDll);
        Assert.NotNull(result.BuildDuration);
        Assert.Equal(logPath, result.BuildLogPath);
        Assert.True(File.Exists(logPath));
    }

    [Fact]
    public void BuildLeavingGrandchildHoldingStdout_ReturnsAfterDrainBound()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var scratch = new Scratch();
        var dll = Path.Combine(scratch.Root, WorkerDllRelativePath);
        var logPath = Path.Combine(scratch.Root, B4aWorkerSetup.BuildLogFileName);
        var elapsed = Stopwatch.StartNew();
        try
        {
            var result = B4aWorkerSetup.EnsureWorkerDll(
                scratch.Root,
                WorkerDllRelativePath,
                scratch.Root,
                GenerousTimeout,
                ShortDrain,
                () =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
                    File.WriteAllText(dll, string.Empty);
                    return LeakingShell();
                });
            elapsed.Stop();

            Assert.Equal(dll, result.WorkerDll);
            Assert.NotNull(result.BuildDuration);
            Assert.True(result.BuildDuration < PromptReturn, $"Build duration {result.BuildDuration} exceeded {PromptReturn}.");
            Assert.True(elapsed.Elapsed < PromptReturn, $"Setup returned after {elapsed.Elapsed}, expected under {PromptReturn}.");
            Assert.Contains("this log may be truncated", File.ReadAllText(logPath), StringComparison.Ordinal);

            var leakedPid = LeakedChildPid(logPath);
            Assert.True(leakedPid is not null, "The build did not report the leaked child pid.");
            Assert.False(Process.GetProcessById(leakedPid!.Value).HasExited, "The leaked child should still be running during setup.");
        }
        finally
        {
            KillLeakedChild(logPath);
        }
    }

    [Fact]
    public void ExistingDll_ReturnsWithoutBuilding()
    {
        using var scratch = new Scratch();
        var dll = Path.Combine(scratch.Root, WorkerDllRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllText(dll, string.Empty);
        var builds = 0;

        var result = B4aWorkerSetup.EnsureWorkerDll(
            scratch.Root,
            WorkerDllRelativePath,
            scratch.Root,
            GenerousTimeout,
            ShortDrain,
            () =>
            {
                builds++;
                return FakeWorker(scratch.Root, "ok", 0);
            });

        Assert.Equal(dll, result.WorkerDll);
        Assert.Null(result.BuildDuration);
        Assert.Null(result.BuildLogPath);
        Assert.Equal(0, builds);
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

    // The shell exits at once while its background sleep inherits stdout, so the pipes never reach EOF.
    private static ProcessStartInfo LeakingShell()
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("sleep 60 & echo $!; exit 0");
        return start;
    }

    private static int? LeakedChildPid(string logPath)
    {
        var firstLine = File.ReadLines(logPath).FirstOrDefault();
        return int.TryParse(firstLine, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : null;
    }

    private static void KillLeakedChild(string logPath)
    {
        if (!File.Exists(logPath) || LeakedChildPid(logPath) is not { } pid)
        {
            return;
        }

        using var child = Process.GetProcessById(pid);
        child.Kill();
    }

    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Root = Path.Combine(Path.GetTempPath(), $"b4a-worker-setup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
