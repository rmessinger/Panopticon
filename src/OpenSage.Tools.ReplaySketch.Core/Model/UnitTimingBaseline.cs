using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// A checked-in golden duration for one unit's primary action, with per-entry tolerance: a
/// percentage of the golden value plus a minimum absolute frame floor so a one-frame jitter does
/// not fail a short action. See deep dive P9 §2.7.
/// </summary>
public sealed class UnitTimingBaselineEntry
{
    public string DefinitionName { get; set; } = "";
    public int FactionIndex { get; set; }
    public string ActionKind { get; set; } = "";
    public uint GoldenFrames { get; set; }
    public double TolerancePercent { get; set; } = 10.0;
    public uint MinimumFrameFloor { get; set; } = 2;

    /// <summary>A unit passes when |measured - golden| is within the larger of the percentage band and the floor.</summary>
    public bool IsWithinTolerance(uint measured)
    {
        var band = (uint)Math.Ceiling(GoldenFrames * (TolerancePercent / 100.0));
        var allowance = Math.Max(band, MinimumFrameFloor);
        var delta = measured > GoldenFrames ? measured - GoldenFrames : GoldenFrames - measured;
        return delta <= allowance;
    }
}

public enum BaselineEntryVerdict
{
    Pass,
    Regressed,
    New,
}

public sealed record BaselineVerdict(
    string DefinitionName,
    int FactionIndex,
    string ActionKind,
    BaselineEntryVerdict Verdict,
    uint? MeasuredFrames,
    uint? GoldenFrames);

/// <summary>
/// The golden-baseline store for the validation ground. Loaded from a checked-in JSON file and
/// written only under an explicit refresh flag (a normal run never rewrites it). See deep dive
/// P9 §2.7.
/// </summary>
public sealed class UnitTimingBaseline
{
    public List<UnitTimingBaselineEntry> Entries { get; set; } = [];

    public UnitTimingBaselineEntry? Find(string definitionName, int factionIndex) =>
        Entries.FirstOrDefault(e => e.DefinitionName == definitionName && e.FactionIndex == factionIndex);

    /// <summary>Compares a report against the baseline, producing per-timing verdicts.</summary>
    public IReadOnlyList<BaselineVerdict> Compare(ValidationGroundReport report)
    {
        var verdicts = new List<BaselineVerdict>();

        foreach (var timing in report.Timings)
        {
            var entry = Find(timing.DefinitionName, timing.FactionIndex);
            if (entry == null)
            {
                verdicts.Add(new BaselineVerdict(
                    timing.DefinitionName,
                    timing.FactionIndex,
                    timing.ActionKind.ToString(),
                    BaselineEntryVerdict.New,
                    timing.Frames,
                    GoldenFrames: null));
            }
            else
            {
                var pass = entry.IsWithinTolerance(timing.Frames);
                verdicts.Add(new BaselineVerdict(
                    timing.DefinitionName,
                    timing.FactionIndex,
                    entry.ActionKind,
                    pass ? BaselineEntryVerdict.Pass : BaselineEntryVerdict.Regressed,
                    timing.Frames,
                    entry.GoldenFrames));
            }
        }

        return verdicts;
    }

    /// <summary>True when any timing in the report regressed beyond its tolerance.</summary>
    public bool HasRegression(ValidationGroundReport report) =>
        Compare(report).Any(v => v.Verdict == BaselineEntryVerdict.Regressed);

    public static UnitTimingBaseline Load(string path)
    {
        if (!File.Exists(path))
        {
            return new UnitTimingBaseline();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UnitTimingBaseline>(json) ?? new UnitTimingBaseline();
    }

    /// <summary>Saves the baseline. Callers gate this behind an explicit refresh flag.</summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
