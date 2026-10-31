using System;

namespace Panopticon.Geospatial.Sampling;

/// <summary>
/// Report accumulator for sampling. The sampler records each result; no-data fills and
/// outside-coverage fills are counted separately, and <see cref="AffectedBounds"/> is the
/// union of the footprints of samples that required any fill.
/// </summary>
public sealed class SamplingStats
{
    public long SampleCount { get; private set; }

    public long FailedSampleCount { get; private set; }

    /// <summary>Total in-raster no-data fill events.</summary>
    public long NoDataFillCount { get; private set; }

    /// <summary>Total outside-coverage fill events.</summary>
    public long OutsideFillCount { get; private set; }

    public ProjectedBounds? AffectedBounds { get; private set; }

    public void Record(RasterSampleResult result, SampleFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(footprint);

        SampleCount++;
        NoDataFillCount += result.NoDataFills;
        OutsideFillCount += result.OutsideFills;

        if (result.Status == SampleStatus.Failed)
        {
            FailedSampleCount++;
        }

        if (result.NoDataFills > 0 || result.OutsideFills > 0)
        {
            AffectedBounds = AffectedBounds?.Union(footprint.Bounds) ?? footprint.Bounds;
        }
    }
}
