#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Profiles;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// WI-A5-26: B1 throughput metrics for the A5 authoring and replay pipeline. Five operations are
/// measured with the manual-mode <see cref="BenchmarkMetricRecorder"/>: scenario-generator
/// expansion (a5.expand), scenario serialization (a5.serialize), run-bundle open incl. the state
/// artifact index scan (a5.bundleOpen), random state seeks (a5.randomSeek), and the visible-frame
/// projection path streaming + LOD batching (a5.visibleProjection). Fixtures are authored in code;
/// no install is required.
/// </summary>
public sealed record A5ThroughputPlan(
    int EntityCount = 10_000,
    int CommandCount = 10_000,
    int RecordingFrames = 250,
    int Iterations = 5,
    int SnapshotInterval = 50)
{
    public static A5ThroughputPlan FullScale { get; } = new();

    public static A5ThroughputPlan SmokeScale { get; } = new(EntityCount: 400, CommandCount: 400, RecordingFrames: 40, Iterations: 3);
}

public static class A5ThroughputBenchmark
{
    private const string ScenarioId = "a5.throughput";
    private const string FixtureScenarioId = "a5.throughput-fixture";
    private const string GridDefinitionName = "AmericaInfantryRanger";

    public static BenchmarkRunRecord Run(A5ThroughputPlan? plan = null)
    {
        plan ??= A5ThroughputPlan.FullScale;
        var root = Path.Combine(Path.GetTempPath(), $"a5-throughput-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var fixtureScenario = BuildFixtureScenario(plan);
        var bundleDirectory = Path.Combine(root, "bundle");
        WriteRecordingBundle(bundleDirectory, plan);
        var stateArtifactPath = Path.Combine(bundleDirectory, RunBundle.StateFileName);

        try
        {
            using var seekReader = new WorldStateArtifactReader(stateArtifactPath, SageGame.CncGenerals);
            var seekRandom = new Random(1);
            using var recorder = new BenchmarkMetricRecorder();
            var totalStopwatch = Stopwatch.StartNew();

            for (var i = 0; i < plan.Iterations; i++)
            {
                recorder.BeginTick((uint)i);

                Measure(recorder, "a5.expand", () => ScenarioGeneratorService.Expand(BuildFixtureScenario(plan)));
                Measure(recorder, "a5.serialize", () =>
                {
                    var expanded = ScenarioGeneratorService.Expand(BuildFixtureScenario(plan));
                    return JsonSerializer.SerializeToUtf8Bytes(expanded).Length;
                });
                Measure(recorder, "a5.bundleOpen", () =>
                {
                    var contents = RunBundle.Load(bundleDirectory);
                    using var reader = new WorldStateArtifactReader(contents.StateArtifactPath, SageGame.CncGenerals);
                    return reader.RecordCount;
                });
                Measure(recorder, "a5.randomSeek", () =>
                {
                    var sum = 0u;
                    for (var s = 0; s < 5; s++)
                    {
                        var frame = (uint)seekRandom.Next((int)seekReader.FirstFrame, (int)seekReader.LastFrame + 1);
                        sum += (uint)seekReader.ReconstructStateAt(frame).Objects.Count;
                    }

                    return sum;
                });
                Measure(recorder, "a5.visibleProjection", () =>
                {
                    var summary = seekReader.ReconstructStateAt(seekReader.LastFrame);
                    var projection = RecordedStateProjector.Project(summary, null);
                    var streaming = new ReplayStreamingService(seekReader, cellSize: 100f, cacheCapacity: 4);
                    var viewport = new WorldViewport(0f, 0f, 10_000f, 10_000f);
                    var streamed = streaming.Query(seekReader.LastFrame, viewport);
                    return ReplayPrimitiveBatcher.BuildBatches(projection.Points.Select(p => p.Point).ToList(), 1f).Stats.BatchCount + streamed.VisibleEntityCount;
                });

                recorder.EndTick((uint)i);
            }

            totalStopwatch.Stop();

            var record = new BenchmarkRunRecord(
                ScenarioId,
                seed: 1,
                warmupFrames: 0,
                measuredFrames: plan.Iterations,
                framesCompleted: plan.Iterations,
                stopPredicateTriggered: false,
                measuredWallMs: totalStopwatch.Elapsed.TotalMilliseconds)
            {
                TickSamples = recorder.TickSamples.ToList(),
                SystemSamples = recorder.SystemSamples.ToList(),
                Environment = EnvironmentRecord.Capture(),
            };
            record.Statistics.AddRange(SimulationBenchmarkRunner.BuildStatistics(record, seed: 1, baseline: null));
            return record;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Measure(BenchmarkMetricRecorder recorder, string system, Func<object?> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        operation();
        stopwatch.Stop();
        recorder.RecordSystemSpan(system, stopwatch.Elapsed);
    }

    private static ReplayScenario BuildFixtureScenario(A5ThroughputPlan plan)
    {
        var gridSide = (int)Math.Ceiling(Math.Sqrt(plan.EntityCount));
        var templateRepeat = Math.Max(1, plan.CommandCount / 10);

        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            DomainId = "rts",
            Seed = 1,
        };
        scenario.Players.Add(new PlayerSlotConfig { Name = "USA", FactionIndex = 2, StartPosition = 1 });
        scenario.Players.Add(new PlayerSlotConfig { Name = "GLA", FactionIndex = 4, StartPosition = 2 });
        scenario.PopulationGenerators.Add(
            new GridPopulationGenerator(0, GridDefinitionName, Rows: (uint)gridSide, Columns: (uint)gridSide, OriginX: 0.5f, OriginY: 0.5f, SpacingX: 0.002f, SpacingY: 0.002f));
        scenario.CommandGenerators.Add(new CommandTimelineGenerator(
            PlayerIndex: 0,
            Template: Enumerable.Range(0, 10)
                .Select(i => new ActionEntry(
                    $"template-{i}",
                    CommandSpec.CreateOpen(Panopticon.Logic.Orders.OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                    position: null,
                    timing: new FixedTiming(10)))
                .ToList(),
            RepeatCount: (uint)templateRepeat,
            StartFrame: 100,
            IntervalFrames: 10_000));
        return scenario;
    }

    private static void WriteRecordingBundle(string bundleDirectory, A5ThroughputPlan plan)
    {
        Directory.CreateDirectory(bundleDirectory);
        var statePath = Path.Combine(bundleDirectory, RunBundle.StateFileName);

        using (var writer = new WorldStateArtifactWriter(statePath, SageGame.CncGenerals))
        {
            var previous = BuildSummary(0, plan.EntityCount);
            writer.WriteSnapshot(previous);
            for (var frame = 1u; frame <= (uint)plan.RecordingFrames; frame++)
            {
                var current = BuildSummary(frame, plan.EntityCount);
                writer.WriteDelta(WorldStateDelta.Diff(frame, previous, current));
                previous = current;
            }
        }

        var manifest = new RunOutput
        {
            Seed = 1,
            ScenarioId = FixtureScenarioId,
            SnapshotArtifactPath = statePath,
            TotalLogicFrames = (uint)plan.RecordingFrames,
        };
        manifest.SaveToFile(Path.Combine(bundleDirectory, RunBundle.ManifestFileName));
        RunBundle.WriteEvents(
            Path.Combine(bundleDirectory, RunBundle.EventsFileName),
            [new RunEventRecord(0, "actionDispatched", "Construct", "USA", 0, "AmericaCommandCenter", null)]);
    }

    private static WorldStateSummary BuildSummary(uint frame, int entityCount)
    {
        var summary = new WorldStateSummary { Frame = frame };
        for (var id = 1u; id <= (uint)entityCount; id++)
        {
            summary.Objects.Add(new WorldObjectState
            {
                ObjectId = id,
                DefinitionName = GridDefinitionName,
                OwnerIndex = (int)(id % 2),
                Position = new System.Numerics.Vector3((frame + id) % 10_000f, id % 500f, 0f),
                Health = 100f - (frame % 50f),
                MaxHealth = 100f,
            });
        }

        summary.PlayerMoney.AddRange([10_000u - frame, 20_000u + frame]);
        return summary;
    }
}
