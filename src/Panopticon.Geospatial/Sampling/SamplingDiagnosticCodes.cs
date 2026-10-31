namespace Panopticon.Geospatial.Sampling;

/// <summary>
/// Stable diagnostic codes owned by the sampling component. GeoDiagnosticCodes is not
/// modified by sampling cards.
/// </summary>
public static class SamplingDiagnosticCodes
{
    /// <summary>An in-raster no-data value was filled by policy.</summary>
    public const string NoDataFilled = "GEO-SMP-001";

    /// <summary>An outside-coverage portion was filled by edge policy.</summary>
    public const string OutsideCoverageFilled = "GEO-SMP-002";

    /// <summary>The no-data policy rejected a sample.</summary>
    public const string NoDataPolicyFailure = "GEO-SMP-003";

    /// <summary>The edge policy rejected a sample.</summary>
    public const string EdgePolicyFailure = "GEO-SMP-004";

    /// <summary>The sample or its inputs are invalid, including non-finite results and failed coordinate transforms.</summary>
    public const string InvalidSample = "GEO-SMP-005";

    /// <summary>A footprint loop or search window exceeded the configured cell bound.</summary>
    public const string ResourceLimit = "GEO-SMP-006";
}
