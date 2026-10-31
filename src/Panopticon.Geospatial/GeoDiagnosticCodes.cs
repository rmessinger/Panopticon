namespace Panopticon.Geospatial;

public static class GeoDiagnosticCodes
{
    public const string ManifestFailure = "GEO-MAN-001";

    public const string ManifestOversized = "GEO-MAN-002";

    public const string ManifestSchemaInvalid = "GEO-MAN-003";

    public const string ManifestMissingRequired = "GEO-MAN-004";

    public const string ManifestValueInvalid = "GEO-MAN-005";

    public const string ManifestSourcePathRejected = "GEO-MAN-006";

    public const string ArchiveFailure = "GEO-ARC-001";

    public const string ParseFailure = "GEO-PAR-001";

    public const string CoordinatesFailure = "GEO-CRD-001";

    public const string CoordinatesUnsupportedCrs = "GEO-CRD-002";

    public const string CoordinatesScaleError = "GEO-CRD-003";

    public const string CoordinatesInvalidExtent = "GEO-CRD-004";

    public const string CoordinatesExtentOverflow = "GEO-CRD-005";

    public const string RasterFailure = "GEO-RAS-001";

    public const string GenerateFailure = "GEO-GEN-001";

    public const string WriteFailure = "GEO-WRI-001";

    public const string ValidateFailure = "GEO-VAL-001";
}
