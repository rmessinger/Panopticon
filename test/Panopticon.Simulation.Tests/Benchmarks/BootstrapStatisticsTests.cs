using System;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// B1-5 statistics: deterministic seeded median bootstrap and the CI-overlap regression rule.
/// </summary>
public class BootstrapStatisticsTests
{
    [Theory]
    [InlineData(new[] { 1.0 }, 1.0)]
    [InlineData(new[] { 3.0, 1.0, 2.0 }, 2.0)]
    [InlineData(new[] { 4.0, 1.0, 2.0, 3.0 }, 2.5)]
    public void MedianCi_ReportsMedian(double[] samples, double expectedMedian)
    {
        var (median, _, _) = BootstrapStatistics.MedianCi(samples, seed: 1, iterations: 1);

        Assert.Equal(expectedMedian, median);
    }

    [Fact]
    public void MedianCi_IsReproducibleForSameSamplesAndSeed()
    {
        var samples = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12 };

        var first = BootstrapStatistics.MedianCi(samples, seed: 42);
        var second = BootstrapStatistics.MedianCi(samples, seed: 42);

        Assert.Equal(first.Median, second.Median);
        Assert.Equal(first.Low, second.Low);
        Assert.Equal(first.High, second.High);
    }

    [Fact]
    public void MedianCi_BracketsMedianWithOrderedBounds()
    {
        var samples = new double[] { 5, 6, 7, 7, 8, 9, 10, 11, 12, 30 };

        var (median, low, high) = BootstrapStatistics.MedianCi(samples, seed: 7);

        Assert.True(low <= median, $"low {low} <= median {median}");
        Assert.True(median <= high, $"median {median} <= high {high}");
    }

    [Fact]
    public void MedianCi_SingleSample_DegeneratesToPoint()
    {
        var (median, low, high) = BootstrapStatistics.MedianCi([3.5], seed: 1);

        Assert.Equal(3.5, median);
        Assert.Equal(3.5, low);
        Assert.Equal(3.5, high);
    }

    [Fact]
    public void Percentile_InterpolatesBetweenRanks()
    {
        Assert.Equal(2.5, BootstrapStatistics.Percentile([1, 2, 3, 4], 0.5));
        Assert.Equal(1.5, BootstrapStatistics.Percentile([1, 2, 3, 4], 1.0 / 6));
        Assert.Equal(4, BootstrapStatistics.Percentile([1, 2, 3, 4], 1.0));
    }

    [Fact]
    public void FromSamples_FillsMetricAndCounts()
    {
        var statistics = BenchmarkStatistics.FromSamples("tick.wallMs", [1, 2, 3], seed: 9);

        Assert.Equal("tick.wallMs", statistics.Metric);
        Assert.Equal(3, statistics.Samples);
        Assert.Equal(2, statistics.Median);
        Assert.Null(statistics.BaselineRunId);
        Assert.Null(statistics.IsRegression);
    }

    [Fact]
    public void IntervalsDiffer_DetectsSeparatedAndOverlappingIntervals()
    {
        var baseline = new BenchmarkStatistics { Metric = "tick.wallMs", Median = 2, ConfidenceLow = 1.8, ConfidenceHigh = 2.2 };
        var same = new BenchmarkStatistics { Metric = "tick.wallMs", Median = 2.1, ConfidenceLow = 1.9, ConfidenceHigh = 2.3 };
        var changed = new BenchmarkStatistics { Metric = "tick.wallMs", Median = 5, ConfidenceLow = 4.8, ConfidenceHigh = 5.2 };

        Assert.False(BenchmarkStatistics.IntervalsDiffer(baseline, same));
        Assert.True(BenchmarkStatistics.IntervalsDiffer(baseline, changed));
    }
}
