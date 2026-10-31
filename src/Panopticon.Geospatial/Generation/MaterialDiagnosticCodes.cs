namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Stable diagnostic codes owned by the material layer generator. GeoDiagnosticCodes is
/// not modified by material cards.
/// </summary>
public static class MaterialDiagnosticCodes
{
    /// <summary>A rule references a material name that is not an entry of the palette.</summary>
    public const string MaterialNameUnknown = "GEO-MAT-001";

    /// <summary>The classification raster's sample format is not one of the categorical integer formats.</summary>
    public const string MaterialSourceFormatInvalid = "GEO-MAT-002";

    /// <summary>A sampled classification value is not a finite integral class value.</summary>
    public const string MaterialValueNotIntegral = "GEO-MAT-003";

    /// <summary>Classification rules were supplied without a classification raster.</summary>
    public const string MaterialClassificationMissing = "GEO-MAT-004";

    /// <summary>The output index buffer would exceed a configured import limit.</summary>
    public const string MaterialOutputLimitExceeded = "GEO-MAT-005";

    /// <summary>A polygon feature due for painting has a non-finite coordinate or non-finite derived geometry.</summary>
    public const string MaterialFeatureInvalid = "GEO-MAT-006";

    /// <summary>The classification raster does not carry exactly one band.</summary>
    public const string MaterialSourceBandCountInvalid = "GEO-MAT-007";

    /// <summary>Vector input or polygon work exceeded a configured import limit.</summary>
    public const string MaterialVectorWorkLimitExceeded = "GEO-MAT-008";
}
