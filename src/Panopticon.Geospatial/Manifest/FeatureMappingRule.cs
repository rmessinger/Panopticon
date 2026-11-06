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

/// <summary>Height cell lookup adds the map border shift (HeightMap.cs:148-149), then truncates toward zero with C# (int) (HeightMap.cs:150), so shifted coordinates in (-1, 0) map to cell 0; importer domain clipping does not reproduce that alias.</summary>
public enum ManifestMaskCoverage
{
    Sample = 0,

    ConservativeArea,
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
    bool? Buildable = null,
    string? LayerName = null,
    ManifestMaskCoverage? Coverage = null)
{
    public static bool IsTargetCompatible(ManifestFeatureTarget target, ManifestFeatureGeometry geometry) =>
        (target, geometry) switch
        {
            (ManifestFeatureTarget.Landmark or ManifestFeatureTarget.Waypoint or ManifestFeatureTarget.Object or ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Point) => true,
            (ManifestFeatureTarget.Road or ManifestFeatureTarget.Rail or ManifestFeatureTarget.River or ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Line) => true,
            (ManifestFeatureTarget.Water or ManifestFeatureTarget.Material or ManifestFeatureTarget.Passability or ManifestFeatureTarget.Buildability or ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Polygon) => true,
            _ => false,
        };
}
