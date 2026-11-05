using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>One closed range of observed values, for example sampled source GSD per axis.</summary>
public readonly record struct PayloadObservedRange
{
    public PayloadObservedRange(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), $"The range [{min}, {max}] must be finite and ordered.");
        }

        Min = min;
        Max = max;
    }

    public double Min { get; }

    public double Max { get; }
}

/// <summary>
/// Minimum and maximum values observed across every recorded kernel selection. A member
/// is null when no selection supplied a finite value for it; non-finite observations are
/// skipped rather than clamped.
/// </summary>
public sealed record PayloadObservedRanges
{
    public PayloadObservedRanges(
        PayloadObservedRange? sourceGsdXMeters = null,
        PayloadObservedRange? sourceGsdYMeters = null,
        PayloadObservedRange? outputSpacingXMeters = null,
        PayloadObservedRange? outputSpacingYMeters = null,
        PayloadObservedRange? gsdRatioX = null,
        PayloadObservedRange? gsdRatioY = null)
    {
        SourceGsdXMeters = sourceGsdXMeters;
        SourceGsdYMeters = sourceGsdYMeters;
        OutputSpacingXMeters = outputSpacingXMeters;
        OutputSpacingYMeters = outputSpacingYMeters;
        GsdRatioX = gsdRatioX;
        GsdRatioY = gsdRatioY;
    }

    public PayloadObservedRange? SourceGsdXMeters { get; }

    public PayloadObservedRange? SourceGsdYMeters { get; }

    public PayloadObservedRange? OutputSpacingXMeters { get; }

    public PayloadObservedRange? OutputSpacingYMeters { get; }

    public PayloadObservedRange? GsdRatioX { get; }

    public PayloadObservedRange? GsdRatioY { get; }
}

/// <summary>Sample count recorded for one resampling kernel.</summary>
public readonly record struct PayloadKernelSampleCount
{
    public PayloadKernelSampleCount(SampleKernel kernel, long count)
    {
        if (!Enum.IsDefined(kernel))
        {
            throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Undefined kernel.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(count);

        Kernel = kernel;
        Count = count;
    }

    public SampleKernel Kernel { get; }

    public long Count { get; }
}

/// <summary>
/// Immutable snapshot of a <see cref="SamplingStats"/> accumulator: fill and failure
/// counters, one count per kernel in enum order, and the observed GSD, spacing, and
/// ratio ranges from every selection, failed samples included.
/// </summary>
public sealed record PayloadSamplingSummary
{
    public PayloadSamplingSummary(
        long sampleCount,
        long failedSampleCount,
        long noDataFillCount,
        long outsideFillCount,
        IReadOnlyList<PayloadKernelSampleCount> kernelCounts,
        PayloadObservedRanges? observedRanges = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        ArgumentOutOfRangeException.ThrowIfNegative(failedSampleCount);
        ArgumentOutOfRangeException.ThrowIfNegative(noDataFillCount);
        ArgumentOutOfRangeException.ThrowIfNegative(outsideFillCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(failedSampleCount, sampleCount);
        ArgumentNullException.ThrowIfNull(kernelCounts);

        var expectedKernelCount = Enum.GetValues<SampleKernel>().Length;
        if (kernelCounts.Count != expectedKernelCount)
        {
            throw new ArgumentException(
                $"The kernel count list must hold exactly one entry per kernel ({expectedKernelCount}); found {kernelCounts.Count}.",
                nameof(kernelCounts));
        }

        var copy = new PayloadKernelSampleCount[kernelCounts.Count];
        var totalCount = 0L;
        for (var index = 0; index < kernelCounts.Count; index++)
        {
            var entry = kernelCounts[index];
            copy[index] = entry;

            if (index > 0 && (int)entry.Kernel <= (int)copy[index - 1].Kernel)
            {
                throw new ArgumentException(
                    "The kernel count list must list every kernel exactly once, in enum order.",
                    nameof(kernelCounts));
            }

            // Difference guard instead of a running sum: long.MaxValue counts must not wrap.
            if (entry.Count > sampleCount - totalCount)
            {
                throw new ArgumentException(
                    $"The kernel counts exceed the {sampleCount} recorded samples.",
                    nameof(kernelCounts));
            }

            totalCount += entry.Count;
        }

        SampleCount = sampleCount;
        FailedSampleCount = failedSampleCount;
        NoDataFillCount = noDataFillCount;
        OutsideFillCount = outsideFillCount;
        KernelCounts = new ReadOnlyCollection<PayloadKernelSampleCount>(copy);
        ObservedRanges = observedRanges;
    }

    public long SampleCount { get; }

    public long FailedSampleCount { get; }

    /// <summary>Total in-raster no-data fill events.</summary>
    public long NoDataFillCount { get; }

    /// <summary>Total outside-coverage fill events.</summary>
    public long OutsideFillCount { get; }

    /// <summary>One entry per <see cref="SampleKernel"/> in enum order.</summary>
    public IReadOnlyList<PayloadKernelSampleCount> KernelCounts { get; }

    public PayloadObservedRanges? ObservedRanges { get; }
}
