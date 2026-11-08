#nullable enable

using System;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Geospatial and vertical coordinate transform parameters in the package manifest.</summary>
public sealed record MapPackageGeospatialTransform(
    string? SourceCrs,
    MapPackageQuantity? OriginEasting,
    MapPackageQuantity? OriginNorthing,
    double MetersPerGameUnit,
    string? VerticalDatum,
    MapPackageQuantity? VerticalOrigin,
    double? VerticalExaggeration)
{
    public static MapPackageGeospatialTransform FromModel(GeospatialTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return new MapPackageGeospatialTransform(
            transform.SourceCrs,
            transform.OriginEasting.HasValue ? MapPackageQuantity.FromMapQuantity(transform.OriginEasting.Value) : null,
            transform.OriginNorthing.HasValue ? MapPackageQuantity.FromMapQuantity(transform.OriginNorthing.Value) : null,
            transform.MetersPerGameUnit,
            transform.VerticalDatum,
            transform.VerticalOrigin.HasValue ? MapPackageQuantity.FromMapQuantity(transform.VerticalOrigin.Value) : null,
            transform.VerticalExaggeration);
    }

    public GeospatialTransform ToModel()
    {
        MapQuantity? easting = OriginEasting?.TryToMapQuantity(out var e) == true ? e : null;
        MapQuantity? northing = OriginNorthing?.TryToMapQuantity(out var n) == true ? n : null;
        MapQuantity? vertical = VerticalOrigin?.TryToMapQuantity(out var v) == true ? v : null;
        return new GeospatialTransform(
            SourceCrs,
            easting,
            northing,
            MetersPerGameUnit,
            VerticalDatum,
            vertical,
            VerticalExaggeration);
    }
}
