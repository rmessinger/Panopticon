namespace Panopticon.Geospatial.Preview;

/// <summary>Stable diagnostic codes of the resident preview builder (B4b deep dive section 7).</summary>
public static class PreviewDiagnosticCodes
{
    public const string TextureMapJsonInvalid = "GEO-PRV-001";

    public const string TextureMapDuplicateKey = "GEO-PRV-002";

    public const string TextureMapMissingBinding = "GEO-PRV-003";

    public const string TextureMapUnknownTexture = "GEO-PRV-004";

    public const string CropInvalid = "GEO-PRV-005";

    public const string PreviewOversized = "GEO-PRV-006";

    public const string WaypointNameDuplicate = "GEO-PRV-007";
}
