using System.Collections.Generic;

namespace Panopticon.Geospatial.Sampling;

public enum SampleStatus
{
    Sampled = 0,

    Failed = 1,
}

/// <summary>
/// One sampled band value with its policy outcome. <see cref="Value"/> is meaningful only
/// when <see cref="Status"/> is <see cref="SampleStatus.Sampled"/> and is never NaN.
/// </summary>
public sealed record RasterSampleResult
{
    public required SampleStatus Status { get; init; }

    public double Value { get; init; }

    /// <summary>Scaling report for the sample; null when sampling failed before scaling could be computed.</summary>
    public KernelSelection? Selection { get; init; }

    /// <summary>Number of in-raster no-data fill events applied while producing this sample.</summary>
    public int NoDataFills { get; init; }

    /// <summary>Number of outside-coverage fill events applied while producing this sample.</summary>
    public int OutsideFills { get; init; }

    public required IReadOnlyList<GeoDiagnostic> Diagnostics { get; init; }

    public bool IsSampled => Status == SampleStatus.Sampled;
}
