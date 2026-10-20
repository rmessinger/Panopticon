using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-M5-04 report of one mass battle run: mode, seed, P3 outcome with the winning side, and
/// per-side per-definition spawn and survival counts. Timing-free fields only: same seed and
/// profile produce a byte-identical JSON (the determinism contract the batch runner relies on).
/// </summary>
public sealed record MassBattleReport
{
    public string Map { get; init; } = "";

    public string ProfilePath { get; init; } = "";

    public uint Seed { get; init; }

    /// <summary>"headless" or "windowed".</summary>
    public string Mode { get; init; } = "";

    public int FrameLimit { get; init; }

    public int FramesRun { get; init; }

    public string OutcomeStatus { get; init; } = "";

    public string OutcomeReason { get; init; } = "";

    public string? WinningSideId { get; init; }

    public int? DecidedOnFrame { get; init; }

    public List<MassBattleSideReport> Sides { get; init; } = [];

    public string? ScreenshotPath { get; init; }

    public string? RunOutputPath { get; init; }

    public string? BenchmarkRecordPath { get; init; }

    public List<string> FailureReasons { get; init; } = [];

    public bool AllPassed { get; init; }
}

public sealed record MassBattleSideReport
{
    public string SideId { get; init; } = "";

    public string RosterId { get; init; } = "";

    public int UnitsSpawned { get; init; }

    public int UnitsAliveAtEnd { get; init; }

    public int AirfieldsPlaced { get; init; }

    public List<MassBattleEntryReport> Entries { get; init; } = [];
}

public sealed record MassBattleEntryReport
{
    public string Definition { get; init; } = "";

    public bool Aircraft { get; init; }

    public int Spawned { get; init; }

    public int AliveAtEnd { get; init; }
}

public static class MassBattleReportIo
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void WriteJson(MassBattleReport report, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(report, Options));
    }

    public static string WriteText(MassBattleReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.AppendLine($"Mass battle: {report.Map} ({report.ProfilePath})");
        b.AppendLine(
            $"mode: {report.Mode}, seed {report.Seed}, frames {report.FramesRun}/{report.FrameLimit}, outcome: {report.OutcomeStatus}/{report.OutcomeReason}");
        b.AppendLine(
            report.WinningSideId is null
                ? "winner: (none)"
                : $"winner: {report.WinningSideId} on frame {report.DecidedOnFrame?.ToString(culture)}");
        b.AppendLine();
        b.AppendLine(string.Format(
            culture, "{0,-8} {1,-12} {2,8} {3,8} {4,9}",
            "Side", "Roster", "Spawned", "Alive", "Airfields"));
        foreach (var side in report.Sides)
        {
            b.AppendLine(string.Format(
                culture, "{0,-8} {1,-12} {2,8} {3,8} {4,9}",
                side.SideId,
                side.RosterId,
                side.UnitsSpawned,
                side.UnitsAliveAtEnd,
                side.AirfieldsPlaced));
        }

        b.AppendLine();
        b.AppendLine(string.Format(
            culture, "{0,-8} {1,-34} {2,8} {3,10}",
            "Side", "Definition", "Spawned", "Alive"));
        foreach (var side in report.Sides)
        {
            foreach (var entry in side.Entries)
            {
                b.AppendLine(string.Format(
                    culture, "{0,-8} {1,-34} {2,8} {3,10}",
                    side.SideId,
                    entry.Definition,
                    entry.Spawned,
                    entry.AliveAtEnd));
            }
        }

        if (report.FailureReasons.Count > 0)
        {
            b.AppendLine();
            b.AppendLine("Failure reasons:");
            foreach (var reason in report.FailureReasons)
            {
                b.AppendLine($"  {reason}");
            }
        }

        return b.ToString();
    }
}
