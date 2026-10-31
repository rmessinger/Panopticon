namespace Panopticon.Geospatial.Features;

/// <summary>
/// Stable diagnostic codes owned by the feature mapper (GEO-MAP scope). GeoDiagnosticCodes
/// is not modified by feature cards.
/// </summary>
public static class FeatureMappingDiagnosticCodes
{
    /// <summary>A mapping rule is malformed: missing source, target, or parameters, an incompatible geometry, or non-finite values.</summary>
    public const string MappingRuleInvalid = "GEO-MAP-001";

    /// <summary>A clipped feature's geometry kind is not supported by the mapper.</summary>
    public const string MappingUnsupportedGeometry = "GEO-MAP-002";

    /// <summary>Sampling the terrain height for a generated point failed or was not finite; no height is fabricated.</summary>
    public const string MappingHeightSampleFailed = "GEO-MAP-003";

    /// <summary>Mapping input or expanded output exceeded a configured import limit.</summary>
    public const string MappingLimitExceeded = "GEO-MAP-004";

    /// <summary>An object rule uses 'along-feature' orientation on a point, which has no defined heading.</summary>
    public const string MappingOrientationUndefined = "GEO-MAP-005";
}
