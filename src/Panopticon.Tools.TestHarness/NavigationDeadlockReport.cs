using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Panopticon.Tools.TestHarness;

/// <summary>Per-case outcome of a <c>NavigationDeadlock</c> run (WI-N6-02).</summary>
public sealed record NavigationDeadlockCaseResult(
    string Name,
    string Mechanism,
    int UnitsSpawned,
    int SettledUnits,
    int FramesRun,
    string TerminationReason,
    int? FramesToSettle,
    double PathSpanMs,
    double MaxTickPathSpanMs,
    long SyncRequests,
    long AsyncSubmitted,
    long AsyncApplied,
    long AsyncCancelled,
    int ImpassableRetracePoints,
    long ReroutedFollowers,
    long BlockedRepaths,
    bool MechanismConfirmed,
    string MechanismNote,
    IReadOnlyList<NavigationStallRecord> StallRecords,
    IReadOnlyList<NavigationWaitForCycle> WaitForCyclesAtEnd);

/// <summary>Report of a <c>NavigationDeadlock</c> run (WI-N6-02).</summary>
public sealed record NavigationDeadlockReport(
    int SchemaVersion,
    string Map,
    int Seed,
    int FrameBudget,
    bool ValidateOffsetRoutes,
    bool BlockedResolution,
    IReadOnlyList<NavigationDeadlockCaseResult> Cases)
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void WriteJson(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public void WriteText(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path);
        writer.WriteLine($"NavigationDeadlock baseline: map {Map}, seed {Seed}, budget {FrameBudget} frames/case.");
        writer.WriteLine();
        writer.WriteLine(
            "Case                 Units Settled Frames Termination      SpanMs MaxTick Retrace Reroute Repath Cycles Mech");
        foreach (var result in Cases)
        {
            writer.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,-20}{1,6}{2,8}{3,7} {4,-16}{5,7:F1}{6,8:F1}{7,8}{8,8}{9,7}{10,7}{11,6}",
                result.Name,
                result.UnitsSpawned,
                result.SettledUnits,
                result.FramesRun,
                result.TerminationReason,
                result.PathSpanMs,
                result.MaxTickPathSpanMs,
                result.ImpassableRetracePoints,
                result.ReroutedFollowers,
                result.BlockedRepaths,
                result.WaitForCyclesAtEnd.Count,
                result.MechanismConfirmed ? "yes" : "no"));
        }

        writer.WriteLine();
        foreach (var result in Cases)
        {
            var reasons = result.StallRecords
                .GroupBy(record => record.Reason)
                .Select(group => $"{group.Key}={group.Count()}");
            writer.WriteLine($"{result.Name}: {string.Join(", ", reasons)} stalls; {result.MechanismNote}");
        }
    }
}
