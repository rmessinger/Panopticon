using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Tests.Benchmarks;

/// <summary>
/// Outer guard for the B4a proof worker. Kills the worker's process tree when the newest stage marker
/// is older than the stage limit or the tree's resident set exceeds the RSS limit. Tree RSS is read
/// from /proc, so the guard reports zero RSS on non-Linux hosts.
/// </summary>
internal sealed class B4aProofWatchdog
{
    private const string StageMarkerFileName = "stage-marker.json";
    private const int BudgetExitCode = 2;
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions MarkerOptions = new(JsonSerializerDefaults.Web);

    internal enum Outcome
    {
        Completed,
        GuardFail,
        Failed,
    }

    internal sealed record Limits(TimeSpan StageLimit, long TreeRssLimitBytes, TimeSpan PollInterval);

    internal sealed record Verdict(
        Outcome Outcome,
        string Reason,
        int WorkerPid,
        int? ExitCode,
        long PeakTreeRssBytes,
        bool WorkerExited);

    private sealed record Marker(string Stage, DateTimeOffset StartedUtc);

    public static Verdict Run(ProcessStartInfo startInfo, string artifactRoot, Limits limits)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ArgumentNullException.ThrowIfNull(limits);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Watchdog could not start the worker.");
        var spawnedUtc = DateTimeOffset.UtcNow;
        var peakTreeRss = 0L;
        while (true)
        {
            if (process.WaitForExit(limits.PollInterval))
            {
                return process.ExitCode switch
                {
                    0 => new Verdict(Outcome.Completed, "worker exited 0", process.Id, 0, peakTreeRss, true),
                    BudgetExitCode => new Verdict(Outcome.GuardFail, "worker reported a stage budget overrun", process.Id, BudgetExitCode, peakTreeRss, true),
                    var code => new Verdict(Outcome.Failed, $"worker exited {code}", process.Id, code, peakTreeRss, true),
                };
            }

            var (stage, startedUtc) = LatestMarker(artifactRoot) is { } marker
                ? (marker.Stage, marker.StartedUtc)
                : ("before first stage marker", spawnedUtc);
            if (DateTimeOffset.UtcNow - startedUtc > limits.StageLimit)
            {
                return Kill(process, Outcome.GuardFail, $"stage '{stage}' exceeded {limits.StageLimit.TotalSeconds:F0} s", peakTreeRss);
            }

            var rss = TreeResidentBytes(process.Id);
            peakTreeRss = Math.Max(peakTreeRss, rss);
            if (rss > limits.TreeRssLimitBytes)
            {
                return Kill(process, Outcome.GuardFail, $"tree RSS {rss} bytes exceeded {limits.TreeRssLimitBytes} bytes", peakTreeRss);
            }
        }
    }

    private static Verdict Kill(Process process, Outcome outcome, string reason, long peakTreeRss)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited between the poll and the kill; the verdict still reflects the guard decision.
        }

        var exited = process.WaitForExit(KillWait);
        return new Verdict(outcome, reason, process.Id, exited ? process.ExitCode : null, peakTreeRss, exited);
    }

    private static Marker? LatestMarker(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root, StageMarkerFileName, SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<Marker>(File.ReadAllText(path), MarkerOptions))
            .OfType<Marker>()
            .OrderByDescending(marker => marker.StartedUtc)
            .FirstOrDefault();
    }

    private static long TreeResidentBytes(int rootPid)
    {
        var total = 0L;
        var pending = new Stack<int>();
        pending.Push(rootPid);
        while (pending.Count > 0)
        {
            var pid = pending.Pop();
            total += ResidentBytes(pid);
            foreach (var child in ChildPids(pid))
            {
                pending.Push(child);
            }
        }

        return total;
    }

    private static IEnumerable<int> ChildPids(int pid)
    {
        var children = new List<int>();
        try
        {
            foreach (var task in Directory.EnumerateDirectories($"/proc/{pid}/task"))
            {
                var childrenFile = Path.Combine(task, "children");
                if (!File.Exists(childrenFile))
                {
                    continue;
                }

                foreach (var token in File.ReadAllText(childrenFile).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    children.Add(int.Parse(token, CultureInfo.InvariantCulture));
                }
            }
        }
        catch (IOException)
        {
            // The process exited while the tree was being read; its remaining children are gone with it.
        }

        return children;
    }

    private static long ResidentBytes(int pid)
    {
        try
        {
            var line = File.ReadLines($"/proc/{pid}/status").FirstOrDefault(l => l.StartsWith("VmRSS:", StringComparison.Ordinal));
            if (line is null)
            {
                return 0;
            }

            var kilobytes = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            return long.Parse(kilobytes, CultureInfo.InvariantCulture) * 1024;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
