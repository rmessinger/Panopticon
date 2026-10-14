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
}
