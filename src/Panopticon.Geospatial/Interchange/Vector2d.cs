namespace Panopticon.Geospatial.Interchange;

/// <summary>
/// Double-precision coordinate pair. Geographic coordinates (EPSG:4326) use X = longitude
/// degrees and Y = latitude degrees; projected coordinates use X = easting and Y = northing.
/// </summary>
public readonly record struct Vector2d(double X, double Y);
