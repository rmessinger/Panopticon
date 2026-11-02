namespace Panopticon.Geospatial.Manifest;

public enum ManifestCrsKind
{
    Utm,

    TransverseMercator,
}

public enum ManifestHemisphere
{
    North,

    South,
}

public sealed record TargetCoordinateSystemSpec(
    ManifestCrsKind Kind,
    int? UtmZone = null,
    ManifestHemisphere? Hemisphere = null,
    double? CentralMeridianDegrees = null);
