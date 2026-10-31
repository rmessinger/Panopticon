namespace Panopticon.Geospatial.Features;

/// <summary>
/// Stable diagnostic codes owned by the feature clipper. GeoDiagnosticCodes is not
/// modified by feature cards.
/// </summary>
public static class FeatureDiagnosticCodes
{
    /// <summary>A feature lay entirely outside the map rectangle and was dropped.</summary>
    public const string FeatureDroppedOutside = "GEO-FEA-001";

    /// <summary>Projecting a feature into the target CRS failed or produced a non-finite coordinate.</summary>
    public const string FeatureProjectionFailed = "GEO-FEA-002";

    /// <summary>A polygon's topology is invalid, for example a self-intersecting or zero-area ring.</summary>
    public const string FeatureInvalidTopology = "GEO-FEA-003";

    /// <summary>A source coordinate is not finite.</summary>
    public const string FeatureNonFiniteCoordinate = "GEO-FEA-004";

    /// <summary>A feature only touched the map rectangle and left no clipped geometry.</summary>
    public const string FeatureDegenerateAfterClip = "GEO-FEA-005";

    /// <summary>A multi geometry nested deeper than the supported maximum.</summary>
    public const string FeatureNestingTooDeep = "GEO-FEA-006";

    /// <summary>The clipped output exceeded a configured import limit.</summary>
    public const string FeatureOutputLimitExceeded = "GEO-FEA-007";

    /// <summary>A geometry kind the clipper does not support.</summary>
    public const string FeatureUnsupportedGeometry = "GEO-FEA-008";
}
