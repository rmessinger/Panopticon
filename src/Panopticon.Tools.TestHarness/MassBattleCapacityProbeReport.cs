using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Engagement artifact of a <c>MassBattleCapacityProbe</c> run (WI-M1-01): per-side spawn and
/// casualty counts, per-jet sortie cycles (launches and recoveries through the airfield parking
/// flow), and the stop reason. Serialized to JSON for the record and a fixed-width stdout table.
/// </summary>
public sealed record MassBattleCapacityProbeReport
{
    public string Map { get; init; } = "";

    public string Provenance { get; init; } = "";

    public string ProfilePath { get; init; } = "";

    public string? ScreenshotPath { get; init; }

    public bool Headless { get; init; }

    public string AircraftDefinition { get; init; } = "";

    public int SettleFrames { get; init; }

    public int MeasuredFramesRequested { get; init; }

    public int MeasuredFramesRun { get; init; }

    public string StopReason { get; init; } = "";

    public List<SideEngagementResult> Sides { get; init; } = new();

    public List<SortieResult> Sorties { get; init; } = new();

    public List<string> FailureReasons { get; init; } = new();

    public bool AllPassed { get; init; }

    public MassBattleAsyncPathAnchorReport? AsyncPathAnchor { get; init; }

    /// <summary>WI-M4-03: army-AI mode squad invariant; null in the scripted M1 mode.</summary>
    public MassBattleSquadInvariantReport? SquadInvariant { get; init; }

    /// <summary>WI-M4-05: army-AI mode air mission totals per side; null in the scripted M1 mode.</summary>
    public List<AirMissionSideResult>? AirMissions { get; init; }

    public static void WriteJson(MassBattleCapacityProbeReport report, string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, options));
    }

    public static string WriteText(MassBattleCapacityProbeReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.AppendLine($"Mass battle capacity probe: {report.Map} ({report.Provenance})");
        b.AppendLine(
            $"mode: {(report.Headless ? "headless" : "windowed")}, settle {report.SettleFrames} frames, measured {report.MeasuredFramesRun}/{report.MeasuredFramesRequested} frames, stop: {report.StopReason}");
        b.AppendLine();
        b.AppendLine(string.Format(
            culture, "{0,-8} {1,7} {2,9} {3,7} {4,9}",
            "Side", "Ground", "Aircraft", "Alive", "Destroyed"));
        foreach (var side in report.Sides)
        {
            b.AppendLine(string.Format(
                culture, "{0,-8} {1,7} {2,9} {3,7} {4,9}",
                side.Id,
                side.GroundSpawned,
                side.AircraftParked,
                side.AliveAtEnd,
                side.DestroyedAtEnd));
        }

        b.AppendLine();
        b.AppendLine(string.Format(
            culture, "{0,-8} {1,-22} {2,9} {3,10} {4,-20}",
            "Side", "Aircraft", "Launches", "Recoveries", "Final state"));
        foreach (var sortie in report.Sorties)
        {
            b.AppendLine(string.Format(
                culture, "{0,-8} {1,-22} {2,9} {3,10} {4,-20}",
                sortie.SideId,
                sortie.Definition,
                sortie.Launches,
                sortie.Recoveries,
                sortie.FinalState));
        }

        b.AppendLine();
        if (report.AsyncPathAnchor is not null)
        {
            b.AppendLine(MassBattleAsyncPathAnchorReport.WriteText(report.AsyncPathAnchor));
        }

        if (report.SquadInvariant is not null)
        {
            b.AppendLine();
            b.AppendLine("Squad invariant (WI-M4-03, army-AI mode):");
            b.AppendLine(string.Format(
                culture, "{0,-8} {1,7} {2,8} {3,11} {4,13} {5,12} {6,14}",
                "Side", "Squads", "Members", "Unassigned", "MaxUnassigned", "Commanderl.", "MaxCommanderl."));
            foreach (var side in report.SquadInvariant.Sides)
            {
                b.AppendLine(string.Format(
                    culture, "{0,-8} {1,7} {2,8} {3,11} {4,12} {5,13} {6,14}",
                    side.SideId,
                    side.GroundSquads,
                    side.GroundMembers,
                    side.UnassignedEligibleGround,
                    side.MaxUnassignedEligibleGround,
                    side.CommanderlessSquads,
                    side.MaxCommanderlessSquads));
            }

            var invariant = report.SquadInvariant;
            b.AppendLine(string.Format(
                culture,
                "Group orders (WI-M4-04): decision ticks {0}, max path requests per decision tick {1} " +
                "(squads {2}, members {3}, blocked followers {4}); bound is squads plus blocked followers.",
                invariant.DecisionTicksObserved,
                invariant.MaxPathRequestsPerDecisionTick,
                invariant.SquadsAtWorstDecisionTick,
                invariant.MembersAtWorstDecisionTick,
                invariant.MaxBlockedFollowersPerDecisionTick));
        }

        if (report.AirMissions is { Count: > 0 })
        {
            b.AppendLine();
            b.AppendLine("Air missions (WI-M4-05, army-AI mode):");
            b.AppendLine(string.Format(
                culture, "{0,-8} {1,8} {2,8} {3,8} {4,10}",
                "Side", "Strike", "Patrol", "Escort", "Jet sorties with >= 2 missions"));
            foreach (var side in report.AirMissions)
            {
                b.AppendLine(string.Format(
                    culture, "{0,-8} {1,8} {2,8} {3,8} {4,10}",
                    side.SideId,
                    side.Strike,
                    side.Patrol,
                    side.Escort,
                    side.JetsWithMultipleMissions));
            }
        }

        if (report.FailureReasons.Count > 0)
        {
            foreach (var reason in report.FailureReasons)
            {
                b.AppendLine($"reason: {reason}");
            }
        }

        b.AppendLine(report.AllPassed ? "All checks passed." : "FAILURES present; see reasons above.");
        return b.ToString();
    }
}

