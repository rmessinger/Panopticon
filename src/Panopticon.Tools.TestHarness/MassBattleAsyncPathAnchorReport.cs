using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Async-path scale anchor artifact of a <c>MassBattleCapacityProbe</c> run (WI-B4-06):
/// squad-shaped request bursts issued on a fixed cadence through
/// <see cref="Panopticon.Navigation.Navigation.SubmitPathRequest"/> while the engagement runs,
/// with per-request latency, per-burst backlog, and the queue's deadline accounting. Null on
/// reports of runs made without the anchor enabled.
/// </summary>
public sealed record MassBattleAsyncPathAnchorReport
{
    public int BurstEveryFrames { get; init; }

    public int RequestsPerBurst { get; init; }

    public int AsyncPathFrameBound { get; init; }

    public int Submitted { get; init; }

    public int Applied { get; init; }

    public int Cancelled { get; init; }

    public long DeadlineMisses { get; init; }

    /// <summary>Median, p99, and max wall latency in ms over applied requests.</summary>
    public double MedianWallMs { get; init; }

    public double P99WallMs { get; init; }

    public double MaxWallMs { get; init; }

    /// <summary>Logic frames from submission to application, over applied requests.</summary>
    public double MedianFramesWaited { get; init; }

    public int MaxFramesWaited { get; init; }

    /// <summary>Largest not-yet-terminal backlog observed right after a step.</summary>
    public long MaxOutstanding { get; init; }

    public List<AsyncPathBurstSample> Bursts { get; init; } = new();

    public List<AsyncPathRequestSample> Samples { get; init; } = new();

    public static string WriteText(MassBattleAsyncPathAnchorReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.AppendLine(
            $"async path anchor: every {report.BurstEveryFrames} frames x {report.RequestsPerBurst} requests, bound {report.AsyncPathFrameBound} frames");
        b.AppendLine(
            $"submitted {report.Submitted}, applied {report.Applied}, cancelled {report.Cancelled}, deadline misses {report.DeadlineMisses}");
        b.AppendLine(string.Format(
            culture,
            "latency: median {0:F1} ms, p99 {1:F1} ms, max {2:F1} ms; waited: median {3:F1} frames, max {4} frames; max outstanding {5}",
            report.MedianWallMs,
            report.P99WallMs,
            report.MaxWallMs,
            report.MedianFramesWaited,
            report.MaxFramesWaited,
            report.MaxOutstanding));
        return b.ToString();
    }
}

/// <summary>Per-burst backlog: when the burst's requests began and finished applying.</summary>
public sealed record AsyncPathBurstSample
{
    public int SubmitFrame { get; init; }

    public int FirstAppliedFrame { get; init; }

    public int LastAppliedFrame { get; init; }

    public int AppliedCount { get; init; }

    public long MaxOutstanding { get; init; }
}

/// <summary>One anchor request: submit and application frames, wall latency, and result shape.</summary>
public sealed record AsyncPathRequestSample
{
    public int SubmitFrame { get; init; }

    /// <summary>Frame the request applied; -1 when it never applied (cancelled or run end).</summary>
    public int AppliedFrame { get; init; }

    public double WallMs { get; init; }

    public int Waypoints { get; init; }

    public bool EndIsPassable { get; init; }

    public bool Applied { get; init; }
}
