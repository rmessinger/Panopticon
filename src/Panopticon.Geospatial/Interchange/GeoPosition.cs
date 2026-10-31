namespace Panopticon.Geospatial.Interchange;

/// <summary>Geometry coordinate: planar X/Y in the owning CRS plus optional altitude in meters.</summary>
public readonly record struct GeoPosition(Vector2d Coordinate, double? AltitudeMeters = null);
