namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Stable diagnostic codes owned by the terrain mask generator. GeoDiagnosticCodes is
/// not modified by mask cards.
/// </summary>
public static class TerrainMaskDiagnosticCodes
{
    /// <summary>Slope thresholds are not finite angles in [0, 90] degrees.</summary>
    public const string TerrainMaskThresholdInvalid = "GEO-MSK-001";

    /// <summary>
    /// A water, passability, or buildability region is not a polygon with a valid rule
    /// index and its typed rule parameters, or its coordinates are not usable.
    /// </summary>
    public const string TerrainMaskRegionInvalid = "GEO-MSK-002";

    /// <summary>Vector input or mask polygon work exceeded a configured import limit.</summary>
    public const string TerrainMaskLimitExceeded = "GEO-MSK-003";

    /// <summary>The playable-area polygon does not convert to finite game coordinates.</summary>
    public const string TerrainMaskPlayableInvalid = "GEO-MSK-004";
}
