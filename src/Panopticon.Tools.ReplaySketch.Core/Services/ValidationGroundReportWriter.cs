using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Emits the validation-ground report as a fixed-width stdout table and a machine-readable JSON
/// artifact. Timeouts and gaps are rendered distinctly from measured durations, and rows are
/// sorted deterministically (faction then definition name) so two runs produce diffable output.
/// See deep dive P9 §2.6.
/// </summary>
public static class ValidationGroundReportWriter
{
    public static string WriteTextReport(
        ValidationGroundReport report,
        UnitTimingBaseline? baseline = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Unit validation ground results");
        sb.AppendLine($"{"Unit",-34}{"Fac",-5}{"Action",-13}{"Frames",-8}{"Base",-8}{"Delta",-8}{"Verdict",-12}");
        sb.AppendLine(new string('-', 88));

        var verdicts = baseline?.Compare(report) ?? [];
        var verdictByUnit = verdicts
            .ToDictionary(v => (v.DefinitionName, v.FactionIndex));

        foreach (var row in SortedRows(report))
        {
            AppendRow(sb, row, verdictByUnit);
        }

        var passed = verdicts.Count(v => v.Verdict == BaselineEntryVerdict.Pass);
        var regressed = verdicts.Count(v => v.Verdict == BaselineEntryVerdict.Regressed);
        var newUnits = verdicts.Count(v => v.Verdict == BaselineEntryVerdict.New);

        sb.AppendLine();
        sb.AppendLine($"Summary: {report.Timings.Count} measured ({passed} pass, {regressed} regressed, {newUnits} new), " +
                      $"{report.Timeouts.Count} timed out, {report.Gaps.Count} gaps, {report.TotalUnitCount} total.");
        return sb.ToString();
    }

    public static void WriteJson(
        ValidationGroundReport report,
        UnitTimingBaseline? baseline,
        string path)
    {
        var verdicts = baseline?.Compare(report) ?? [];
        var payload = new
        {
            timings = report.Timings.Select(t => new
            {
                t.DefinitionName,
                t.FactionIndex,
                action = t.ActionKind.ToString(),
                t.Frames,
                verdict = VerdictFor(t, verdicts),
            }),
            timeouts = report.Timeouts.Select(t => new
            {
                t.DefinitionName,
                t.FactionIndex,
                action = t.ActionKind.ToString(),
                t.FrameBudget,
            }),
            gaps = report.Gaps.Select(g => new
            {
                g.DefinitionName,
                g.FactionIndex,
                action = g.ActionKind.ToString(),
                g.Reason,
            }),
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static IEnumerable<ReportRow> SortedRows(ValidationGroundReport report)
    {
        foreach (var t in report.Timings.OrderBy(t => t.FactionIndex).ThenBy(t => t.DefinitionName))
        {
            yield return new ReportRow(t.DefinitionName, t.FactionIndex, t.ActionKind.ToString(),
                MeasuredFrames: t.Frames, FrameBudget: null, IsTimeout: false, GapReason: null);
        }

        foreach (var t in report.Timeouts.OrderBy(t => t.FactionIndex).ThenBy(t => t.DefinitionName))
        {
            yield return new ReportRow(t.DefinitionName, t.FactionIndex, t.ActionKind.ToString(),
                MeasuredFrames: null, FrameBudget: t.FrameBudget, IsTimeout: true, GapReason: null);
        }

        foreach (var g in report.Gaps.OrderBy(g => g.FactionIndex).ThenBy(g => g.DefinitionName))
        {
            yield return new ReportRow(g.DefinitionName, g.FactionIndex, g.ActionKind.ToString(),
                MeasuredFrames: null, FrameBudget: null, IsTimeout: false, GapReason: g.Reason);
        }
    }

    private static void AppendRow(
        StringBuilder sb, ReportRow row,
        IReadOnlyDictionary<(string, int), BaselineVerdict> verdictByUnit)
    {
        string frames = row.MeasuredFrames.HasValue
            ? row.MeasuredFrames.Value.ToString(CultureInfo.InvariantCulture)
            : (row.IsTimeout ? "-" : "gap");

        string baseline = "";
        string delta = "";
        string verdict = row.IsTimeout ? "TIMEOUT" : (row.GapReason != null ? "GAP" : "OK");

        if (!row.IsTimeout && row.GapReason == null &&
            verdictByUnit.TryGetValue((row.DefinitionName, row.FactionIndex), out var v))
        {
            baseline = v.GoldenFrames?.ToString(CultureInfo.InvariantCulture) ?? "new";
            if (v.MeasuredFrames.HasValue && v.GoldenFrames.HasValue)
            {
                var d = (int)v.MeasuredFrames.Value - (int)v.GoldenFrames.Value;
                delta = d.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            }

            verdict = v.Verdict.ToString().ToUpperInvariant();
        }

        if (row.GapReason != null)
        {
            verdict = "GAP";
        }

        sb.AppendLine($"{row.DefinitionName,-34}{row.FactionIndex,-5}{row.ActionKind,-13}" +
                      $"{frames,-8}{baseline,-8}{delta,-8}{verdict,-12}");
        if (row.GapReason != null)
        {
            sb.AppendLine($"    reason: {row.GapReason}");
        }
    }

    private static string VerdictFor(UnitTiming t, IEnumerable<BaselineVerdict> verdicts)
    {
        var v = verdicts.FirstOrDefault(v => v.DefinitionName == t.DefinitionName && v.FactionIndex == t.FactionIndex);
        return v?.Verdict.ToString() ?? "New";
    }

    private sealed record ReportRow(
        string DefinitionName,
        int FactionIndex,
        string ActionKind,
        uint? MeasuredFrames,
        int? FrameBudget,
        bool IsTimeout,
        string? GapReason);
}
