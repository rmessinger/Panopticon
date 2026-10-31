namespace Panopticon.Geospatial.Interchange;

/// <summary>Geographic point in WGS84 degrees; latitude is positive north, longitude positive east.</summary>
public readonly record struct GeoPoint(double LatitudeDegrees, double LongitudeDegrees)
{
    /// <summary>Returns the point in the geographic Vector2d convention (X = longitude, Y = latitude).</summary>
    public Vector2d ToVector2d()
    {
        return new Vector2d(LongitudeDegrees, LatitudeDegrees);
    }

    /// <summary>Builds a GeoPoint from a geographic Vector2d.</summary>
    public static GeoPoint FromVector2d(Vector2d coordinate)
    {
        return new GeoPoint(coordinate.Y, coordinate.X);
    }
}
