using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;

namespace Panopticon.Tests.Benchmarks;

/// <summary>
/// B4a-19: consumer measurements (author, region edit, save, load, play) run through the B1
/// recorder and statistics. Smoke runs use the install-free <see cref="InstallFreeSmokePlayRuntime"/>;
/// full-scale sizes are asserted as constants and never executed.
/// </summary>
public sealed class B4aConsumerBenchmarkTests
{
    private static readonly string[] Stages = ["b4a.author", "b4a.regionEdit", "b4a.save", "b4a.load", "b4a.play"];

    [Fact]
    public void SmokeRun_EmitsEveryStageAsB1SpanWithThreeRepeats()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());
        var record = run.Record;

        Assert.Equal("b4a.consumer", record.ScenarioId);
        Assert.Equal(3 * Stages.Length, record.TickSamples.Count);
        Assert.Equal(3 * Stages.Length, record.SystemSamples.Count);
        foreach (var stage in Stages)
        {
            Assert.Contains(record.Statistics, s => s.Metric == $"system.{stage}.spanMs" && s.Samples == 3);
        }

        Assert.Contains(record.Statistics, s => s.Metric == "tick.wallMs");
        Assert.Equal(Stages, run.StageSummaries.Select(s => s.Stage));
        Assert.All(run.StageSummaries, s => Assert.Equal(3, s.Repeats));
        Assert.Equal(3, run.Observations.Count);
        Assert.NotNull(record.Environment);
        Assert.Equal(B4aConsumerPlan.SmokeScale.Repeats * B4aConsumerPlan.SmokeScale.PlayFrames, record.MeasuredFrames);
        Assert.Equal(record.MeasuredFrames, record.FramesCompleted);
    }

    [Fact]
    public void SmokeRun_IsStableAcrossRuns()
    {
        var first = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());
        var second = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());

        Assert.Equal(first.Record.ScenarioId, second.Record.ScenarioId);
        Assert.Equal(first.Record.Seed, second.Record.Seed);
        Assert.Equal(first.Record.Statistics.Select(s => s.Metric), second.Record.Statistics.Select(s => s.Metric));
        Assert.True(first.Observations.SequenceEqual(second.Observations));
    }

    [Fact]
    public void SmokeRun_RegionEditMovesAndDeletesSelectedObjectsThenUndoRestores()
    {
        var plan = B4aConsumerPlan.SmokeScale;
        var (expectedMoved, expectedDeleted) = ExpectedStripCounts(plan.ObjectCount);
        var run = B4aConsumerBenchmark.Run(plan, new InstallFreeSmokePlayRuntime());

        Assert.All(run.Observations, o =>
        {
            // CreateFromHeights adds two player start waypoints to the authored objects.
            Assert.Equal(plan.ObjectCount + 2, o.AuthoredObjectCount);
            Assert.True(o.BrushCenterElevationDelta > 0);
            Assert.Equal(expectedMoved, o.RegionMovedCount);
            Assert.Equal(expectedDeleted, o.RegionDeletedCount);
            Assert.Equal(o.AuthoredObjectCount - o.RegionDeletedCount, o.CountAfterDelete);
            Assert.Equal(o.AuthoredObjectCount, o.CountAfterUndo);
            Assert.True(o.RegionUndoRestoredAuthoredState);
        });
    }

    [Fact]
    public void SmokeRun_SaveLoadRoundTripPreservesAuthoredPopulation()
    {
        var plan = B4aConsumerPlan.SmokeScale;
        var runtime = new InstallFreeSmokePlayRuntime();
        var run = B4aConsumerBenchmark.Run(plan, runtime);

        Assert.All(run.Observations, o =>
        {
            Assert.Equal(o.AuthoredObjectCount, o.LoadedObjectCount);
            Assert.True(o.LoadedObjectsMatchSaved);
            Assert.Equal(plan.PlayFrames, o.PlayFramesCompleted);
        });

        Assert.Equal(plan.Repeats, runtime.Checksums.Count);
        Assert.All(runtime.Checksums, checksum => Assert.Equal(runtime.Checksums[0], checksum));
    }

    [Fact]
    public void StageSummary_ComputesExactMeanAndSampleVarianceFromRepeats()
    {
        var repeats = new[] { 2.0, 4.0, 4.0 }
            .Select(w => new B4aStageRepeat(WallMs: w, AllocBytes: w * 10, Gc0: 0, Gc1: 0, Gc2: 0))
            .ToList();

        var summary = B4aStageSummary.FromRepeats("test", repeats);

        Assert.Equal(3, summary.Repeats);
        Assert.Equal(10.0 / 3.0, summary.WallMsMean, 12);

        // Sample variance: squared deviations sum to 8/3, divided by n - 1 = 2, gives 4/3.
        // The population variance over the same samples would be 8/9; the summary does not use it.
        Assert.Equal(4.0 / 3.0, summary.WallMsVariance, 12);

        Assert.Equal(100.0 / 3.0, summary.AllocBytesMean, 12);
        Assert.Equal(400.0 / 3.0, summary.AllocBytesVariance, 12);
        Assert.Equal(0.0, summary.Gc0Mean);
        Assert.Equal(0.0, summary.Gc0Variance);
    }

    [Fact]
    public void StageSummary_RequiresTwoRepeatsForSampleVariance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            B4aStageSummary.FromRepeats("test", [new B4aStageRepeat(1, 0, 0, 0, 0)]));
    }

    [Fact]
    public void SmokeRun_AllocAndGcMeasuresAreNonNegative()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());

        Assert.All(run.Record.TickSamples, tick => Assert.True(tick.AllocBytes >= 0));
        Assert.All(run.StageSummaries, s =>
        {
            Assert.True(s.AllocBytesMean >= 0);
            Assert.True(s.Gc0Mean >= 0 && s.Gc1Mean >= 0 && s.Gc2Mean >= 0);
            Assert.True(s.WallMsVariance >= 0 && s.AllocBytesVariance >= 0);
        });
        Assert.True(run.StageSummaries.Single(s => s.Stage == "b4a.author").AllocBytesMean > 0);
    }

    [Fact]
    public void SmokeRun_ReportsProcessLifetimePeakWorkingSet()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());

        Assert.True(run.ProcessPeakWorkingSetBytes > 0);
        Assert.True(run.ProcessPeakWorkingSetBytes >= run.Record.Environment!.PeakWorkingSetBytes);
    }

    [Fact]
    public void SmokeRun_JsonRoundTripPreservesScenarioAndStageStatistics()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());

        var reloaded = BenchmarkReportWriter.FromJson(BenchmarkReportWriter.ToJson(run.Record));

        Assert.Equal("b4a.consumer", reloaded.ScenarioId);
        Assert.Equal(run.Record.Statistics.Count, reloaded.Statistics.Count);
        Assert.Equal(run.Record.TickSamples.Count, reloaded.TickSamples.Count);
        foreach (var stage in Stages)
        {
            var original = run.Record.Statistics.Single(s => s.Metric == $"system.{stage}.spanMs");
            var roundTripped = reloaded.Statistics.Single(s => s.Metric == original.Metric);
            Assert.Equal(original.Median, roundTripped.Median);
            Assert.Equal(original.ConfidenceLow, roundTripped.ConfidenceLow);
            Assert.Equal(original.ConfidenceHigh, roundTripped.ConfidenceHigh);
            Assert.Equal(3, roundTripped.Samples);
        }
    }

    [Fact]
    public void SmokeRun_CsvCarriesStageSpanRows()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());
        var path = Path.Combine(Path.GetTempPath(), $"b4a-consumer-{Guid.NewGuid():N}.csv");

        try
        {
            BenchmarkReportWriter.WriteCsv(run.Record, path);
            var csv = File.ReadAllText(path);

            foreach (var stage in Stages)
            {
                Assert.Contains($",system.{stage}.meanMs,", csv);
                Assert.Contains($",stat.system.{stage}.spanMs.median,", csv);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SmokePlan_UsesSmallSizesWithThreeRepeats()
    {
        Assert.Equal(200, B4aConsumerPlan.SmokeScale.ObjectCount);
        Assert.Equal(10, B4aConsumerPlan.SmokeScale.PlayFrames);
        Assert.Equal(3, B4aConsumerPlan.SmokeScale.Repeats);
        Assert.Equal(500, B4aConsumerPlan.SmokeScale.PlayableSize);
        Assert.Equal(513, B4aConsumerPlan.SmokeScale.LatticeSize);
    }

    [Fact]
    public void FullScalePlan_MeetsDocumentedSizes()
    {
        Assert.Equal(10_000, B4aConsumerPlan.FullScale.ObjectCount);
        Assert.Equal(300, B4aConsumerPlan.FullScale.PlayFrames);
        Assert.Equal(3, B4aConsumerPlan.FullScale.Repeats);
        Assert.Equal(4096, B4aConsumerPlan.FullScale.PlayableSize);
        Assert.Equal(4109, B4aConsumerPlan.FullScale.LatticeSize);
    }

    [Fact]
    public void SmokeRun_AuthorMeshMoldStrokeAddsOneUndoEntryAndUndoRedoRestoreHeights()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());

        Assert.All(run.Observations, o =>
        {
            Assert.Equal(1, o.MeshMoldUndoEntriesAdded);
            Assert.True(o.MeshMoldChangedCellCount > 0);
            Assert.True(o.MeshMoldUndoRestoredPreStroke);
            Assert.True(o.MeshMoldRedoReappliedStroke);
        });
    }

    [Fact]
    public void SmokeRun_LoadedObjectsMatchSavedAcrossFullPersistedSequence()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());

        Assert.All(run.Observations, o =>
        {
            Assert.True(o.LoadedObjectsMatchSaved);
            // Every placed object carries its placement index property, so properties were compared, not skipped.
            Assert.True(o.SavedPropertyCount >= o.AuthoredObjectCount);
        });
    }

    [Fact]
    public void SmokeRun_LoadedTypesMatchPlanWithOnlyStartWaypointsUnplaced()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());

        Assert.All(run.Observations, o => Assert.True(o.LoadedTypesMatchPlan));
    }

    [Fact]
    public void SmokeRun_LoadedHeightsAndBlendTilesMatchSaved()
    {
        var run = B4aConsumerBenchmark.Run(B4aConsumerPlan.SmokeScale, new InstallFreeSmokePlayRuntime());

        Assert.All(run.Observations, o =>
        {
            Assert.True(o.LoadedHeightsMatchSaved);
            Assert.True(o.LoadedBlendTilesMatchSaved);
        });
    }

    [Fact]
    public void ViewportRenderer_LogicFrameSeamIsInertBeforeInitialize()
    {
        using var renderer = new GameViewportRenderer { Installation = null };

        renderer.EnableFixedLogicTimestep();
        renderer.Initialize(64, 64);

        Assert.False(renderer.IsInitialized);
        Assert.Equal(0u, renderer.CurrentLogicFrame);
        Assert.Equal(0, renderer.LoadedGameObjectCount);
        Assert.Equal(0, renderer.StreamedTerrainPatchCount);
        Assert.Equal(0L, renderer.StreamedTerrainVertexBytes);
    }

    // Independent oracle for the strip regions: the grid is row-major with ceil(sqrt(n)) columns,
    // and each strip covers a quarter of the columns (at least one).
    private static (int Left, int Right) ExpectedStripCounts(int count)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var width = Math.Max(1, columns / 4);
        var left = Enumerable.Range(0, count).Count(i => i % columns < width);
        var right = Enumerable.Range(0, count).Count(i => i % columns >= columns - width);
        return (left, right);
    }
}

/// <summary>
/// Install-free smoke runtime for the b4a.play seam. Opens the saved package through
/// <see cref="EditorMap.OpenPackage(string)"/> and visits every loaded object once per frame,
/// accumulating a checksum over the object positions. It does not boot the game; the real renderer
/// boot implements the same <see cref="IB4aPlayRuntime"/> seam.
/// </summary>
internal sealed class InstallFreeSmokePlayRuntime : IB4aPlayRuntime
{
    public List<double> Checksums { get; } = [];

    public int RunFrames(string packageDirectory, int frames, uint seed)
    {
        var map = EditorMap.OpenPackage(packageDirectory);
        var objects = map.MapFile.ObjectsList.Objects;

        double checksum = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            foreach (var mapObject in objects)
            {
                checksum += mapObject.Position.X * (frame + 1) + mapObject.Position.Y + seed;
            }
        }

        Checksums.Add(checksum);
        return frames;
    }
}
