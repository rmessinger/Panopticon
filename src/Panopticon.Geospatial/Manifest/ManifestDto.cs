using System.Collections.Generic;

namespace Panopticon.Geospatial.Manifest;

internal sealed record ManifestSourceDto(
    string? Id = null,
    ManifestSourceKind? Kind = null,
    string? Path = null,
    ManifestSourceRole? Role = null,
    string? VerticalDatum = null,
    string? Label = null,
    string? Attribution = null,
    double? HorizontalAccuracyMeters = null,
    double? VerticalAccuracyMeters = null);

internal sealed record TargetCoordinateSystemDto(
    ManifestCrsKind? Type = null,
    int? Zone = null,
    ManifestHemisphere? Hemisphere = null,
    double? CentralMeridian = null);

internal sealed record VerticalPolicyDto(
    double? OriginMeters = null,
    double? Exaggeration = null);

internal sealed record RasterPolicyDto(
    ManifestNoDataPolicy? NoData = null,
    double? NoDataSearchRadiusMeters = null,
    double? NoDataConstantMeters = null,
    ManifestEdgePolicy? Edge = null,
    double? EdgeConstantMeters = null,
    double? EdgeSearchRadiusMeters = null,
    int? ImageryPixelsPerSample = null);

internal sealed record ToleranceDto(
    double? HorizontalMeters = null,
    double? VerticalMeters = null,
    double? RelativeVerticalMeters = null);

internal sealed record MaterialRuleDto(
    string? Material = null,
    int? ClassificationValue = null,
    string? SourceId = null,
    string? Attribute = null,
    string? Value = null);

internal sealed record FeatureMappingDto(
    string? SourceId = null,
    ManifestFeatureTarget? Target = null,
    ManifestFeatureGeometry? Geometry = null,
    string? Attribute = null,
    string? Value = null,
    string? ObjectDefinition = null,
    ManifestFeatureOrientation? Orientation = null,
    double? OrientationDegrees = null,
    string? Material = null,
    bool? Passable = null,
    bool? Buildable = null);

internal sealed record SlopePolicyDto(
    double? ImpassableSlopeDegrees = null,
    double? UnbuildableSlopeDegrees = null);

internal sealed record ManifestDto(
    IReadOnlyList<ManifestSourceDto?>? Sources = null,
    TargetCoordinateSystemDto? TargetCoordinateSystem = null,
    double? MetersPerGameUnit = null,
    string? DefaultMaterial = null,
    string? ExtentFeatureId = null,
    VerticalPolicyDto? VerticalPolicy = null,
    RasterPolicyDto? RasterPolicy = null,
    ToleranceDto? Tolerances = null,
    IReadOnlyList<MaterialRuleDto?>? MaterialRules = null,
    IReadOnlyList<FeatureMappingDto?>? FeatureMappings = null,
    SlopePolicyDto? SlopePolicy = null,
    double? MaxScaleError = null);
