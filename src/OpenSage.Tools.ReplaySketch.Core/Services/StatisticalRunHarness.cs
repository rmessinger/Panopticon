using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenSage.Tools.ReplaySketch.Assertions;

namespace OpenSage.Tools.ReplaySketch.Services;

// WI-A2-04: multi-sample statistical validation over P0-harness runs. Checks every run's
// correctness invariants independently and reports aggregate metric distributions
// (mean, variance, confidence interval, outliers). Never compares runs against each other:
// same-seed agreement and state-hash parity are not requirements (A2 validation model).

/// <summary>One completed run: its recorded seed, per-run assertion results, and T0 run output.</summary>
public sealed record StatisticalRunSample(
    int RunIndex,
    uint Seed,
    AssertionReport Assertions,
    RunOutput? Output);

public sealed class MetricOutlier
{
    public int RunIndex { get; init; }
    public double Value { get; init; }
}

public sealed class MetricStatistics
{
    public string Name { get; init; } = string.Empty;
    public int SampleCount { get; init; }
    public double Mean { get; init; }
    public double SampleVariance { get; init; }
    public double ConfidenceLow { get; init; }
    public double ConfidenceHigh { get; init; }
    public IReadOnlyList<MetricOutlier> Outliers { get; init; } = [];
}

public sealed class RunInvariantFailure
{
    public int RunIndex { get; init; }
    public uint Seed { get; init; }
    public string AssertionId { get; init; } = string.Empty;
    public string? Message { get; init; }
}

public sealed class StatisticalRunReport
{
    public int RunCount => Samples.Count;
    public IReadOnlyList<StatisticalRunSample> Samples { get; init; } = [];
    public IReadOnlyList<MetricStatistics> Metrics { get; init; } = [];
    public IReadOnlyList<RunInvariantFailure> InvariantFailures { get; init; } = [];

    public bool AllInvariantsPassed => InvariantFailures.Count == 0;

    public MetricStatistics? Metric(string name) => Metrics.FirstOrDefault(m => m.Name == name);

    public string ToJson()
    {
        var payload = new
        {
            runCount = RunCount,
            allInvariantsPassed = AllInvariantsPassed,
            invariantFailures = InvariantFailures.Select(f => new
            {
                run = f.RunIndex,
                seed = f.Seed,
                assertion = f.AssertionId,
                message = f.Message,
            }),
            metrics = Metrics.Select(m => new
            {
                name = m.Name,
                samples = m.SampleCount,
                mean = m.Mean,
                variance = m.SampleVariance,
                ci95 = new[] { m.ConfidenceLow, m.ConfidenceHigh },
                outliers = m.Outliers.Select(o => new { run = o.RunIndex, value = o.Value }),
            }),
        };

        return JsonSerializer.Serialize(payload);
    }
}

public static class StatisticalRunHarness
{
    // Two-sided 95% Student-t critical values by degrees of freedom (n - 1); normal
    // approximation beyond the table.
    private static double TCritical95(int degreesOfFreedom) => degreesOfFreedom switch
    {
        1 => 12.706,
        2 => 4.303,
        3 => 3.182,
        4 => 2.776,
        5 => 2.571,
        6 => 2.447,
        7 => 2.365,
        8 => 2.306,
        9 => 2.262,
        10 => 2.228,
        11 => 2.201,
        12 => 2.179,
        13 => 2.160,
        14 => 2.145,
        15 => 2.131,
        16 => 2.120,
        17 => 2.110,
        18 => 2.101,
        19 => 2.093,
        20 => 2.086,
        21 => 2.080,
        22 => 2.074,
        23 => 2.069,
        24 => 2.064,
        25 => 2.060,
        26 => 2.056,
        27 => 2.052,
        28 => 2.048,
        29 => 2.045,
        _ => 1.960,
    };

