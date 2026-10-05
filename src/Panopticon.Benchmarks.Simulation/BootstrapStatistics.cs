using System;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Deterministic median bootstrap: resamples with a seeded <see cref="Random"/> so the same
/// samples and seed always produce the same confidence interval (A2 reproducibility stance:
/// repeatable summaries, not same-seed state equality).
/// </summary>
public static class BootstrapStatistics
{
    /// <summary>
    /// Resamples <paramref name="iterations"/> medians and returns the median plus the
    /// percentile confidence interval at <paramref name="confidence"/> (default 95 percent).
    /// </summary>
    public static (double Median, double Low, double High) MedianCi(
        double[] samples,
        uint seed,
        int iterations = 2000,
        double confidence = 0.95)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(confidence, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(confidence, 1);

        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        var median = Percentile(sorted, 0.5);

        if (samples.Length == 1 || iterations == 1)
        {
            return (median, median, median);
        }

        var random = new Random((int)seed);
        var resampledMedians = new double[iterations];
        var scratch = new double[samples.Length];
        for (var i = 0; i < iterations; i++)
        {
            for (var j = 0; j < samples.Length; j++)
            {
                scratch[j] = samples[random.Next(samples.Length)];
            }

            Array.Sort(scratch);
            resampledMedians[i] = Percentile(scratch, 0.5);
        }

        Array.Sort(resampledMedians);
        var alpha = (1 - confidence) / 2;
        return (median, Percentile(resampledMedians, alpha), Percentile(resampledMedians, 1 - alpha));
    }

    /// <summary>
    /// Percentile of already-sorted samples using linear interpolation between ranks.
    /// </summary>
    public static double Percentile(double[] sortedSamples, double probability)
    {
        ArgumentNullException.ThrowIfNull(sortedSamples);
        if (sortedSamples.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(sortedSamples));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(probability, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(probability, 1);

        if (sortedSamples.Length == 1)
        {
            return sortedSamples[0];
        }

        var rank = probability * (sortedSamples.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return lower == upper
            ? sortedSamples[lower]
            : sortedSamples[lower] + (rank - lower) * (sortedSamples[upper] - sortedSamples[lower]);
    }
}
