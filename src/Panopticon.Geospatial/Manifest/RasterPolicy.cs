namespace Panopticon.Geospatial.Manifest;

public enum ManifestNoDataPolicy
{
    Fail,

    NearestValid,

    Constant,
}

public enum ManifestEdgePolicy
{
    Fail,

    Nearest,

    Constant,
}

public sealed record RasterPolicy(
    ManifestNoDataPolicy NoData = ManifestNoDataPolicy.Fail,
    double? NoDataSearchRadiusMeters = null,
    double? NoDataConstantMeters = null,
    ManifestEdgePolicy Edge = ManifestEdgePolicy.Fail,
    double? EdgeConstantMeters = null,
    double? EdgeSearchRadiusMeters = null,
    int ImageryPixelsPerSample = 10);
