#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Source-side transform facts as read. <c>MetersPerGameUnit</c> is the declared scale in meters per GameUnit. Origins are <see cref="MapQuantity"/> values; <see cref="MapInterchangeValidator"/> checks their finiteness and unit/frame pairing, and the transform itself performs no conversion.</summary>
public sealed record GeospatialTransform(
    string? SourceCrs,
    MapQuantity? OriginEasting,
    MapQuantity? OriginNorthing,
    double MetersPerGameUnit,
    string? VerticalDatum,
    MapQuantity? VerticalOrigin,
    double? VerticalExaggeration);
