using System;
using Veldrid;

namespace Panopticon.Graphics;

/// <summary>
/// Per-scene comparison tolerances for the visual regression golden gate.
/// </summary>
public sealed record GoldenTolerance
{
    /// <summary>Largest per-pixel channel difference treated as equal; deltas above it count a pixel as differing.</summary>
    public int MaxChannelDelta { get; init; } = 2;

    /// <summary>Upper bound on the fraction of differing pixels for a pass.</summary>
    public double MaxDifferingPixelFraction { get; init; } = 0.001;

    /// <summary>Upper bound on the absolute move of either image metric for a pass.</summary>
    public double MaxMetricDelta { get; init; } = 0.02;
}

/// <summary>
/// Result of comparing a capture against its golden. A structural mismatch (size or format)
/// yields a failed comparison with a reason rather than an exception, so the gate can report it
/// as one failing scene and keep the remaining scenes' results.
/// </summary>
public sealed record GoldenComparison
{
    public long PixelsCompared { get; init; }

    public long DifferingPixels { get; init; }

    public double DifferingPixelFraction { get; init; }

    /// <summary>Largest per-pixel channel difference observed across the frame.</summary>
    public int MaxChannelDeltaObserved { get; init; }

    public double SharpnessDelta { get; init; }

    public double EdgeAliasingDelta { get; init; }

    public bool Passed { get; init; }

    public string FailureReason { get; init; }
}

/// <summary>
/// Compares two same-size captured frames pixel by pixel and their metric scores, following the
/// G3 golden-gate tolerances. Pure CPU logic; no graphics device required.
/// </summary>
public static class GoldenFrameComparer
{
    public static GoldenComparison Compare(
        CapturedFrame actual,
        CapturedFrame golden,
        ImageQualityScores actualScores,
        ImageQualityScores goldenScores,
        GoldenTolerance tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(golden);
        ArgumentNullException.ThrowIfNull(actualScores);
        ArgumentNullException.ThrowIfNull(goldenScores);

        var limits = tolerance ?? new GoldenTolerance();

        if (actual.Width != golden.Width || actual.Height != golden.Height)
        {
            return StructuralFailure(
                $"Size mismatch: capture is {actual.Width}x{actual.Height}, golden is {golden.Width}x{golden.Height}.");
        }

        if (actual.Format != golden.Format)
        {
            return StructuralFailure($"Format mismatch: capture is {actual.Format}, golden is {golden.Format}.");
        }

        var actualPixels = actual.PixelData;
        var goldenPixels = golden.PixelData;

        long differingPixels = 0;
        var maxDeltaObserved = 0;
        var pixelCount = (long)actual.Width * actual.Height;

        for (var i = 0; i < actualPixels.Length; i += 4)
        {
            var deltaR = Math.Abs(actualPixels[i] - goldenPixels[i]);
            var deltaG = Math.Abs(actualPixels[i + 1] - goldenPixels[i + 1]);
            var deltaB = Math.Abs(actualPixels[i + 2] - goldenPixels[i + 2]);
            var deltaA = Math.Abs(actualPixels[i + 3] - goldenPixels[i + 3]);

            var delta = Math.Max(Math.Max(deltaR, deltaG), Math.Max(deltaB, deltaA));
            if (delta > maxDeltaObserved)
            {
                maxDeltaObserved = delta;
            }

            if (delta > limits.MaxChannelDelta)
            {
                differingPixels++;
            }
        }

        var differingFraction = pixelCount == 0 ? 0 : differingPixels / (double)pixelCount;
        var sharpnessDelta = Math.Abs(actualScores.Sharpness - goldenScores.Sharpness);
        var edgeAliasingDelta = Math.Abs(actualScores.EdgeAliasing - goldenScores.EdgeAliasing);

        string reason = null;
        if (differingFraction > limits.MaxDifferingPixelFraction)
        {
            reason = $"Differing pixel fraction {differingFraction:0.000000} exceeds {limits.MaxDifferingPixelFraction:0.000000}.";
        }
        else if (sharpnessDelta > limits.MaxMetricDelta)
        {
            reason = $"Sharpness moved by {sharpnessDelta:0.000} (limit {limits.MaxMetricDelta:0.000}).";
        }
        else if (edgeAliasingDelta > limits.MaxMetricDelta)
        {
            reason = $"Edge aliasing moved by {edgeAliasingDelta:0.000} (limit {limits.MaxMetricDelta:0.000}).";
        }

        return new GoldenComparison
        {
            PixelsCompared = pixelCount,
            DifferingPixels = differingPixels,
            DifferingPixelFraction = differingFraction,
            MaxChannelDeltaObserved = maxDeltaObserved,
            SharpnessDelta = sharpnessDelta,
            EdgeAliasingDelta = edgeAliasingDelta,
            Passed = reason == null,
            FailureReason = reason,
        };
    }

    private static GoldenComparison StructuralFailure(string reason) => new()
    {
        Passed = false,
        FailureReason = reason,
    };
}
