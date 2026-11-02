namespace Panopticon.Geospatial.Features;

/// <summary>
/// Position in generated-map game units (X east, Y north, Z up), separate from the
/// source <c>GeoPosition.AltitudeMeters</c> meters retained on the clipped geometry.
/// </summary>
public readonly record struct GamePosition(double X, double Y, double Z);
