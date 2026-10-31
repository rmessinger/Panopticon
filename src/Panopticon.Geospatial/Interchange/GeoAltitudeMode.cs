namespace Panopticon.Geospatial.Interchange;

/// <summary>Altitude interpretation policy for a feature's altitude metadata.</summary>
public enum GeoAltitudeMode
{
    ClampToGround = 0,

    RelativeToGround = 1,

    Absolute = 2,
}
