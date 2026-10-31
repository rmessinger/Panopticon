namespace Panopticon.Geospatial.Readers;

/// <summary>GeoTIFF diagnostic codes.</summary>
public static class GeoTiffDiagnosticCodes
{
    /// <summary>The file declares a well-formed CRS outside the supported source set.</summary>
    public const string CoordinatesUnsupportedCrs = GeoDiagnosticCodes.CoordinatesUnsupportedCrs;

    /// <summary>The file declares linear or angular units other than meters and degrees.</summary>
    public const string CoordinatesUnsupportedUnits = GeoDiagnosticCodes.CoordinatesUnsupportedUnits;

    /// <summary>The compression scheme has no configured LibTiff decoder.</summary>
    public const string ParseUnsupportedCompression = "GEO-PAR-004";

    /// <summary>The bits per sample and sample format combination is outside the supported set.</summary>
    public const string ParseUnsupportedSampleFormat = "GEO-PAR-005";

    /// <summary>Band count, planar config, orientation, or photometric would decode wrong values.</summary>
    public const string ParseUnsupportedLayout = "GEO-PAR-006";

    /// <summary>A strip or tile failed to decode: truncated or corrupt pixel data.</summary>
    public const string ParseRasterDataTruncated = "GEO-PAR-007";

    /// <summary>No usable geotransform: neither tag 34264 nor tags 33550 plus 33922.</summary>
    public const string RasterMissingTransform = "GEO-RAS-002";

    /// <summary>The geotransform carries rotation or shear terms.</summary>
    public const string RasterRotationOrShear = "GEO-RAS-003";

    /// <summary>Geotransform values are invalid: wrong count, non-finite, zero scale, or a non-homogeneous matrix.</summary>
    public const string RasterInvalidTransform = "GEO-RAS-004";

    /// <summary>Dimensions, decoded bytes, or sample counts exceed the import limits.</summary>
    public const string RasterLimitExceeded = "GEO-RAS-005";

    /// <summary>The geotransform scales or shears elevation; only identity (or unused) Z mapping is accepted.</summary>
    public const string RasterUnsupportedElevationMapping = "GEO-RAS-006";

    /// <summary>The extra-sample layout cannot round-trip the interchange without silently changing values.</summary>
    public const string RasterIncompatibleLayout = "GEO-RAS-007";
}
