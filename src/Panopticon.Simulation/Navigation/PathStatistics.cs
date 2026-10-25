using System;
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
}

/// <summary>
/// Immutable snapshot of the navigation statistic accumulators (WI-N0-01): per-origin request
/// counts, search-volume counters, and the async worker's search time. Taken as a before/after
/// pair around a logic tick to derive per-tick deltas; the async worker is a single thread, so
/// its counters lag the logic thread by at most one in-flight request.
/// </summary>
public sealed class PathStatistics
{
    public long UnitRequests { get; }
    public long SquadLeaderRequests { get; }
    public long BlockedFollowerRequests { get; }
    public long HordeRequests { get; }
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

    /// <summary>Worker-thread ComputePath time in milliseconds; logic-thread time is reported
    /// as the stepper's nested path span instead.</summary>
    public double WorkerSearchMs { get; }

    public PathStatistics(
        long unitRequests,
        long squadLeaderRequests,
        long blockedFollowerRequests,
        long hordeRequests,
        long asyncRequests,
        long nodesExpanded,
        long failedSearches,
        long endpointAdjustments,
        long landedFollowers,
        long fallbackFollowers,
        double workerSearchMs)
    {
        UnitRequests = unitRequests;
        SquadLeaderRequests = squadLeaderRequests;
        BlockedFollowerRequests = blockedFollowerRequests;
        HordeRequests = hordeRequests;
        AsyncRequests = asyncRequests;
        NodesExpanded = nodesExpanded;
        FailedSearches = failedSearches;
        EndpointAdjustments = endpointAdjustments;
        LandedFollowers = landedFollowers;
        FallbackFollowers = fallbackFollowers;
        WorkerSearchMs = workerSearchMs;
    }

    public long SynchronousRequests => UnitRequests + SquadLeaderRequests + BlockedFollowerRequests + HordeRequests;

    public long RequestsFor(PathRequestOrigin origin) => origin switch
    {
        PathRequestOrigin.Unit => UnitRequests,
        PathRequestOrigin.SquadLeader => SquadLeaderRequests,
        PathRequestOrigin.BlockedFollower => BlockedFollowerRequests,
        PathRequestOrigin.Horde => HordeRequests,
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
    private long _asyncRequests;
    private long _nodesExpanded;
    private long _failedSearches;
    private long _endpointAdjustments;
    private long _landedFollowers;
    private long _fallbackFollowers;
    private long _workerSearchTicks;

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

    public void AddWorkerSearchTicks(long stopwatchTicks) => Interlocked.Add(ref _workerSearchTicks, stopwatchTicks);

    public PathStatistics Snapshot() => new(
        Interlocked.Read(ref _unitRequests),
        Interlocked.Read(ref _squadLeaderRequests),
        Interlocked.Read(ref _blockedFollowerRequests),
        Interlocked.Read(ref _hordeRequests),
        Interlocked.Read(ref _asyncRequests),
        Interlocked.Read(ref _nodesExpanded),
        Interlocked.Read(ref _failedSearches),
        Interlocked.Read(ref _endpointAdjustments),
        Interlocked.Read(ref _landedFollowers),
        Interlocked.Read(ref _fallbackFollowers),
        Interlocked.Read(ref _workerSearchTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
}
