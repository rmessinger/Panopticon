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

    /// <summary>Offset-shifted retrace waypoints across the order that sit on impassable cells
    /// (WI-N2-01: existing retrace behavior, recorded, not fixed here).</summary>
    public int ImpassableRetracePoints { get; init; }

    /// <summary>Landing counts by ring distance from the blocked offset destination, indexed by
    /// ring (WI-N2-01 distribution).</summary>
    public long[] LandingRings { get; init; } = [];

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
            culture, "{0,-30} {1,7} {2,6} {3,10} {4,7} {5,8} {6,10} {7,7} {8,8} {9,11} {10,10} {11,8} {12,10}",
            "Case", "Spawned", "Squads", "SpanMs", "Unit", "SquadL.", "BlockedF.", "Landed", "Fallback", "Nodes", "Failed", "ImpassTP", "MeanMs"));
        foreach (var c in report.Cases)
        {
            b.AppendLine(string.Format(
                culture, "{0,-30} {1,7} {2,6} {3,10:0.000} {4,7} {5,8} {6,10} {7,7} {8,8} {9,11} {10,10} {11,8} {12,10:0.000}",
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
                c.MeanSearchMs));
        }

        return b.ToString();
    }
}
