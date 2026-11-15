namespace Panopticon.Geospatial.Preview;

/// <summary>Stable diagnostic codes of the payload-to-package converter.</summary>
public static class GeneratedMapPackageDiagnosticCodes
{
    public const string ChunkReadFailed = "GEO-PKG-001";

    public const string ImageryDecodeFailed = "GEO-PKG-002";

    public const string ImageryDimensionsMismatch = "GEO-PKG-003";

    public const string ImageryOversized = "GEO-PKG-004";

    public const string ChunkCountOverflow = "GEO-PKG-005";

    public const string LatticeAreaOverflow = "GEO-PKG-006";

    public const string FillCountOverflow = "GEO-PKG-007";

    public const string StableIdDeduplicated = "GEO-PKG-008";

    public const string StableIdSourceConflict = "GEO-PKG-009";

    public const string VectorFeatureConflict = "GEO-PKG-010";

    public const string BuildingConflict = "GEO-PKG-011";

    public const string BuildingHeightUnresolved = "GEO-PKG-012";

    public const string BuildingGenerated = "GEO-PKG-013";

    /// <summary>Fatal: terrain samples are non-finite or outside the resident height grid range; no package is written.</summary>
    public const string HeightQuantization = "GEO-PKG-014";

    /// <summary>Informational: count and maximum error of terrain samples quantized to the resident height grid.</summary>
    public const string HeightQuantizationSummary = "GEO-PKG-015";
}
