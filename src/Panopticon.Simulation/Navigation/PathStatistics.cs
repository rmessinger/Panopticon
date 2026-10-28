using System;
using System.Collections.Generic;
using System.Threading;

namespace Panopticon.Navigation;

/// <summary>
/// Who asked for a path (WI-N0-01): per-origin request counts attribute path load to its
/// gameplay source; the sum over the synchronous origins equals the delta of the global
/// <see cref="Navigation.PathRequestCount"/>.
/// </summary>
public enum PathRequestOrigin
{
    Unit,
    SquadLeader,
    BlockedFollower,
    Horde,
    Async,

    /// <summary>Bounded local detours issued by the blocked-unit resolver (WI-N8-01).</summary>
    BlockedRepath,
}

/// <summary>
/// Immutable snapshot of the navigation statistic accumulators (WI-N0-01): per-origin request
/// counts, search-volume counters, and the async worker's search time. Taken as a before/after
/// pair around a logic tick to derive per-tick deltas; the async worker is a single thread, so
/// its counters lag the logic thread by at most one in-flight request.
/// </summary>
public sealed class PathStatistics
{
    /// <summary>Goal cells are bucketed into squares of this many cells per side for the
    /// shared-destination histogram (WI-N3-01).</summary>
    public const int GoalNeighbourhoodCells = 8;

    public long UnitRequests { get; }
    public long SquadLeaderRequests { get; }
    public long BlockedFollowerRequests { get; }
    public long HordeRequests { get; }
    public long BlockedRepathRequests { get; }
    public long AsyncRequests { get; }
    public long NodesExpanded { get; }
    public long FailedSearches { get; }
    public long EndpointAdjustments { get; }

    /// <summary>Blocked followers that landed through the bounded ring search and issued no
    /// search (WI-N2-01).</summary>
    public long LandedFollowers { get; }

    /// <summary>Blocked followers whose landing failed (other region or ring bound) and paid
    /// for their own full search (WI-N2-01); equals the BlockedFollowerRequests delta.</summary>
    public long FallbackFollowers { get; }

    /// <summary>Followers whose formation-offset retrace crossed impassable cells and were
    /// rerouted with <see cref="NavigationOptions.ValidateOffsetRoutes"/> on: landed on the
    /// leader route or handed their own search (WI-N7-02).</summary>
    public long ReroutedFollowers { get; }

    /// <summary>Worker-thread ComputePath time in milliseconds; logic-thread time is reported
    /// as the stepper's nested path span instead.</summary>
    public double WorkerSearchMs { get; }

    /// <summary>Full searches recorded since the last goal-histogram reset (WI-N3-01): every
    /// ComputePath that reaches Graph.Search, sync or async.</summary>
    public long GoalHistogramSearches { get; }

    /// <summary>Largest number of full searches sharing one goal neighbourhood since the last
    /// goal-histogram reset (WI-N3-01); a per-window gauge, not a cumulative counter.</summary>
    public long GoalHistogramMaxBucket { get; }

    public PathStatistics(
        long unitRequests,
        long squadLeaderRequests,
        long blockedFollowerRequests,
        long hordeRequests,
        long blockedRepathRequests,
        long asyncRequests,
        long nodesExpanded,
        long failedSearches,
        long endpointAdjustments,
        long landedFollowers,
        long fallbackFollowers,
        long reroutedFollowers,
        double workerSearchMs,
        long goalHistogramSearches,
        long goalHistogramMaxBucket)
    {
        UnitRequests = unitRequests;
        SquadLeaderRequests = squadLeaderRequests;
        BlockedFollowerRequests = blockedFollowerRequests;
        HordeRequests = hordeRequests;
        BlockedRepathRequests = blockedRepathRequests;
        AsyncRequests = asyncRequests;
        NodesExpanded = nodesExpanded;
        FailedSearches = failedSearches;
        EndpointAdjustments = endpointAdjustments;
        LandedFollowers = landedFollowers;
        FallbackFollowers = fallbackFollowers;
        ReroutedFollowers = reroutedFollowers;
        WorkerSearchMs = workerSearchMs;
        GoalHistogramSearches = goalHistogramSearches;
        GoalHistogramMaxBucket = goalHistogramMaxBucket;
    }

    public long SynchronousRequests => UnitRequests + SquadLeaderRequests + BlockedFollowerRequests
        + HordeRequests + BlockedRepathRequests;

