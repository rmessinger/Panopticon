using System;
using System.Diagnostics;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Executes one plan through its scenario: warmup frames are stepped unrecorded, then the
/// measured frames run under the A1 recorder. The B1-2 skeleton times the measured phase with
/// a wall-clock stopwatch; tick, allocation, GC, and memory samples arrive in B1-3.
/// </summary>
public sealed class SimulationBenchmarkRunner
{
    public BenchmarkRunRecord Run(SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var scenario = SimulationBenchmarkScenario.Create(plan.ScenarioId);
        using var run = scenario.CreateRun(plan);
        scenario.ConfigureOrders(run.Host, plan);

        run.Host.Run(plan.WarmupFrames);

        using var recorder = new RunOutputRecorder(run.Host, run.Game, plan.Seed, plan.ScenarioId);
        var stopwatch = Stopwatch.StartNew();
        var measured = run.Host.Run(plan.MeasuredFrames);
        stopwatch.Stop();
        var output = recorder.Complete();

        return new BenchmarkRunRecord(
            plan.ScenarioId,
            plan.Seed,
            plan.WarmupFrames,
            plan.MeasuredFrames,
            measured.FramesCompleted,
            measured.StopPredicateTriggered,
            stopwatch.Elapsed.TotalMilliseconds)
        {
            Output = output,
        };
    }
}
