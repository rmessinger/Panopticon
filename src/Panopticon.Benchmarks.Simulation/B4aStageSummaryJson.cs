using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Serializes a B4a consumer run to stage-summary.json. The file is additive: the B1 run record is
/// still persisted by <see cref="BenchmarkReportWriter"/>. Variances are sample variances (n - 1).
/// Allocation fields count bytes allocated on the measuring thread within each stage bracket.
/// </summary>
public static class B4aStageSummaryJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(B4aConsumerRun run, B4aConsumerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plan);

        var record = run.Record;
        var observations = run.Observations;
        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            record.ScenarioId,
            record.Seed,
            Plan = new
            {
                plan.ObjectCount,
                plan.PlayableSize,
                plan.LatticeSize,
                plan.PlayFrames,
                plan.Repeats,
            },
            RepeatCount = observations.Count,
            Repeats = observations,
            Stages = run.StageSummaries.Select(s => new
            {
                s.Stage,
                Samples = s.Repeats,
                WallMs = new { Mean = s.WallMsMean, SampleVariance = s.WallMsVariance },
                AllocBytesCurrentThread = new { Mean = s.AllocBytesMean, SampleVariance = s.AllocBytesVariance },
                Gc0 = new { Mean = s.Gc0Mean, SampleVariance = s.Gc0Variance },
                Gc1 = new { Mean = s.Gc1Mean, SampleVariance = s.Gc1Variance },
                Gc2 = new { Mean = s.Gc2Mean, SampleVariance = s.Gc2Variance },
            }),
            AllocBytesScope = "current thread only: GC.GetAllocatedBytesForCurrentThread delta per stage bracket",
            ProcessPeakWorkingSet = new
            {
                Bytes = run.ProcessPeakWorkingSetBytes,
                Scope = "process lifetime high-water mark (Process.PeakWorkingSet64), not per stage",
            },
            record.Environment,
            Verification = new
            {
                LoadedCountMatchesAuthored = observations.All(o => o.LoadedObjectCount == o.AuthoredObjectCount),
                LoadedObjectsMatchSaved = observations.All(o => o.LoadedObjectsMatchSaved),
                BrushCenterElevationRaised = observations.All(o => o.BrushCenterElevationDelta > 0),
                RegionUndoRestoredAuthoredState = observations.All(o => o.RegionUndoRestoredAuthoredState),
                MeshMoldSingleUndoEntry = observations.All(o => o.MeshMoldUndoEntriesAdded == 1),
                MeshMoldStrokeChangedHeights = observations.All(o => o.MeshMoldChangedCellCount > 0),
                MeshMoldUndoRestoredPreStroke = observations.All(o => o.MeshMoldUndoRestoredPreStroke),
                MeshMoldRedoReappliedStroke = observations.All(o => o.MeshMoldRedoReappliedStroke),
            },
        }, JsonOptions);
    }

    public static void WriteTo(B4aConsumerRun run, B4aConsumerPlan plan, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        File.WriteAllText(path, Serialize(run, plan));
    }
}
