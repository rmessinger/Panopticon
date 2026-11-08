using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Tools.ReplaySketch.Assertions;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-H5-05 report models: one record per harness run, the named comparisons between two runs,
/// and the aggregate parity report. Any inequality produces named diff entries carrying both
/// values; the report states the claim discipline verbatim.
/// </summary>
public sealed record BehavioralParityRunRecord(
    string Side,
    int RunIndex,
    uint Seed,
    string ContentPath,
    uint FinalFrame,
    bool HaltedOnOutcome,
    string OutcomeSummary,
    IReadOnlyDictionary<string, double> AssertionValues,
    IReadOnlyDictionary<string, int> SpawnedCounts,
    IReadOnlyDictionary<string, int> SurvivorCounts,
    IReadOnlyDictionary<string, int> KillCounts,
    IReadOnlyDictionary<string, int> LossCounts,
    string? Error);

/// <summary>One named comparison row: both values and whether they are equal.</summary>
public sealed record BehavioralParityComparison(
    string Kind,
    string Name,
    string? Expected,
    string? Actual,
    bool Passed);

/// <summary>Aggregate report for the full parity harness run.</summary>
public sealed record BehavioralParityReport(
    string ScenarioName,
    string SourceMapPath,
    uint Seed,
    string AssertionSet,
    string MapPackageDirectory,
    string UnitPackageDirectory,
    IReadOnlyList<BehavioralParityRunRecord> Runs,
    BehavioralParityComparisonSet Parity,
    BehavioralParityComparisonSet OriginalStability,
    BehavioralParityComparisonSet ConvertedStability,
    bool AllPassed,
    string ClaimStatement)
{
    public const string ClaimText =
        "Play-identically is proven by this run only: this scenario, this seed, this runtime. "
        + "Same-runtime original-versus-converted comparison only; never a cross-runtime or "
        + "cross-unit claim.";
}

/// <summary>A comparison set between two named runs (parity original-vs-converted, or stability repeat).</summary>
public sealed record BehavioralParityComparisonSet(
    string LeftLabel,
    string RightLabel,
    bool IsDeterminismCheck,
    bool AllPassed,
    IReadOnlyList<BehavioralParityComparison> Comparisons);

public static class BehavioralParityReportIo
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static void WriteJson(BehavioralParityReport report, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
    }

    /// <summary>Text summary: one line per comparison, divergences marked with both values.</summary>
    public static string WriteText(BehavioralParityReport report)
    {
        var lines = new List<string>
        {
            $"behavioral parity: scenario '{report.ScenarioName}' map '{report.SourceMapPath}' seed {report.Seed}",
            $"converted map package: {report.MapPackageDirectory}",
            $"converted unit package: {report.UnitPackageDirectory}",
            $"claim: {BehavioralParityReport.ClaimText}",
            string.Empty,
        };

        foreach (var run in report.Runs)
        {
            lines.Add(
                $"run {run.Side}#{run.RunIndex}: frame {run.FinalFrame}, outcome {run.OutcomeSummary}"
                + (run.Error is null ? string.Empty : $", error: {run.Error}"));
        }

        lines.Add(string.Empty);
        AppendSet(lines, "parity (original vs converted)", report.Parity);
        AppendSet(lines, "stability (original repeat)", report.OriginalStability);
        AppendSet(lines, "stability (converted repeat)", report.ConvertedStability);
        lines.Add(report.AllPassed
            ? "parity: PASS - all end-state assertions equal across both runs"
            : "parity: FAIL - see the named diff entries above");
        return string.Join('\n', lines);
    }

    private static void AppendSet(List<string> lines, string title, BehavioralParityComparisonSet set)
    {
        lines.Add($"{title} ({set.LeftLabel} vs {set.RightLabel}): {(set.AllPassed ? "equal" : "DIFFERS")}");
        foreach (var comparison in set.Comparisons.Where(c => !c.Passed))
        {
            lines.Add($"  DIFF {comparison.Kind}/{comparison.Name}: left={comparison.Expected} right={comparison.Actual}");
        }

        var failed = set.Comparisons.Count(c => !c.Passed);
        lines.Add($"  {set.Comparisons.Count - failed} equal, {failed} differing of {set.Comparisons.Count} comparisons"
            + (set.IsDeterminismCheck ? " [same-content determinism check]" : string.Empty));
        lines.Add(string.Empty);
    }
}
