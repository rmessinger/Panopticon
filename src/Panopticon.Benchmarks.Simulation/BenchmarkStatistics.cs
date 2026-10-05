using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// One metric summary: median with a seeded bootstrap confidence interval, plus the
/// CI-overlap regression verdict against a stored baseline run (B1 decision: a metric
/// regresses when the baseline and candidate intervals do not overlap; the medians show the
/// direction, so an improvement also flags as changed until compared by hand).
/// </summary>
public class BenchmarkStatistics
{
    public string Metric { get; set; } = string.Empty;

    public int Samples { get; set; }

    public double Median { get; set; }

    public double ConfidenceLow { get; set; }

    public double ConfidenceHigh { get; set; }

    public string? BaselineRunId { get; set; }

    public bool? IsRegression { get; set; }

    public static BenchmarkStatistics FromSamples(string metric, double[] samples, uint seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        var (median, low, high) = BootstrapStatistics.MedianCi(samples, seed);
        return new BenchmarkStatistics
        {
            Metric = metric,
            Samples = samples.Length,
            Median = median,
            ConfidenceLow = low,
            ConfidenceHigh = high,
        };
    }

    /// <summary>
    /// True when the two intervals do not overlap: the candidate differs from the baseline
    /// beyond what resampling noise explains.
    /// </summary>
    public static bool IntervalsDiffer(BenchmarkStatistics baseline, BenchmarkStatistics candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate.ConfidenceLow > baseline.ConfidenceHigh
            || candidate.ConfidenceHigh < baseline.ConfidenceLow;
    }
}
