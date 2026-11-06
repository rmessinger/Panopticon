#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Source-side transform facts as read. <c>MetersPerGameUnit</c> is the declared scale in meters per GameUnit.</summary>
public sealed record GeospatialTransform(
    string? SourceCrs,
    MapQuantity? OriginEasting,
    MapQuantity? OriginNorthing,
    double MetersPerGameUnit,
    string? VerticalDatum,
    MapQuantity? VerticalOrigin,
    double? VerticalExaggeration);