    public long RequestsFor(PathRequestOrigin origin) => origin switch
    {
        PathRequestOrigin.Unit => UnitRequests,
        PathRequestOrigin.SquadLeader => SquadLeaderRequests,
        PathRequestOrigin.BlockedFollower => BlockedFollowerRequests,
        PathRequestOrigin.Horde => HordeRequests,
        PathRequestOrigin.BlockedRepath => BlockedRepathRequests,
        PathRequestOrigin.Async => AsyncRequests,
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };
}

/// <summary>
/// The mutable accumulator side of <see cref="PathStatistics"/> (WI-N0-01), owned by
/// <see cref="Navigation"/>. Sync counters are written on the logic thread; the async worker
/// writes its request count and search time, so every field uses interlocked access.
/// </summary>
internal sealed class PathStatisticsAccumulator
{
    private long _unitRequests;
    private long _squadLeaderRequests;
    private long _blockedFollowerRequests;
    private long _hordeRequests;
    private long _blockedRepathRequests;
    private long _asyncRequests;
    private long _nodesExpanded;
    private long _failedSearches;
    private long _endpointAdjustments;
    private long _landedFollowers;
    private long _fallbackFollowers;
    private long _reroutedFollowers;
    private long _workerSearchTicks;

    // WI-N3-01: goal histogram window. Buckets key goal cells into GoalNeighbourhoodCells
    // squares; the harness resets the window per tick, so max-bucket readings are per tick.
    private readonly object _goalGate = new();
    private Dictionary<long, long>? _goalHistogram;
    private long _goalHistogramSearches;
    private long _goalHistogramMaxBucket;

    public void AddRequest(PathRequestOrigin origin)
    {
        switch (origin)
        {
            case PathRequestOrigin.Unit:
                Interlocked.Increment(ref _unitRequests);
                break;
            case PathRequestOrigin.SquadLeader:
                Interlocked.Increment(ref _squadLeaderRequests);
                break;
            case PathRequestOrigin.BlockedFollower:
                Interlocked.Increment(ref _blockedFollowerRequests);
                break;
            case PathRequestOrigin.Horde:
                Interlocked.Increment(ref _hordeRequests);
                break;
            case PathRequestOrigin.BlockedRepath:
                Interlocked.Increment(ref _blockedRepathRequests);
                break;
            case PathRequestOrigin.Async:
                Interlocked.Increment(ref _asyncRequests);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }
    }

    public void AddNodesExpanded(long count) => Interlocked.Add(ref _nodesExpanded, count);

    public void AddFailedSearch() => Interlocked.Increment(ref _failedSearches);

    public void AddEndpointAdjustment() => Interlocked.Increment(ref _endpointAdjustments);

    public void AddLandedFollower() => Interlocked.Increment(ref _landedFollowers);

    public void AddFallbackFollower() => Interlocked.Increment(ref _fallbackFollowers);

    public void AddReroutedFollower() => Interlocked.Increment(ref _reroutedFollowers);

    public void AddWorkerSearchTicks(long stopwatchTicks) => Interlocked.Add(ref _workerSearchTicks, stopwatchTicks);

    public void RecordFullSearchGoal(long bucketKey)
    {
        Interlocked.Increment(ref _goalHistogramSearches);
        lock (_goalGate)
        {
            _goalHistogram ??= new Dictionary<long, long>();
            _goalHistogram.TryGetValue(bucketKey, out var count);
            count++;
            _goalHistogram[bucketKey] = count;
            if (count > _goalHistogramMaxBucket)
            {
                _goalHistogramMaxBucket = count;
            }
        }
    }

    public void ResetGoalHistogram()
    {
        Interlocked.Exchange(ref _goalHistogramSearches, 0);
        Interlocked.Exchange(ref _goalHistogramMaxBucket, 0);
        lock (_goalGate)
        {
            _goalHistogram?.Clear();
        }
    }

    public PathStatistics Snapshot() => new(
        Interlocked.Read(ref _unitRequests),
        Interlocked.Read(ref _squadLeaderRequests),
        Interlocked.Read(ref _blockedFollowerRequests),
        Interlocked.Read(ref _hordeRequests),
        Interlocked.Read(ref _blockedRepathRequests),
        Interlocked.Read(ref _asyncRequests),
        Interlocked.Read(ref _nodesExpanded),
        Interlocked.Read(ref _failedSearches),
        Interlocked.Read(ref _endpointAdjustments),
        Interlocked.Read(ref _landedFollowers),
        Interlocked.Read(ref _fallbackFollowers),
        Interlocked.Read(ref _reroutedFollowers),
        Interlocked.Read(ref _workerSearchTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
        Interlocked.Read(ref _goalHistogramSearches),
        Interlocked.Read(ref _goalHistogramMaxBucket));
}
