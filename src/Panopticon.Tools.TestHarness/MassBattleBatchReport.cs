using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-M5-06 batch aggregate: per-side win rates with Wilson 95 percent intervals, draws,
/// failures, frame and wall-clock statistics, and summed kills/losses per definition. Draws
/// (no winner at the frame limit, score ties) and failures stay in the denominator.
/// </summary>
public sealed record MassBattleBatchReport
{
    public string Map { get; init; } = "";

    public string ProfilePath { get; init; } = "";

    public int SeedStart { get; init; }

    public int Runs { get; init; }

    public List<MassBattleBatchSideReport> Sides { get; init; } = [];

    public int Draws { get; init; }

    public int Failures { get; init; }

    public List<MassBattleBatchFailure> FailureDetails { get; init; } = [];

    public int MedianFramesToOutcome { get; init; }

    public BatchMetricStatistics Frames { get; init; } = new();

    public BatchMetricStatistics WallMinutes { get; init; } = new();

    public List<MassBattleKillLossRow> KillsSummed { get; init; } = [];

    public List<MassBattleKillLossRow> KillsPerRunMean { get; init; } = [];

    public List<MassBattleKillLossRow> LossesSummed { get; init; } = [];

    public List<MassBattleKillLossRow> LossesPerRunMean { get; init; } = [];

    public string BatchReportPath { get; init; } = "";

    public bool AllRunsReachedOutcome { get; init; }
}

public sealed record MassBattleBatchSideReport
{
    public string SideId { get; init; } = "";

    public int Wins { get; init; }

    public double WinRate { get; init; }

    public double WilsonLow { get; init; }

    public double WilsonHigh { get; init; }
}

public sealed record MassBattleBatchFailure
{
    public uint Seed { get; init; }

    public string Reason { get; init; } = "";
}

/// <summary>n, mean, median, sample variance, min, max over the batch's per-run values.</summary>
public sealed record BatchMetricStatistics
{
    public int Count { get; init; }

    public double Mean { get; init; }

    public double Median { get; init; }

    public double SampleVariance { get; init; }

    public double Min { get; init; }

    public double Max { get; init; }
}

public static class MassBattleBatchReportIo
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void WriteJson(MassBattleBatchReport report, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(report, Options));
    }

    public static string WriteText(MassBattleBatchReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.AppendLine($"Mass battle batch: {report.Map} ({report.ProfilePath})");
        b.AppendLine(
            $"seeds {report.SeedStart}..{report.SeedStart + report.Runs - 1}, runs {report.Runs}, draws {report.Draws}, failures {report.Failures}");
        b.AppendLine();
        b.AppendLine(string.Format(
            culture, "{0,-8} {1,6} {2,10} {3,12} {4,12}",
            "Side", "Wins", "Win rate", "Wilson low", "Wilson high"));
        foreach (var side in report.Sides)
        {
            b.AppendLine(string.Format(
                culture, "{0,-8} {1,6} {2,10:0.###} {3,12:0.####} {4,12:0.####}",
                side.SideId,
                side.Wins,
                side.WinRate,
                side.WilsonLow,
                side.WilsonHigh));
        }

        b.AppendLine();
        b.AppendLine(string.Format(
            culture,
            "frames to outcome: median {0}, mean {1:0.#}, variance {2:0.#}, min {3}, max {4} (n {5})",
            report.MedianFramesToOutcome,
            report.Frames.Mean,
            report.Frames.SampleVariance,
            report.Frames.Min,
            report.Frames.Max,
            report.Frames.Count));
        b.AppendLine(string.Format(
            culture,
            "wall minutes: median {0:0.#}, mean {1:0.#}, min {2:0.#}, max {3:0.#} (n {4})",
            report.WallMinutes.Median,
            report.WallMinutes.Mean,
            report.WallMinutes.Min,
            report.WallMinutes.Max,
            report.WallMinutes.Count));

        b.AppendLine();
        b.AppendLine("Kills summed (side, definition, count / per-run mean):");
        foreach (var row in report.KillsSummed)
        {
            var mean = report.KillsPerRunMean.Single(m => m.SideId == row.SideId && m.Definition == row.Definition).Count;
            b.AppendLine(string.Format(culture, "  {0,-8} {1,-34} {2,6} / {3:0.##}", row.SideId, row.Definition, row.Count, mean));
        }

        b.AppendLine("Losses summed (side, definition, count / per-run mean):");
        foreach (var row in report.LossesSummed)
        {
            var mean = report.LossesPerRunMean.Single(m => m.SideId == row.SideId && m.Definition == row.Definition).Count;
            b.AppendLine(string.Format(culture, "  {0,-8} {1,-34} {2,6} / {3:0.##}", row.SideId, row.Definition, row.Count, mean));
        }

        foreach (var failure in report.FailureDetails)
        {
            b.AppendLine($"failure seed {failure.Seed}: {failure.Reason}");
        }

        return b.ToString();
    }
}
