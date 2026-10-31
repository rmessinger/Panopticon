namespace Panopticon.Geospatial.Manifest;

public enum ManifestFeatureGeometry
{
    Point,

    Line,

    Polygon,
}

public enum ManifestFeatureTarget
{
    Landmark,

    Waypoint,

    Object,

    Road,

    Rail,

    River,

    Annotation,

    Water,

    Material,

    Passability,

    Buildability,
}

public enum ManifestFeatureOrientation
{
    None,

    Fixed,

    AlongFeature,
}

public sealed record FeatureMappingRule(
    string SourceId,
    ManifestFeatureTarget Target,
    ManifestFeatureGeometry? Geometry = null,
    string? Attribute = null,
    string? Value = null,
    string? ObjectDefinition = null,
    ManifestFeatureOrientation? Orientation = null,
    double? OrientationDegrees = null,
    string? Material = null,
    bool? Passable = null,
    bool? Buildable = null)
{
    public static bool IsTargetCompatible(ManifestFeatureTarget target, ManifestFeatureGeometry geometry) =>
        (target, geometry) switch
        {
            (ManifestFeatureTarget.Landmark or ManifestFeatureTarget.Waypoint or ManifestFeatureTarget.Object, ManifestFeatureGeometry.Point) => true,
            (ManifestFeatureTarget.Road or ManifestFeatureTarget.Rail or ManifestFeatureTarget.River or ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Line) => true,
            (ManifestFeatureTarget.Water or ManifestFeatureTarget.Material or ManifestFeatureTarget.Passability or ManifestFeatureTarget.Buildability or ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Polygon) => true,
            _ => false,
        };
}
