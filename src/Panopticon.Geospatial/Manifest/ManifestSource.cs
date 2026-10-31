namespace Panopticon.Geospatial.Manifest;

public enum ManifestSourceKind
{
    Kml,

    Kmz,

    Geotiff,
}

public enum ManifestSourceRole
{
    Extent,

    Elevation,

    Imagery,

    Classification,

    Vector,
}

public sealed record ManifestSource(
    string Id,
    ManifestSourceKind Kind,
    string Path,
    ManifestSourceRole Role,
    string VerticalDatum = "unknown",
    string? Label = null,
    string? Attribution = null,
    double? HorizontalAccuracyMeters = null,
    double? VerticalAccuracyMeters = null);
