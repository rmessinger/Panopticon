using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Panopticon.Tests.Benchmarks;

/// <summary>
/// Builds the B4a proof worker in Release when its DLL is missing, as bounded setup before the
/// watchdog starts the worker. Build output is captured in <see cref="BuildLogFileName"/> under the
/// artifact root. Setup time is outside the stage budgets. Success is decided by exit code and DLL
/// existence; output drain is best effort because grandchild processes can hold the pipes open.
/// </summary>
internal static class B4aWorkerSetup
{
    internal const string BuildLogFileName = "setup-build.log";
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);

    internal sealed record Result(string WorkerDll, TimeSpan? BuildDuration, string? BuildLogPath);

    internal sealed record BuildRun(int ProcessId, bool TimedOut, bool Exited, int? ExitCode, TimeSpan Duration, string LogPath);

    public static Result EnsureWorkerDll(string repoRoot, string workerDllRelativePath, string artifactRoot, TimeSpan buildTimeout, TimeSpan drainTimeout, Func<ProcessStartInfo> buildStart)
    {
        var workerDll = Path.Combine(repoRoot, workerDllRelativePath);
        if (File.Exists(workerDll))
        {
            return new Result(workerDll, null, null);
        }

        var logPath = Path.Combine(artifactRoot, BuildLogFileName);
        var run = RunBuild(buildStart(), logPath, buildTimeout, drainTimeout);
        if (run.TimedOut)
        {
            throw new InvalidOperationException(
                $"B4a worker setup timeout: Release build did not exit within {buildTimeout.TotalSeconds:F1} s. " +
                $"Owned build process {run.ProcessId} was killed (exited: {run.Exited}). Setup is outside the stage budgets. Build log: {logPath}.");
        }

        if (run.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"B4a worker setup failed: Release build exited {run.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} " +
                $"after {run.Duration.TotalSeconds:F1} s. Build log: {logPath}.");
        }

        if (!File.Exists(workerDll))
        {
            throw new InvalidOperationException(
                $"B4a worker setup failed: Release build exited 0 but the worker DLL is missing at {workerDll}. Build log: {logPath}.");
        }

        return new Result(workerDll, run.Duration, logPath);
    }

    public static BuildRun RunBuild(ProcessStartInfo start, string logPath, TimeSpan timeout, TimeSpan drainTimeout)
    {
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        using var log = new StreamWriter(logPath, append: false);
        using var streamsDrained = new CountdownEvent(2);
        var gate = new object();
        var logOpen = true;
        DataReceivedEventHandler append = (_, e) =>
        {
            lock (gate)
            {
                if (!logOpen)
                {
                    return;
                }

                if (e.Data is null)
                {
                    streamsDrained.Signal();
                }
                else
                {
                    try
                    {
                        log.WriteLine(e.Data);
                    }
                    catch (IOException)
                    {
                        // Log is best effort; a write error here would otherwise fault the reader thread.
                    }
                }
            }
        };

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += append;
        process.ErrorDataReceived += append;
        var started = Stopwatch.GetTimestamp();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"B4a worker setup failed: could not start the Release build ({ex.Message}). Build log: {logPath}.", ex);
        }

        var pid = process.Id;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = !process.WaitForExit(timeout);
        var duration = Stopwatch.GetElapsedTime(started);
        var exited = true;
        if (timedOut)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited between the timed wait and the kill; the timeout verdict still stands.
            }

            exited = process.WaitForExit(KillWait);
        }

        var drained = streamsDrained.Wait(drainTimeout);
        lock (gate)
        {
            if (!drained)
            {
                log.WriteLine(
                    $"Output drain did not reach end of stream within {drainTimeout.TotalSeconds:F1} s after the build process " +
                    $"{(exited ? "exited" : "was killed")}; this log may be truncated.");
            }

            logOpen = false;
        }

        return new BuildRun(pid, timedOut, exited, exited ? process.ExitCode : (int?)null, duration, logPath);
    }

    public static ProcessStartInfo BuildStartInfo(string repoRoot, string projectRelativePath)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(projectRelativePath);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        return start;
    }
}
