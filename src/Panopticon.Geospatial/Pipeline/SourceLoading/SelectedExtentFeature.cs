using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Pipeline.SourceLoading;

/// <summary>
/// The exactly one extent polygon of the import: the feature that owns it and the polygon
/// itself, both in the extent source's own CRS.
/// </summary>
internal sealed record SelectedExtentFeature(GeospatialFeature Feature, GeoPolygon Polygon);
