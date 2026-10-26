using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Panopticon.Tools.TestHarness;

/// <summary>Per-case request counts by <see cref="Panopticon.Navigation.PathRequestOrigin"/>.</summary>
public sealed record PathfindingLoadRequestCounts
{
    public long Unit { get; init; }

    public long SquadLeader { get; init; }

    public long BlockedFollower { get; init; }

    public long Horde { get; init; }

    public long Async { get; init; }
}

/// <summary>Measured result of one <see cref="PathfindingLoadCasePlan"/>.</summary>
public sealed record PathfindingLoadCaseResult
{
    public string Name { get; init; } = "";

    public string Mode { get; init; } = "";

    public string DestinationKind { get; init; } = "";

    public int RequestedSize { get; init; }

    public int SpawnedSize { get; init; }

    public int SquadsAtOrder { get; init; }

    public int SettleFrames { get; init; }

    public int WindowTicks { get; init; }

    public float DestinationX { get; init; }

    public float DestinationY { get; init; }

    public float DestinationZ { get; init; }

    public double OrderTickPathSpanMs { get; init; }

    public PathfindingLoadRequestCounts Requests { get; init; } = new();

    public long SyncRequests { get; init; }

    public long NodesExpanded { get; init; }

    public long FailedSearches { get; init; }

    public long EndpointAdjustments { get; init; }

    /// <summary>Blocked followers served by the bounded ring landing; no search issued
    /// (WI-N2-01).</summary>
    public long LandedFollowers { get; init; }

    /// <summary>Blocked followers whose landing failed and paid for their own search
    /// (WI-N2-01).</summary>
    public long FallbackFollowers { get; init; }

    /// <summary>WI-N7-02: followers whose offset retrace crossed impassable cells and were
    /// rerouted onto the leader route or their own search.</summary>
    public long ReroutedFollowers { get; init; }

    /// <summary>Offset-shifted retrace waypoints across the order that sit on impassable cells
    /// (WI-N2-01: existing retrace behavior, recorded, not fixed here).</summary>
    public int ImpassableRetracePoints { get; init; }

    /// <summary>Landing counts by ring distance from the blocked offset destination, indexed by
    /// ring (WI-N2-01 distribution).</summary>
    public long[] LandingRings { get; init; } = [];

    /// <summary>Largest number of full searches sharing one goal neighbourhood on a single
    /// window tick (WI-N3-01; neighbourhood = PathStatistics.GoalNeighbourhoodCells).</summary>
    public long MaxGoalNeighbourhoodRequests { get; init; }

    /// <summary>Full searches whose goal histogram landed in the order-tick window
    /// (WI-N3-01); includes the inline order searches.</summary>
    public long OrderTickFullSearches { get; init; }

    public double MeanSearchMs { get; init; }

    public double P99SearchMs { get; init; }
}

/// <summary>
/// Artifact of a <c>PathfindingLoad</c> run (WI-N0-02): per-case order-tick path span, request
/// counts by origin, nodes expanded, failed searches, and derived search wall time. Serialized to
/// JSON for the record and a fixed-width stdout table.
/// </summary>
public sealed record PathfindingLoadReport
{
    public int SchemaVersion { get; init; } = 1;

    public string Map { get; init; } = "";

    public string ProfilePath { get; init; } = "";

    public int Seed { get; init; }

    /// <summary>WI-N2-02: whether squad-less selections moved as position clusters.</summary>
    public bool GroupMoves { get; init; }

    /// <summary>WI-N2-02: position-cluster cell size in world units.</summary>
    public int ClusterCells { get; init; }

    /// <summary>WI-N4-02: whether order-driven paths submitted to the async worker queue.</summary>
    public bool AsyncOrderPaths { get; init; }

    /// <summary>WI-N7-02: whether squad offset retrace routes were validated against
    /// impassable cells.</summary>
    public bool ValidateOffsetRoutes { get; init; }

    /// <summary>WI-N4-02: total worker-thread search time, separate from the logic-thread span.</summary>
    public double WorkerSearchMs { get; init; }

    /// <summary>WI-N4-02: async submissions over the whole run.</summary>
    public long AsyncSubmitted { get; init; }

    /// <summary>WI-N4-02: completions applied at drain over the whole run.</summary>
    public long AsyncApplied { get; init; }

    /// <summary>WI-N4-02: submissions cancelled before applying over the whole run.</summary>
    public long AsyncCancelled { get; init; }

    /// <summary>WI-N4-02: applied requests that waited past the frame bound.</summary>
    public long AsyncDeadlineMisses { get; init; }

    /// <summary>WI-N4-02: frame bound for the deadline-miss counter.</summary>
    public int AsyncFrameBound { get; init; }

    /// <summary>WI-N4-02: median waited frames per applied request.</summary>
    public double AsyncWaitFramesMedian { get; init; }

    /// <summary>WI-N4-02: p99 waited frames per applied request.</summary>
    public double AsyncWaitFramesP99 { get; init; }

    /// <summary>WI-N4-02: maximum waited frames per applied request.</summary>
    public int AsyncWaitFramesMax { get; init; }

    public int GridWidth { get; init; }

    public int GridHeight { get; init; }

    /// <summary>How meanSearchMs and p99SearchMs are derived from the per-tick window samples.</summary>
    public string Derivation { get; init; } = "";

    public List<PathfindingLoadCaseResult> Cases { get; init; } = new();

    public List<string> FailureReasons { get; init; } = new();

    public bool AllPassed { get; init; }

    public static void WriteJson(PathfindingLoadReport report, string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, options));
    }

    public static string WriteText(PathfindingLoadReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.AppendLine($"Pathfinding load: {report.Map} ({report.ProfilePath})");
        b.AppendLine($"seed {report.Seed}, grid {report.GridWidth}x{report.GridHeight}");
        b.AppendLine();
        b.AppendLine(string.Format(
            culture, "{0,-30} {1,7} {2,6} {3,10} {4,7} {5,8} {6,10} {7,7} {8,8} {9,11} {10,10} {11,8} {12,10} {13,10} {14,10}",
            "Case", "Spawned", "Squads", "SpanMs", "Unit", "SquadL.", "BlockedF.", "Landed", "Fallback", "Nodes", "Failed", "ImpassTP", "GoalMax", "FullSrch", "MeanMs"));
        foreach (var c in report.Cases)
        {
            b.AppendLine(string.Format(
                culture, "{0,-30} {1,7} {2,6} {3,10:0.000} {4,7} {5,8} {6,10} {7,7} {8,8} {9,11} {10,10} {11,8} {12,10} {13,10} {14,10:0.000}",
                c.Name,
                c.SpawnedSize,
                c.SquadsAtOrder,
                c.OrderTickPathSpanMs,
                c.Requests.Unit,
                c.Requests.SquadLeader,
                c.Requests.BlockedFollower,
                c.LandedFollowers,
                c.FallbackFollowers,
                c.NodesExpanded,
                c.FailedSearches,
                c.ImpassableRetracePoints,
                c.MaxGoalNeighbourhoodRequests,
                c.OrderTickFullSearches,
                c.MeanSearchMs));
        }

        return b.ToString();
    }
}
