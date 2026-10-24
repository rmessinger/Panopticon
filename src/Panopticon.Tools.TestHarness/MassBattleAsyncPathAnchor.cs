using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Panopticon.Logic.Object;
using Panopticon.Navigation;
using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-B4-06 async-path scale anchor: submits squad-shaped request bursts on a frame cadence
/// through <see cref="Navigation.SubmitPathRequest"/> while the capacity-probe engagement runs
/// and records per-request latency (submit to drain-applied), per-frame backlog, and the
/// queue's deadline accounting for the hierarchical-pathfinding gate.
/// </summary>
internal sealed class MassBattleAsyncPathAnchor
{
    private readonly NavigationGrid _navigation;
    private readonly List<Request> _requests = new();
    private readonly List<(int Frame, long Backlog, long SubmitAllocBytes)> _bursts = new();
    private long _maxOutstanding;

    public MassBattleAsyncPathAnchor(NavigationGrid navigation, int burstEveryFrames, int requestsPerBurst)
    {
        _navigation = navigation;
        BurstEveryFrames = burstEveryFrames;
        RequestsPerBurst = requestsPerBurst;
    }

    private sealed class Request
    {
        public required int SubmitFrame { get; init; }

        public required long SubmitTimestamp { get; init; }

        public PathRequestHandle? Handle { get; set; }

        public int AppliedFrame { get; set; } = -1;

        public long AppliedTimestamp { get; set; }

        public int Waypoints { get; set; }

        public bool EndIsPassable { get; set; }
    }

    public int BurstEveryFrames { get; }

    public int RequestsPerBurst { get; }

    /// <summary>Frame the next step advances to; callbacks read it as their application frame.</summary>
    public int CurrentSteppingFrame { get; set; }

    public bool ShouldSubmit(int frame) => frame > 0 && frame % BurstEveryFrames == 0;

    public void SubmitBurst(int frame, IReadOnlyList<(GameObject Requester, Vector3 Start, Vector3 End)> requests)
    {
        var burstBacklog = 0L;

        // WI-N1-03: the submit path's own allocation is the before/after metric; the anchor
        // submits between recorder tick windows, so the B1 tick samples never see it.
        var submitAllocBefore = GC.GetAllocatedBytesForCurrentThread();

        foreach (var (requester, start, end) in requests)
        {
            var request = new Request
            {
                SubmitFrame = frame,
                SubmitTimestamp = Stopwatch.GetTimestamp(),
            };
            request.Handle = _navigation.SubmitPathRequest(
                start,
                end,
                requester,
                handle =>
                {
                    // Fires on the logic thread inside the tick's drain; TryGetResult always
                    // succeeds here because the drain applies before invoking the callback.
                    handle.TryGetResult(out var waypoints, out var endIsPassable);
                    request.AppliedFrame = CurrentSteppingFrame;
                    request.AppliedTimestamp = Stopwatch.GetTimestamp();
                    request.Waypoints = waypoints.Count;
                    request.EndIsPassable = endIsPassable;
                });
            _requests.Add(request);
            burstBacklog = _navigation.AsyncPathOutstanding;
        }

        _bursts.Add((frame, burstBacklog, GC.GetAllocatedBytesForCurrentThread() - submitAllocBefore));
    }

    public void AfterStep()
    {
        var outstanding = _navigation.AsyncPathOutstanding;

        if (outstanding > _maxOutstanding)
        {
            _maxOutstanding = outstanding;
        }
    }

    public MassBattleAsyncPathAnchorReport Build()
    {
        var applied = _requests.Where(r => r.AppliedFrame >= 0).ToList();
        var wallMs = applied.Select(r => Milliseconds(r.AppliedTimestamp - r.SubmitTimestamp)).OrderBy(v => v).ToList();
        var framesWaited = applied.Select(r => r.AppliedFrame - r.SubmitFrame).OrderBy(v => v).ToList();

        var bursts = _bursts
            .Select(burst => new AsyncPathBurstSample
            {
                SubmitFrame = burst.Frame,
                FirstAppliedFrame = _requests.Where(r => r.SubmitFrame == burst.Frame && r.AppliedFrame >= 0).Select(r => r.AppliedFrame).DefaultIfEmpty(-1).Min(),
                LastAppliedFrame = _requests.Where(r => r.SubmitFrame == burst.Frame && r.AppliedFrame >= 0).Select(r => r.AppliedFrame).DefaultIfEmpty(-1).Max(),
                AppliedCount = _requests.Count(r => r.SubmitFrame == burst.Frame && r.AppliedFrame >= 0),
                MaxOutstanding = burst.Backlog,
                SubmitAllocBytes = burst.SubmitAllocBytes,
            })
            .ToList();

        var burstAllocs = _bursts.Select(burst => (double)burst.SubmitAllocBytes).OrderBy(v => v).ToList();

        return new MassBattleAsyncPathAnchorReport
        {
            BurstEveryFrames = BurstEveryFrames,
            RequestsPerBurst = RequestsPerBurst,
            AsyncPathFrameBound = _navigation.AsyncPathFrameBound,
            Submitted = _requests.Count,
            Applied = applied.Count,
            Cancelled = _requests.Count - applied.Count,
            DeadlineMisses = _navigation.AsyncPathDeadlineMisses,
            MedianWallMs = Percentile(wallMs, 0.5),
            P99WallMs = Percentile(wallMs, 0.99),
            MaxWallMs = wallMs.Count > 0 ? wallMs[^1] : 0,
            MedianFramesWaited = Percentile(framesWaited.Select(f => (double)f).ToList(), 0.5),
            MaxFramesWaited = framesWaited.Count > 0 ? framesWaited[^1] : 0,
            MaxOutstanding = _maxOutstanding,
            MedianBurstSubmitAllocBytes = Percentile(burstAllocs, 0.5),
            MaxBurstSubmitAllocBytes = burstAllocs.Count > 0 ? burstAllocs[^1] : 0,
            Bursts = bursts,
            Samples = _requests
                .Select(r => new AsyncPathRequestSample
                {
                    SubmitFrame = r.SubmitFrame,
                    AppliedFrame = r.AppliedFrame,
                    WallMs = r.AppliedFrame >= 0 ? Milliseconds(r.AppliedTimestamp - r.SubmitTimestamp) : 0,
                    Waypoints = r.Waypoints,
                    EndIsPassable = r.EndIsPassable,
                    Applied = r.AppliedFrame >= 0,
                })
                .ToList(),
        };
    }

    private static double Milliseconds(long duration) => duration * 1000.0 / Stopwatch.Frequency;

    private static double Percentile(List<double> sorted, double q)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Clamp(Math.Ceiling(q * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }
}
