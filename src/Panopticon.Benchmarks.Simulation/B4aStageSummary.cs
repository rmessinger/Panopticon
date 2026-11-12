using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// One repeat of one stage: the stage bracket's wall time, allocation delta, and GC collection
/// count deltas.
/// </summary>
public sealed record B4aStageRepeat(double WallMs, double AllocBytes, double Gc0, double Gc1, double Gc2);

/// <summary>
/// Per-stage mean and sample variance over the repeats. Variance is the sample variance: the sum
/// of squared deviations divided by (repeats - 1), so a stage's repeats estimate run-to-run spread.
/// </summary>
public sealed record B4aStageSummary(
    string Stage,
    int Repeats,
    double WallMsMean,
    double WallMsVariance,
    double AllocBytesMean,
    double AllocBytesVariance,
    double Gc0Mean,
    double Gc0Variance,
    double Gc1Mean,
    double Gc1Variance,
    double Gc2Mean,
    double Gc2Variance)
{
    public static B4aStageSummary FromRepeats(string stage, IReadOnlyList<B4aStageRepeat> repeats)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(repeats);
        ArgumentOutOfRangeException.ThrowIfLessThan(repeats.Count, 2);

        var wallMs = repeats.Select(r => r.WallMs).ToArray();
        var allocBytes = repeats.Select(r => r.AllocBytes).ToArray();
        var gc0 = repeats.Select(r => r.Gc0).ToArray();
        var gc1 = repeats.Select(r => r.Gc1).ToArray();
        var gc2 = repeats.Select(r => r.Gc2).ToArray();

        return new B4aStageSummary(
            stage,
            repeats.Count,
            Mean(wallMs),
            SampleVariance(wallMs),
            Mean(allocBytes),
            SampleVariance(allocBytes),
            Mean(gc0),
            SampleVariance(gc0),
            Mean(gc1),
            SampleVariance(gc1),
            Mean(gc2),
            SampleVariance(gc2));
    }

    private static double Mean(double[] values)
    {
        return values.Sum() / values.Length;
    }

    private static double SampleVariance(double[] values)
    {
        var mean = Mean(values);
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
    }
}
