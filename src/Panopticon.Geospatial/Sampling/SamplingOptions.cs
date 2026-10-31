using System;

namespace Panopticon.Geospatial.Sampling;

public enum NoDataPolicy
{
    Fail = 0,

    NearestValid = 1,

    Constant = 2,
}

public enum EdgePolicy
{
    Fail = 0,

    Nearest = 1,

    Constant = 2,
}

/// <summary>
/// No-data and edge policies for one sampling run, mirroring the manifest's raster policy
/// model: coverage outside the source raster is distinct from in-raster no-data, and each
/// is governed separately. Radii are in target-projected meters.
/// </summary>
public sealed record SamplingOptions
{
    public const int DefaultMaxFootprintCells = 1 << 20;

    public SamplingOptions(
        NoDataPolicy noData = NoDataPolicy.Fail,
        double? noDataSearchRadiusMeters = null,
        double? noDataConstantMeters = null,
        EdgePolicy edge = EdgePolicy.Fail,
        double? edgeConstantMeters = null,
        double? edgeSearchRadiusMeters = null,
        int maxFootprintCells = DefaultMaxFootprintCells)
    {
        if (!Enum.IsDefined(noData))
        {
            throw new ArgumentOutOfRangeException(nameof(noData), noData, "Undefined no-data policy.");
        }

        if (!Enum.IsDefined(edge))
        {
            throw new ArgumentOutOfRangeException(nameof(edge), edge, "Undefined edge policy.");
        }

        ValidateOptionalPositiveFinite(noDataSearchRadiusMeters, nameof(noDataSearchRadiusMeters));
        ValidateOptionalFinite(noDataConstantMeters, nameof(noDataConstantMeters));
        ValidateOptionalPositiveFinite(edgeSearchRadiusMeters, nameof(edgeSearchRadiusMeters));
        ValidateOptionalFinite(edgeConstantMeters, nameof(edgeConstantMeters));

        if (noData == NoDataPolicy.NearestValid && noDataSearchRadiusMeters is null)
        {
            throw new ArgumentException(
                "noDataSearchRadiusMeters is required when the no-data policy is 'nearest-valid'.");
        }

        if (noData == NoDataPolicy.Constant && noDataConstantMeters is null)
        {
            throw new ArgumentException("noDataConstantMeters is required when the no-data policy is 'constant'.");
        }

        if (edge == EdgePolicy.Nearest && edgeSearchRadiusMeters is null)
        {
            throw new ArgumentException("edgeSearchRadiusMeters is required when the edge policy is 'nearest'.");
        }

        if (edge == EdgePolicy.Constant && edgeConstantMeters is null)
        {
            throw new ArgumentException("edgeConstantMeters is required when the edge policy is 'constant'.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFootprintCells);

        NoData = noData;
        NoDataSearchRadiusMeters = noDataSearchRadiusMeters;
        NoDataConstantMeters = noDataConstantMeters;
        Edge = edge;
        EdgeConstantMeters = edgeConstantMeters;
        EdgeSearchRadiusMeters = edgeSearchRadiusMeters;
        MaxFootprintCells = maxFootprintCells;
    }

    public NoDataPolicy NoData { get; }

    /// <summary>Search radius for <see cref="NoDataPolicy.NearestValid"/>, in target-projected meters.</summary>
    public double? NoDataSearchRadiusMeters { get; }

    /// <summary>Fill value for <see cref="NoDataPolicy.Constant"/>.</summary>
    public double? NoDataConstantMeters { get; }

    public EdgePolicy Edge { get; }

    /// <summary>Fill value for <see cref="EdgePolicy.Constant"/>.</summary>
    public double? EdgeConstantMeters { get; }

    /// <summary>Search radius for <see cref="EdgePolicy.Nearest"/>, in target-projected meters.</summary>
    public double? EdgeSearchRadiusMeters { get; }

    /// <summary>
    /// Cumulative per-sample work budget in source cell visits: every footprint overlap
    /// computation and every nearest-cell search candidate counts against it. Exceeding
    /// the budget fails the sample with a resource-limit diagnostic.
    /// </summary>
    public int MaxFootprintCells { get; }

    private static void ValidateOptionalFinite(double? value, string paramName)
    {
        if (value is { } number && !double.IsFinite(number))
        {
            throw new ArgumentOutOfRangeException(paramName, number, "Value must be finite.");
        }
    }

    private static void ValidateOptionalPositiveFinite(double? value, string paramName)
    {
        if (value is { } number && (!double.IsFinite(number) || number <= 0d))
        {
            throw new ArgumentOutOfRangeException(paramName, number, "Value must be finite and positive.");
        }
    }
}