    public static StatisticalRunReport Aggregate(IEnumerable<StatisticalRunSample> samples)
    {
        var sampleList = samples.ToList();

        var failures = sampleList
            .SelectMany(sample => sample.Assertions.Results
                .Where(result => !result.Passed)
                .Select(result => new RunInvariantFailure
                {
                    RunIndex = sample.RunIndex,
                    Seed = sample.Seed,
                    AssertionId = result.AssertionId,
                    Message = result.Message,
                }))
            .ToList();

        var metrics = new List<MetricStatistics>();
        foreach (var (name, values) in CollectMetricSeries(sampleList))
        {
            if (values.Count > 0)
            {
                metrics.Add(ComputeStatistics(name, values));
            }
        }

        return new StatisticalRunReport
        {
            Samples = sampleList,
            Metrics = metrics,
            InvariantFailures = failures,
        };
    }

    // The A1-5 T0 metric set: run totals plus each player's final-sample economy metrics,
    // grouped into one series per metric name across runs. Runs without a run output
    // (harness runs that produce metrics only) contribute nothing rather than a zero.
    private static IEnumerable<(string Name, List<(int RunIndex, double Value)> Values)> CollectMetricSeries(
        IReadOnlyList<StatisticalRunSample> samples)
    {
        var series = new Dictionary<string, List<(int RunIndex, double Value)>>();

        void Add(string name, int runIndex, double value)
        {
            if (!series.TryGetValue(name, out var values))
            {
                series[name] = values = [];
            }

            values.Add((runIndex, value));
        }

        foreach (var sample in samples)
        {
            if (sample.Output is null)
            {
                continue;
            }

            Add("totalLogicFrames", sample.RunIndex, sample.Output.TotalLogicFrames);
            Add("wallClockMs", sample.RunIndex, sample.Output.WallClockDurationMilliseconds);

            if (sample.Output.MetricSamples.Count == 0)
            {
                continue;
            }

            var last = sample.Output.MetricSamples[^1];
            foreach (var player in last.Players)
            {
                var label = string.IsNullOrEmpty(player.Player) ? "unnamed" : player.Player;
                Add($"final.money.{label}", sample.RunIndex, player.Money);
                Add($"final.ownedObjects.{label}", sample.RunIndex, player.OwnedObjects);
            }
        }

        return series.Select(kv => (kv.Key, kv.Value));
    }

    internal static MetricStatistics ComputeStatistics(
        string name,
        IReadOnlyList<(int RunIndex, double Value)> samples)
    {
        var values = samples.Select(s => s.Value).ToList();
        var n = values.Count;
        var mean = values.Sum() / n;

        // Sample variance (n - 1 denominator); a single sample has no dispersion.
        var variance = n > 1
            ? values.Sum(v => (v - mean) * (v - mean)) / (n - 1)
            : 0d;

        var t = TCritical95(n - 1);
        var halfWidth = t * Math.Sqrt(variance / n);

        return new MetricStatistics
        {
            Name = name,
            SampleCount = n,
            Mean = mean,
            SampleVariance = variance,
            ConfidenceLow = mean - halfWidth,
            ConfidenceHigh = mean + halfWidth,
            Outliers = FindOutliers(name, mean, samples),
        };
    }

    // Tukey fences: values beyond 1.5x the interquartile range from the nearest quartile.
    private static IReadOnlyList<MetricOutlier> FindOutliers(
        string name,
        double mean,
        IReadOnlyList<(int RunIndex, double Value)> samples)
    {
        var sorted = samples.OrderBy(s => s.Value).ToList();
        if (sorted.Count < 4)
        {
            // Quartiles are not meaningful for tiny samples.
            return [];
        }

        var q1 = Quantile(sorted, 0.25);
        var q3 = Quantile(sorted, 0.75);
        var iqr = q3 - q1;
        var lowFence = q1 - 1.5 * iqr;
        var highFence = q3 + 1.5 * iqr;

        return samples
            .Where(s => s.Value < lowFence || s.Value > highFence)
            .Select(s => new MetricOutlier { RunIndex = s.RunIndex, Value = s.Value })
            .ToList();
    }

    private static double Quantile(List<(int RunIndex, double Value)> sorted, double q)
    {
        var position = q * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        var fraction = position - lower;
        return sorted[lower].Value + fraction * (sorted[upper].Value - sorted[lower].Value);
    }
}
