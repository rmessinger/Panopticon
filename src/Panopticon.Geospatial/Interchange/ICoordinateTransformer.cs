namespace Panopticon.Geospatial.Interchange;

/// <summary>Transforms points between supported coordinate reference systems.</summary>
public interface ICoordinateTransformer
{
    /// <summary>
    /// Transforms <paramref name="point"/> from <paramref name="source"/> to
    /// <paramref name="target"/>. Geographic points use the Vector2d convention
    /// X = longitude, Y = latitude; projected points use X = easting, Y = northing.
    /// Implementations throw for unsupported systems; their TryTransform overloads report
    /// the same failures as diagnostics instead of throwing.
    /// </summary>
    Vector2d Transform(Vector2d point, CoordinateSystemId source, CoordinateSystemId target);
}
