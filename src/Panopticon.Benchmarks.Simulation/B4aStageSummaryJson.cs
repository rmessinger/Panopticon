using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Serializes a B4a consumer run to stage-summary.json. The file is additive: the B1 run record is
/// still persisted by <see cref="BenchmarkReportWriter"/>. Variances are sample variances (n - 1).
/// Allocation fields count bytes allocated on the measuring thread within each stage bracket; GC
/// fields are process-wide collection-count deltas. The cleanup stage is the play runtime's renderer
/// dispose, which runs inside the play bracket and is split out from it here.
/// </summary>
public static class B4aStageSummaryJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(B4aConsumerRun run, B4aConsumerPlan plan, IReadOnlyList<double> playDisposeMs)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(playDisposeMs);

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
            Stages = run.StageSummaries.Select(s => (object)new
            {
                s.Stage,
                Samples = s.Repeats,
                WallMs = new { Mean = s.WallMsMean, SampleVariance = s.WallMsVariance },
                AllocBytesCurrentThread = new { Mean = s.AllocBytesMean, SampleVariance = s.AllocBytesVariance },
                Gc0 = new { Mean = s.Gc0Mean, SampleVariance = s.Gc0Variance },
                Gc1 = new { Mean = s.Gc1Mean, SampleVariance = s.Gc1Variance },
                Gc2 = new { Mean = s.Gc2Mean, SampleVariance = s.Gc2Variance },
            }).Append(CleanupStage(playDisposeMs)),
            AllocBytesScope = "current thread only: GC.GetAllocatedBytesForCurrentThread delta per stage bracket",
            GcScope = "process-wide: GC.CollectionCount delta per stage bracket, counting collections on all threads",
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
                LoadedTypesMatchPlan = observations.All(o => o.LoadedTypesMatchPlan),
                LoadedHeightsMatchSaved = observations.All(o => o.LoadedHeightsMatchSaved),
                LoadedBlendTilesMatchSaved = observations.All(o => o.LoadedBlendTilesMatchSaved),
            },
        }, JsonOptions);
    }

    public static void WriteTo(B4aConsumerRun run, B4aConsumerPlan plan, string path, IReadOnlyList<double> playDisposeMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        File.WriteAllText(path, Serialize(run, plan, playDisposeMs));
    }

    private static object CleanupStage(IReadOnlyList<double> playDisposeMs)
    {
        var summary = B4aStageSummary.FromRepeats(
            B4aWorldBuilderPlayRuntime.CleanupStage,
            playDisposeMs.Select(ms => new B4aStageRepeat(ms, AllocBytes: 0, Gc0: 0, Gc1: 0, Gc2: 0)).ToList());

        return new
        {
            summary.Stage,
            Samples = summary.Repeats,
            WallMs = new { Mean = summary.WallMsMean, SampleVariance = summary.WallMsVariance },
        };
    }
}
