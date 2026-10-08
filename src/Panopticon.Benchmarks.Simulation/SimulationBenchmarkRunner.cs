using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
        using var metricRecorder = new BenchmarkMetricRecorder(run.Host);
        var stopwatch = Stopwatch.StartNew();
        var measured = run.Host.Run(plan.MeasuredFrames);
        stopwatch.Stop();
        var output = recorder.Complete();

        var environment = EnvironmentRecord.Capture();
        environment.InstallId = run.Game.Definition.Identifier;
        var installPath = Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
        if (!string.IsNullOrEmpty(installPath))
        {
            environment.Env["CNC_GENERALS_PATH"] = installPath;
        }

        BenchmarkRunRecord? baseline = null;
        if (plan.BaselinePath is not null)
        {
            baseline = BenchmarkReportWriter.FromJson(File.ReadAllText(plan.BaselinePath));
        }

        var record = new BenchmarkRunRecord(
            plan.ScenarioId,
            plan.Seed,
            plan.WarmupFrames,
            plan.MeasuredFrames,
            measured.FramesCompleted,
            measured.StopPredicateTriggered,
            stopwatch.Elapsed.TotalMilliseconds)
        {
            Output = output,
            TickSamples = [.. metricRecorder.TickSamples],
            SystemSamples = [.. metricRecorder.SystemSamples],
            Environment = environment,
        };
        record.Statistics.AddRange(BuildStatistics(record, plan.Seed, baseline));
        return record;
    }

    // Internal for the M1 capacity probe (WI-M1-02): the TestHarness probe builds the same
    // B1 statistics from its manually driven measured window, then appends probe metrics.
    internal static List<BenchmarkStatistics> BuildStatistics(
        BenchmarkRunRecord record,
        uint seed,
        BenchmarkRunRecord? baseline)
    {
        var statistics = new List<BenchmarkStatistics>
        {
            BenchmarkStatistics.FromSamples("tick.wallMs", [.. record.TickSamples.Select(t => t.WallMs)], seed),
            BenchmarkStatistics.FromSamples(
                "tick.allocBytes",
                [.. record.TickSamples.Select(t => (double)t.AllocBytes)],
                seed),
        };

        foreach (var group in record.SystemSamples.GroupBy(s => s.System).OrderBy(g => g.Key))
        {
            statistics.Add(BenchmarkStatistics.FromSamples(
                $"system.{group.Key}.spanMs",
                [.. group.Select(s => s.SpanMs)],
                seed));
        }

        if (baseline is not null)
        {
            var baselineRunId = baseline.Output?.RunId ?? $"{baseline.ScenarioId}:{baseline.Seed}";
            foreach (var stat in statistics)
            {
                var baselineStat = baseline.Statistics.FirstOrDefault(s => s.Metric == stat.Metric);
                if (baselineStat is null)
                {
                    continue;
                }

                stat.BaselineRunId = baselineRunId;
                stat.IsRegression = BenchmarkStatistics.IntervalsDiffer(baselineStat, stat);
            }
        }

        return statistics;
    }
}
