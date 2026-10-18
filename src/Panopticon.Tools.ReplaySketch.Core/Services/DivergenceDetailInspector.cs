#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// WI-A5-22: matches a divergence report against a run bundle's T2 detail windows and derives
/// per-entity, per-quantity delta rows. Detail is recovered from recorded captures only; the
/// simulation is never rerun.
/// </summary>
public enum DivergenceDetailState
{
    /// <summary>A captured window covers the first divergent frame and subtree.</summary>
    Matched,

    /// <summary>The run recorded no T2 detail windows at all.</summary>
    Missing,

    /// <summary>Windows exist but none covers the divergent frame and subtree.</summary>
    Unarmed,

    /// <summary>The two runs are not signature-comparable; no matching is possible.</summary>
    Incompatible,
}

/// <summary>One quantity change inside the matched window; frame drives replay navigation.</summary>
public sealed record DivergenceDetailRow(
    uint Frame,
    string Subtree,
    uint? EntityId,
    string Quantity,
    string Before,
    string Current,
    string Delta);

/// <summary>Result of inspecting one bundle's captures against a divergence report.</summary>
public sealed record DivergenceDetailReport(
    DivergenceDetailState State,
    string Diagnostic,
    IReadOnlyList<DivergenceDetailRow> Rows,
    uint? WindowStart,
    uint? WindowEnd,
    string? WindowReason);

/// <summary>
/// WI-A5-22: owns the matching (divergence locus to T2 window) and delta derivation (window
/// frames to entity/quantity rows). Pure function over recorded artifacts.
/// </summary>
public static class DivergenceDetailInspector
{
    private const string WorldPrefix = "world/";

    public static DivergenceDetailReport Inspect(RunDivergenceReport divergence, RunOutput manifest)
    {
        if (!divergence.Comparable)
        {
            return new DivergenceDetailReport(
                DivergenceDetailState.Incompatible,
                divergence.IncompatibilityReason ?? "runs are not comparable",
                [],
                null,
                null,
                null);
        }

        var captures = manifest.TriggeredCaptures;
        if (captures is not { Count: > 0 })
        {
            return new DivergenceDetailReport(
                DivergenceDetailState.Missing,
                "the run recorded no T2 detail windows (capture never tripped or was disabled)",
                [],
                null,
                null,
                null);
        }

        var frame = divergence.FirstDivergentFrame;
        var subtreeKeys = divergence.Loci
            .Where(l => l.Frame == frame)
            .Select(l => l.Path.StartsWith(WorldPrefix, StringComparison.Ordinal)
                ? l.Path[WorldPrefix.Length..]
                : l.Path)
            .ToHashSet(StringComparer.Ordinal);

        var window = captures.FirstOrDefault(w =>
            frame >= w.StartFrame &&
            frame <= w.EndFrame &&
            w.DivergedSubtrees?.Any(subtreeKeys.Contains) == true);

        if (window is null)
        {
            return new DivergenceDetailReport(
                DivergenceDetailState.Unarmed,
                $"{captures.Count} T2 window(s) recorded, but none covers frame {frame} in subtree(s) {string.Join(", ", subtreeKeys)}",
                [],
                null,
                null,
                null);
        }

        var rows = BuildRows(window);
        return new DivergenceDetailReport(
            DivergenceDetailState.Matched,
            $"window {window.StartFrame}-{window.EndFrame} (reason: {window.Reason}) covers frame {frame} in subtree(s) {string.Join(", ", subtreeKeys)}",
            rows,
            window.StartFrame,
            window.EndFrame,
            window.Reason);
    }

    private static List<DivergenceDetailRow> BuildRows(TriggeredDetailWindowRecord window)
    {
        var rows = new List<DivergenceDetailRow>();
        var subtree = window.DivergedSubtrees is { Count: > 0 } ? window.DivergedSubtrees[0] : "world";
        // Walk the window in order so "before" is the previous recorded value of each quantity.
        var lastHealth = new Dictionary<uint, float>();
        var lastPosition = new Dictionary<uint, (float X, float Y, float Z)>();

        foreach (var delta in window.Frames)
        {
            if (delta.Added.Count > 0)
            {
                rows.Add(new DivergenceDetailRow(
                    delta.Frame, subtree, null, "added", string.Empty, delta.Added.Count.ToString(), $"+{delta.Added.Count}"));
            }

            foreach (var added in delta.Added)
            {
                lastHealth[added.ObjectId] = added.Health;
                lastPosition[added.ObjectId] = (added.X, added.Y, added.Z);
            }

            if (delta.Removed.Count > 0)
            {
                rows.Add(new DivergenceDetailRow(
                    delta.Frame, subtree, null, "removed", string.Empty, delta.Removed.Count.ToString(), $"-{delta.Removed.Count}"));
            }

            foreach (var id in delta.Removed)
            {
                lastHealth.Remove(id);
                lastPosition.Remove(id);
            }

            foreach (var changed in delta.Changed)
            {
                if (lastHealth.TryGetValue(changed.ObjectId, out var beforeHealth))
                {
                    var healthDelta = changed.Health - beforeHealth;
                    rows.Add(new DivergenceDetailRow(
                        delta.Frame,
                        subtree,
                        changed.ObjectId,
                        "health",
                        beforeHealth.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                        changed.Health.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                        healthDelta.ToString("+#.0;-#.0;0.0", System.Globalization.CultureInfo.InvariantCulture)));
                }

                if (lastPosition.TryGetValue(changed.ObjectId, out var beforePosition))
                {
                    var dx = changed.X - beforePosition.X;
                    var dy = changed.Y - beforePosition.Y;
                    var dz = changed.Z - beforePosition.Z;
                    var distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    rows.Add(new DivergenceDetailRow(
                        delta.Frame,
                        subtree,
                        changed.ObjectId,
                        "position",
                        $"({beforePosition.X:0.#}, {beforePosition.Y:0.#}, {beforePosition.Z:0.#})",
                        $"({changed.X:0.#}, {changed.Y:0.#}, {changed.Z:0.#})",
                        $"moved {distance:0.#}"));
                }

                lastHealth[changed.ObjectId] = changed.Health;
                lastPosition[changed.ObjectId] = (changed.X, changed.Y, changed.Z);
            }

            if (delta.PlayerMoney.Count > 0)
            {
                rows.Add(new DivergenceDetailRow(
                    delta.Frame,
                    subtree,
                    null,
                    "playerMoney",
                    string.Join(", ", delta.PlayerMoney),
                    string.Join(", ", delta.PlayerMoney),
                    "recorded per-frame"));
            }
        }

        return rows;
    }
}
