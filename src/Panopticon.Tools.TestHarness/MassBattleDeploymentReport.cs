using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Proof artifact of a <c>MassBattleDeployment</c> run: per-side capacity and airfield placement,
/// zone separation vs the longest roster weapon range, and one reachability row per distinct
/// ground locomotor class. Serialized to JSON for the record and to a fixed-width stdout table.
/// </summary>
public sealed record MassBattleDeploymentReport
{
    public string Map { get; init; } = "";

    public string Provenance { get; init; } = "";

    public string ProfilePath { get; init; } = "";

    public string? ScreenshotPath { get; init; }

    public List<SideCapacityResult> Sides { get; init; } = new();

    public SeparationResult Separation { get; init; } = new();

    public int GridPathNodes { get; init; }

    public bool GridEndPassable { get; init; }

    public List<ReachabilityClassResult> Reachability { get; init; } = new();

    public bool AllPassed { get; init; }

    public static void WriteJson(MassBattleDeploymentReport report, string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, options));
    }

    public static string WriteText(MassBattleDeploymentReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.AppendLine($"Mass battle deployment: {report.Map} ({report.Provenance})");
        b.AppendLine();
        b.AppendLine(string.Format(
            culture, "{0,-8} {1,-24} {2,6} {3,11} {4,10} {5,-8}",
            "Side", "Zone x,y wxh", "Units", "Impassable", "Airfields", "Verdict"));
        foreach (var side in report.Sides)
        {
            b.AppendLine(string.Format(
                culture, "{0,-8} {1,-24} {2,6} {3,11} {4,10} {5,-8}",
                side.Id,
                $"{side.ZoneX.ToString("F0", culture)},{side.ZoneY.ToString("F0", culture)} {side.ZoneWidth.ToString("F0", culture)}x{side.ZoneHeight.ToString("F0", culture)}",
                side.UnitsSpawned,
                side.ImpassableSpawns,
                $"{side.AirfieldsPlaced}/{side.AirfieldAnchors.Count}",
                side.Verdict));
            foreach (var reason in side.FailureReasons)
            {
                b.AppendLine($"    reason: {reason}");
            }
        }

        b.AppendLine();
        b.AppendLine(string.Format(
            culture,
            "Separation: zone gap {0:F0} vs longest roster weapon range {1:F0}: {2}",
            report.Separation.Gap,
            report.Separation.MaxWeaponRange,
            report.Separation.Verdict));
        b.AppendLine(string.Format(
            culture,
            "Grid path between zone centroids: {0} nodes, end passable: {1}",
            report.GridPathNodes,
            report.GridEndPassable));
        b.AppendLine();
        b.AppendLine(string.Format(
            culture, "{0,-22} {1,-30} {2,7} {3,7} {4,-8}",
            "Surfaces", "Representative", "Speed", "Frames", "Verdict"));
        foreach (var probe in report.Reachability)
        {
            b.AppendLine(string.Format(
                culture, "{0,-22} {1,-30} {2,7:F2} {3,7} {4,-8}",
                probe.Surfaces,
                probe.RepresentativeDefinition,
                probe.MaxSpeed,
                probe.Frames,
                probe.Verdict));
            if (probe.Verdict != "OK")
            {
                b.AppendLine($"    reason: {probe.Reason}");
            }
        }

        b.AppendLine();
        b.AppendLine(report.AllPassed ? "All checks passed." : "FAILURES present; see reasons above.");
        return b.ToString();
    }
}

public sealed record SideCapacityResult
{
    public string Id { get; init; } = "";

    public float ZoneX { get; init; }

    public float ZoneY { get; init; }

    public float ZoneWidth { get; init; }

    public float ZoneHeight { get; init; }

    public int UnitsSpawned { get; init; }

    public int UnitsRequested { get; init; }

    public int ImpassableSpawns { get; init; }

    public int ImpassableSlotsSkipped { get; init; }

    public int AirfieldsPlaced { get; init; }

    public float LongestWeaponRange { get; init; }

    public List<MassBattlePoint> AirfieldAnchors { get; init; } = new();

    public List<string> FailureReasons { get; init; } = new();

    public string Verdict => FailureReasons.Count == 0 ? "OK" : "FAIL";
}

public sealed record SeparationResult
{
    public float Gap { get; init; }

    public float MaxWeaponRange { get; init; }

    public string Verdict => Gap > MaxWeaponRange ? "OK" : "FAIL";
}

public sealed record ReachabilityClassResult
{
    public string Surfaces { get; init; } = "";

    public string RepresentativeDefinition { get; init; } = "";

    public float MaxSpeed { get; init; }

    public float Distance { get; init; }

    public int FrameBudget { get; init; }

    public int Frames { get; init; }

    public string Verdict { get; init; } = "FAIL";

    public string Reason { get; init; } = "";
}
