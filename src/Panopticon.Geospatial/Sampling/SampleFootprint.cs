using System;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Sampling;

/// <summary>Axis-aligned rectangle in a projected CRS, in meters.</summary>
public sealed record ProjectedBounds
{
    public ProjectedBounds(double minEasting, double minNorthing, double maxEasting, double maxNorthing)
    {
        if (!double.IsFinite(minEasting) || !double.IsFinite(minNorthing)
            || !double.IsFinite(maxEasting) || !double.IsFinite(maxNorthing))
        {
            throw new ArgumentException("Projected bounds must be finite.");
        }

        if (minEasting >= maxEasting || minNorthing >= maxNorthing)
        {
            throw new ArgumentException("Projected bounds must have positive width and height.");
        }

        MinEasting = minEasting;
        MinNorthing = minNorthing;
        MaxEasting = maxEasting;
        MaxNorthing = maxNorthing;
    }

    public double MinEasting { get; }

    public double MinNorthing { get; }

    public double MaxEasting { get; }

    public double MaxNorthing { get; }

    public double WidthMeters => MaxEasting - MinEasting;

    public double HeightMeters => MaxNorthing - MinNorthing;

    public ProjectedBounds Union(ProjectedBounds other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ProjectedBounds(
            Math.Min(MinEasting, other.MinEasting),
            Math.Min(MinNorthing, other.MinNorthing),
            Math.Max(MaxEasting, other.MaxEasting),
            Math.Max(MaxNorthing, other.MaxNorthing));
    }
}

/// <summary>
/// The output sample's footprint before inverse warping: its rectangle in target
/// projected meters plus the target CRS. The sampler maps this rectangle into source
/// pixel space; the sample center is passed separately, already in source-CRS coordinates.
/// </summary>
public sealed record SampleFootprint
{
    public SampleFootprint(ProjectedBounds bounds, CoordinateSystemId targetCoordinateSystem)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        if (targetCoordinateSystem.EpsgCode == CoordinateSystemId.Wgs84Epsg)
        {
            throw new ArgumentException("The footprint must be in a projected CRS; EPSG:4326 is geographic.");
        }

        Bounds = bounds;
        TargetCoordinateSystem = targetCoordinateSystem;
    }

    public ProjectedBounds Bounds { get; }

    public CoordinateSystemId TargetCoordinateSystem { get; }
}
