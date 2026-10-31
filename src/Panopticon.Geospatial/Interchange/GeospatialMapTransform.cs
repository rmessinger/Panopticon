using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Panopticon.Geospatial.Interchange;

/// <summary>
/// Retained map-level coordinate and elevation metadata (B4b deep dive section 4). The
/// constructor copies the source coordinate system list.
/// </summary>
public sealed record GeospatialMapTransform
{
    public GeospatialMapTransform(
        IEnumerable<CoordinateSystemId> sourceCoordinateSystems,
        CoordinateSystemId projectedCoordinateSystem,
        Vector2d originEastingNorthing,
        double metersPerGameUnit,
        string verticalDatum,
        double verticalOriginMeters,
        double verticalExaggeration)
    {
        ArgumentNullException.ThrowIfNull(sourceCoordinateSystems);
        ArgumentException.ThrowIfNullOrEmpty(verticalDatum);
        ThrowIfNotFinite(metersPerGameUnit, nameof(metersPerGameUnit));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metersPerGameUnit);
        ThrowIfNotFinite(verticalExaggeration, nameof(verticalExaggeration));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verticalExaggeration);
        ThrowIfNotFinite(verticalOriginMeters, nameof(verticalOriginMeters));
        ThrowIfNotFinite(originEastingNorthing.X, nameof(originEastingNorthing));
        ThrowIfNotFinite(originEastingNorthing.Y, nameof(originEastingNorthing));

        SourceCoordinateSystems = new ReadOnlyCollection<CoordinateSystemId>(
            InterchangeCopy.CopyItems(sourceCoordinateSystems, nameof(sourceCoordinateSystems)));
        ProjectedCoordinateSystem = projectedCoordinateSystem;
        OriginEastingNorthing = originEastingNorthing;
        MetersPerGameUnit = metersPerGameUnit;
        VerticalDatum = verticalDatum;
        VerticalOriginMeters = verticalOriginMeters;
        VerticalExaggeration = verticalExaggeration;
    }

    /// <summary>CRS of every source that contributed samples to the map.</summary>
    public IReadOnlyList<CoordinateSystemId> SourceCoordinateSystems { get; }

    public CoordinateSystemId ProjectedCoordinateSystem { get; }

    /// <summary>Local origin in projected coordinates (X = easting, Y = northing).</summary>
    public Vector2d OriginEastingNorthing { get; }

    public double MetersPerGameUnit { get; }

    /// <summary>Vertical datum of the elevation sources, for example EGM96 or unknown.</summary>
    public string VerticalDatum { get; }

    public double VerticalOriginMeters { get; }

    public double VerticalExaggeration { get; }

    private static void ThrowIfNotFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite.");
        }
    }
}