public sealed record SideEngagementResult
{
    public string Id { get; init; } = "";

    public int GroundSpawned { get; init; }

    public int GroundRequested { get; init; }

    public int AircraftParked { get; init; }

    public int AircraftRequested { get; init; }

    public int AliveAtEnd { get; init; }

    public int DestroyedAtEnd { get; init; }
}

/// <summary>
/// One jet's sortie history: launches counted on the Parked-to-taxi transition, recoveries on
/// the return-to-parking transition. Two launches mean the jet rearmed (parked, healed, clips
/// refilled) and flew again while enemies remained.
/// </summary>
public sealed record SortieResult
{
    public string SideId { get; init; } = "";

    public string Definition { get; init; } = "";

    public int Launches { get; init; }

    public int Recoveries { get; init; }

    public int FirstLaunchFrame { get; init; }

    public int FinalState { get; init; }

    public string FinalStateName { get; init; } = "";

    /// <summary>WI-M4-05: missions the air commander assigned to this jet; null in scripted mode.</summary>
    public int? MissionsAssigned { get; init; }
}

/// <summary>WI-M4-05: air mission totals one side's air commander assigned.</summary>
public sealed record AirMissionSideResult
{
    public string SideId { get; init; } = "";

    public int Strike { get; init; }

    public int Patrol { get; init; }

    public int Escort { get; init; }

    /// <summary>Aircraft the commander assigned two or more missions (launched, recovered, re-tasked).</summary>
    public int JetsWithMultipleMissions { get; init; }
}

/// <summary>
/// WI-M4-03: the squad invariant over an army-AI engagement, per side. Current counts are the
/// final snapshot; Max columns are the worst observed after any decision tick, so zero Max
/// columns prove no eligible ground unit was ever left unassigned and no non-empty squad was
/// ever left without a live commander.
/// WI-M4-04: the group-order counters record, per decision tick, how many path requests the
/// dispatched squad orders cost. The bound is squads plus blocked followers (WI-B4-04), not
/// units; the worst tick carries the squad and member counts it was issued against.
/// </summary>
public sealed record MassBattleSquadInvariantReport
{
    public List<SquadInvariantSideResult> Sides { get; init; } = new();

    public int DecisionTicksObserved { get; init; }

    public long MaxPathRequestsPerDecisionTick { get; init; }

    public int SquadsAtWorstDecisionTick { get; init; }

    public int MembersAtWorstDecisionTick { get; init; }

    public long MaxBlockedFollowersPerDecisionTick { get; init; }
}

public sealed record SquadInvariantSideResult
{
    public string SideId { get; init; } = "";

    public int GroundSquads { get; init; }

    public int GroundMembers { get; init; }

    public int UnassignedEligibleGround { get; init; }

    public int MaxUnassignedEligibleGround { get; init; }

    public int CommanderlessSquads { get; init; }

    public int MaxCommanderlessSquads { get; init; }
}
