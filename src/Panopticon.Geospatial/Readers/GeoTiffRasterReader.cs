using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BitMiracle.LibTiff.Classic;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Readers;

/// <summary>
/// Reads GeoTIFF rasters through LibTiff.NET with in-house GeoTIFF key parsing (deep dive B4b
/// sections 3, 4, and 7). Single-band uint8, int16, uint16, int32, and float32 rasters and
/// contiguous uint8 RGB(A) imagery decode to normalized, band-interleaved doubles. Layouts and
/// metadata the import contract cannot honour are rejected with typed diagnostics before any
/// pixel decode; resource limits are checked before any allocation.
/// </summary>
public sealed class GeoTiffRasterReader : IGeospatialRasterReader
{
    private const ushort MeterUnitsCode = 9001;

    private const ushort DegreeUnitsCode = 9102;

    private const ushort Egm96VerticalCrs = 5773;

    private const ushort Egm2008VerticalCrs = 3855;

    public GeospatialRaster<double>? Read(string path, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(diagnostics);

        try
        {
            return ReadCore(path, limits, diagnostics);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidCastException
            or InvalidOperationException
            or FormatException
            or OverflowException
            or NotSupportedException)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ParseFailure,
                GeoStage.Parse,
                GeoSeverity.Error,
                $"GeoTIFF file could not be read: {ex.Message}",
                path,
                ex));
            return null;
        }
    }

    private static GeospatialRaster<double>? ReadCore(string path, GeospatialImportLimits limits, GeoDiagnosticBag diagnostics)
    {
        GeoTiffTagRegistration.EnsureRegistered();

        if (!TiffDirectoryPreflight.TryValidate(path, limits, diagnostics))
        {
            return null;
        }

        using var tiff = Tiff.Open(path, "r");

        if (tiff is null)
        {
            AddParseError(diagnostics, path, "GeoTIFF file could not be opened by LibTiff.");
            return null;
        }

        var (width, height) = ReadDimensions(tiff);

        if (width <= 0 || height <= 0)
        {
            AddParseError(diagnostics, path, $"GeoTIFF dimensions are missing or invalid: {width} x {height}.");
            return null;
        }

        if (width > limits.MaxRasterWidth || height > limits.MaxRasterHeight)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.RasterLimitExceeded,
                GeoStage.Raster,
                path,
                $"Raster dimensions {width} x {height} exceed the limit of {limits.MaxRasterWidth} x {limits.MaxRasterHeight}.");
            return null;
        }

        var layout = ValidateLayout(tiff, diagnostics, path);

        if (layout is not { } validLayout)
        {
            return null;
        }

        var noData = ResolveNoData(tiff, validLayout, diagnostics, path);

        if (noData is null)
        {
            return null;
        }

        var keys = ReadGeoKeys(tiff, diagnostics, path);

        if (keys is null)
        {
            return null;
        }

        var coordinateSystem = ResolveCoordinateSystem(keys, diagnostics, path);

        if (coordinateSystem is not { } crs)
        {
            return null;
        }

        if (!UnitsAreSupported(keys, diagnostics, path))
        {
            return null;
        }

        var pixelIsPoint = ResolveRasterPixelIsPoint(keys, diagnostics, path);

        if (pixelIsPoint is not { } pointConvention)
        {
            return null;
        }

        var transform = ResolveTransform(tiff, pointConvention, diagnostics, path);

        if (transform is null)
        {
            return null;
        }

        var decode = GuardResources(tiff, width, height, validLayout, limits, diagnostics, path);

        if (decode is not { } decodePlan)
        {
            return null;
        }

        var samples = new double[decodePlan.SampleCount];

        if (!Decode(tiff, samples, validLayout, width, height, diagnostics, path))
        {
            return null;
        }

        var verticalDatum = ResolveVerticalDatum(keys);
        var hasNoData = noData.Value.IsPresent;

        return new GeospatialRaster<double>(
            width,
            height,
            validLayout.BandCount,
            transform,
            crs,
            samples,
            hasNoData ? noData.Value.Value : 0d,
            hasNoData,
            validLayout.Format,
            verticalDatum);
    }

    private static (int Width, int Height) ReadDimensions(Tiff tiff)
    {
        var widthField = tiff.GetField(TiffTag.IMAGEWIDTH);
        var heightField = tiff.GetField(TiffTag.IMAGELENGTH);
        var width = widthField is { Length: > 0 } ? widthField[0].ToInt() : 0;
        var height = heightField is { Length: > 0 } ? heightField[0].ToInt() : 0;

        return (width, height);
    }

    private static RasterLayout? ValidateLayout(Tiff tiff, GeoDiagnosticBag diagnostics, string path)
    {
        var bands = ReadIntTag(tiff, TiffTag.SAMPLESPERPIXEL) ?? 1;

        if (bands is not (1 or 3 or 4))
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.ParseUnsupportedLayout,
                GeoStage.Parse,
                path,
                $"Sample count per pixel {bands} is unsupported; expected 1, 3, or 4 bands.");
            return null;
        }

        var planarConfig = (PlanarConfig)(ReadIntTag(tiff, TiffTag.PLANARCONFIG) ?? (int)PlanarConfig.CONTIG);

        if (planarConfig != PlanarConfig.CONTIG)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.ParseUnsupportedLayout,
                GeoStage.Parse,
                path,
                "Planar configuration SEPARATE is unsupported; samples must be contiguous.");
            return null;
        }

        var orientation = (Orientation)(ReadIntTag(tiff, TiffTag.ORIENTATION) ?? (int)Orientation.TOPLEFT);

        if (orientation != Orientation.TOPLEFT)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.ParseUnsupportedLayout,
                GeoStage.Parse,
                path,
                $"Orientation {orientation} is unsupported; only TOPLEFT is accepted.");
            return null;
        }

        if (!PhotometricMatchesBands(tiff, bands, diagnostics, path))
        {
            return null;
        }

        var format = ResolveSampleFormat(tiff, bands, diagnostics, path);

        if (format is null)
        {
            return null;
        }

        if (bands == 4 && !ExtraSamplesAreAccepted(tiff, diagnostics, path))
        {
            return null;
        }

        var compression = (Compression)(ReadIntTag(tiff, TiffTag.COMPRESSION) ?? (int)Compression.NONE);

        if (!tiff.IsCodecConfigured(compression))
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.ParseUnsupportedCompression,
                GeoStage.Parse,
                path,
                $"Compression scheme {compression} has no configured LibTiff decoder.");
            return null;
        }

        return new RasterLayout(bands, format.Value);
    }

    /// <summary>
    /// Checks EXTRASAMPLES for 4-band RGB rasters. The interchange carries no alpha-association
    /// metadata and downstream PNG expects straight alpha, so associated (premultiplied) alpha
    /// is rejected rather than silently darkened; explicit unassociated alpha (2), and an
    /// unspecified (0) or absent descriptor (read as: the stored fourth band is unassociated
    /// alpha, per the WI-B4b-09 RGBA contract), are accepted. Three-band rasters are unaffected.
    /// </summary>
    private static bool ExtraSamplesAreAccepted(Tiff tiff, GeoDiagnosticBag diagnostics, string path)
    {
        var fields = tiff.GetField(TiffTag.EXTRASAMPLES);

        if (fields is not { Length: > 0 })
        {
            return true;
        }

        var values = fields[fields.Length - 1].ToShortArray();

        if (values is { Length: 1 } descriptor)
        {
            if (descriptor[0] is 0 or 2)
            {
                return true;
            }

            if (descriptor[0] == 1)
            {
                AddError(
                    diagnostics,
                    GeoTiffDiagnosticCodes.RasterIncompatibleLayout,
                    GeoStage.Raster,
                    path,
                    "ExtraSamples associated alpha (1) would darken colors downstream; the interchange requires unassociated alpha (2).");
                return false;
            }
        }

        AddError(
            diagnostics,
            GeoTiffDiagnosticCodes.RasterIncompatibleLayout,
            GeoStage.Raster,
            path,
            $"ExtraSamples ({string.Join(", ", values)}) is not representable for 4-band RGBA rasters; one unassociated alpha (2) or unspecified (0) descriptor is required.");
        return false;
    }

    private static bool PhotometricMatchesBands(Tiff tiff, int bands, GeoDiagnosticBag diagnostics, string path)
    {
        var photometricField = tiff.GetField(TiffTag.PHOTOMETRIC);

        if (photometricField is null or { Length: 0 })
        {
            // TIFF has no default photometric; without the tag the band count alone decides.
            return true;
        }

        var photometric = (Photometric)photometricField[0].ToShort();
        var expected = bands == 1 ? Photometric.MINISBLACK : Photometric.RGB;

        if (photometric != expected)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.ParseUnsupportedLayout,
                GeoStage.Parse,
                path,
                $"Photometric interpretation {photometric} with {bands} bands is unsupported; expected {expected}.");
            return false;
        }

        return true;
    }

    private static RasterSampleFormat? ResolveSampleFormat(Tiff tiff, int bands, GeoDiagnosticBag diagnostics, string path)
    {
        var bits = ReadIntTag(tiff, TiffTag.BITSPERSAMPLE) ?? 1;
        var sampleFormat = (SampleFormat)(ReadIntTag(tiff, TiffTag.SAMPLEFORMAT) ?? (int)SampleFormat.UINT);
        RasterSampleFormat? format = (sampleFormat, bits) switch
        {
            (SampleFormat.UINT, 8) => RasterSampleFormat.UInt8,
            (SampleFormat.UINT, 16) => RasterSampleFormat.UInt16,
            (SampleFormat.INT, 16) => RasterSampleFormat.Int16,
            (SampleFormat.INT, 32) => RasterSampleFormat.Int32,
            (SampleFormat.IEEEFP, 32) => RasterSampleFormat.Float32,
            _ => null,
        };

        if (format is null)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.ParseUnsupportedSampleFormat,
                GeoStage.Parse,
                path,
                $"Bits per sample {bits} with sample format {sampleFormat} is unsupported.");
            return null;
        }

        if (bands != 1 && format != RasterSampleFormat.UInt8)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.ParseUnsupportedSampleFormat,
                GeoStage.Parse,
                path,
                $"Multi-band rasters must be uint8, not {format}.");
            return null;
        }

        return format;
    }

    /// <summary>
    /// Validates GDAL_NODATA (tag 42113) against the sample format before decode. Floating-point
    /// sentinels normalize through the source precision, so -9999.1 matches the decoded
    /// float32 -9999.099609375; integer sentinels must be integral and in range.
    /// </summary>
    private static ResolvedNoData? ResolveNoData(Tiff tiff, RasterLayout layout, GeoDiagnosticBag diagnostics, string path)
    {
        var text = ReadAsciiTag(tiff, TiffTag.GDAL_NODATA);

        if (string.IsNullOrEmpty(text))
        {
            return new ResolvedNoData(false, 0d);
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            AddParseError(diagnostics, path, $"GDAL_NODATA value '{text}' is not a number.");
            return null;
        }

        if (layout.Format == RasterSampleFormat.Float32)
        {
            var normalized = (double)(float)parsed;

            if (double.IsInfinity(normalized))
            {
                AddParseError(diagnostics, path, $"GDAL_NODATA value '{text}' is outside the float32 range.");
                return null;
            }

            return new ResolvedNoData(true, normalized);
        }

        if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed != Math.Truncate(parsed))
        {
            AddParseError(diagnostics, path, $"GDAL_NODATA value '{text}' must be an integer for {layout.Format} rasters.");
            return null;
        }

        if (!WithinIntegerNoDataRange(parsed, layout.Format))
        {
            AddParseError(diagnostics, path, $"GDAL_NODATA value '{text}' is outside the {layout.Format} range.");
            return null;
        }

        return new ResolvedNoData(true, parsed);
    }

    private static bool WithinIntegerNoDataRange(double value, RasterSampleFormat format)
    {
        return format switch
        {
            RasterSampleFormat.UInt8 => value is >= byte.MinValue and <= byte.MaxValue,
            RasterSampleFormat.Int16 => value is >= short.MinValue and <= short.MaxValue,
            RasterSampleFormat.UInt16 => value is >= ushort.MinValue and <= ushort.MaxValue,
            RasterSampleFormat.Int32 => value is >= int.MinValue and <= int.MaxValue,
            _ => false,
        };
    }

    private static GeoKeySet? ReadGeoKeys(Tiff tiff, GeoDiagnosticBag diagnostics, string path)
    {
        var directoryValues = ReadShortArrayTag(tiff, TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG);
        var keys = GeoKeyParser.TryParse(
            directoryValues,
            ReadDoubleArrayTag(tiff, TiffTag.GEOTIFF_GEODOUBLEPARAMSTAG),
            ReadAsciiTag(tiff, TiffTag.GEOTIFF_GEOASCIIPARAMSTAG)?.Length ?? 0,
            diagnostics,
            path);

        if (keys is null && directoryValues is not null)
        {
            return null;
        }

        if (keys is null)
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesFailure,
                GeoStage.Coordinates,
                path,
                "GeoTIFF carries no GeoKey directory, so its coordinate reference system is missing.");
            return null;
        }

        return keys;
    }

    private static CoordinateSystemId? ResolveCoordinateSystem(GeoKeySet keys, GeoDiagnosticBag diagnostics, string path)
    {
        var hasProjected = keys.Contains(GeoKeyParser.ProjectedCSTypeKey);
        var hasGeographic = keys.Contains(GeoKeyParser.GeographicTypeKey);

        if (!hasProjected && !hasGeographic)
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesFailure,
                GeoStage.Coordinates,
                path,
                "GeoKey directory declares no coordinate reference system.");
            return null;
        }

        var modelType = keys.TryGetInteger(GeoKeyParser.ModelTypeKey, out var declaredModelType) ? declaredModelType : 0;

        if (modelType is not (0 or 1 or 2))
        {
            AddParseError(diagnostics, path, $"GeoKey model type {modelType} is not 1 (projected) or 2 (geographic).");
            return null;
        }

        if (hasProjected)
        {
            if (modelType == 2)
            {
                AddParseError(diagnostics, path, "GeoKey model type is geographic but a projected CRS key is present.");
                return null;
            }

            if (!keys.TryGetInteger(GeoKeyParser.ProjectedCSTypeKey, out var projectedCode))
            {
                AddParseError(diagnostics, path, "Projected CRS GeoKey stores no readable value.");
                return null;
            }

            // A geographic key alongside a projected one is the projected CRS's base; it is
            // consistent only when it is the WGS84 base of a WGS84 UTM zone.
            if (hasGeographic)
            {
                if (!keys.TryGetInteger(GeoKeyParser.GeographicTypeKey, out var baseCode))
                {
                    AddParseError(diagnostics, path, "Geographic CRS GeoKey stores no readable value.");
                    return null;
                }

                if (baseCode != CoordinateSystemId.Wgs84Epsg)
                {
                    AddParseError(
                        diagnostics,
                        path,
                        $"Projected CRS EPSG:{projectedCode.ToString(CultureInfo.InvariantCulture)} declares geographic base EPSG:{baseCode.ToString(CultureInfo.InvariantCulture)}, not the required EPSG:4326.");
                    return null;
                }
            }

            if (projectedCode is >= 32601 and <= 32660)
            {
                return CoordinateSystemId.Utm(projectedCode - 32600, northernHemisphere: true);
            }

            if (projectedCode is >= 32701 and <= 32760)
            {
                return CoordinateSystemId.Utm(projectedCode - 32700, northernHemisphere: false);
            }

            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.CoordinatesUnsupportedCrs,
                GeoStage.Coordinates,
                path,
                $"Projected CRS EPSG:{projectedCode.ToString(CultureInfo.InvariantCulture)} is outside the supported source set (WGS84 UTM zones).");
            return null;
        }

        if (modelType == 1)
        {
            AddParseError(diagnostics, path, "GeoKey model type is projected but no projected CRS key is present.");
            return null;
        }

        if (!keys.TryGetInteger(GeoKeyParser.GeographicTypeKey, out var geographicCode))
        {
            AddParseError(diagnostics, path, "Geographic CRS GeoKey stores no readable value.");
            return null;
        }

        if (geographicCode == CoordinateSystemId.Wgs84Epsg)
        {
            return CoordinateSystemId.Wgs84;
        }

        AddError(
            diagnostics,
            GeoTiffDiagnosticCodes.CoordinatesUnsupportedCrs,
            GeoStage.Coordinates,
            path,
            $"Geographic CRS EPSG:{geographicCode.ToString(CultureInfo.InvariantCulture)} is outside the supported source set (EPSG:4326).");
        return null;
    }

    private static bool UnitsAreSupported(GeoKeySet keys, GeoDiagnosticBag diagnostics, string path)
    {
        if (RejectUnits(keys, GeoKeyParser.ProjectedLinearUnitsKey, MeterUnitsCode, "projected linear", diagnostics, path)
            || RejectUnits(keys, GeoKeyParser.GeogLinearUnitsKey, MeterUnitsCode, "geographic linear", diagnostics, path)
            || RejectUnits(keys, GeoKeyParser.GeogAngularUnitsKey, DegreeUnitsCode, "angular", diagnostics, path)
            || RejectUnits(keys, GeoKeyParser.VerticalUnitsKey, MeterUnitsCode, "vertical", diagnostics, path))
        {
            return false;
        }

        return true;
    }

    private static bool RejectUnits(
        GeoKeySet keys,
        ushort keyId,
        ushort supportedCode,
        string unitKind,
        GeoDiagnosticBag diagnostics,
        string path)
    {
        if (!keys.TryGetInteger(keyId, out var code) || code == supportedCode)
        {
            return false;
        }

        AddError(
            diagnostics,
            GeoTiffDiagnosticCodes.CoordinatesUnsupportedUnits,
            GeoStage.Coordinates,
            path,
            $"GeoKey {unitKind} unit code {code} is unsupported; only code {supportedCode} is accepted.");
        return true;
    }

    private static bool? ResolveRasterPixelIsPoint(GeoKeySet keys, GeoDiagnosticBag diagnostics, string path)
    {
        if (!keys.TryGetInteger(GeoKeyParser.RasterTypeKey, out var rasterType) || rasterType == 1)
        {
            return false;
        }

        if (rasterType == 2)
        {
            return true;
        }

        AddParseError(diagnostics, path, $"GeoKey raster type {rasterType} is not 1 (PixelIsArea) or 2 (PixelIsPoint).");

        return null;
    }

    private static string ResolveVerticalDatum(GeoKeySet keys)
    {
        if (!keys.TryGetInteger(GeoKeyParser.VerticalCSTypeKey, out var verticalCrs))
        {
            return "unknown";
        }

        return verticalCrs switch
        {
            Egm96VerticalCrs => "EGM96",
            Egm2008VerticalCrs => "EGM2008",
            _ => "unknown",
        };
    }

    private static AffineTransform? ResolveTransform(Tiff tiff, bool pixelIsPoint, GeoDiagnosticBag diagnostics, string path)
    {
        var matrix = ReadDoubleArrayTag(tiff, TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG);
        var pixelScale = ReadDoubleArrayTag(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG);
        var tiepoint = ReadDoubleArrayTag(tiff, TiffTag.GEOTIFF_MODELTIEPOINTTAG);

        if (matrix is not null && (pixelScale is not null || tiepoint is not null))
        {
            AddParseError(diagnostics, path, "GeoTIFF carries both ModelTransformation and ModelPixelScale or ModelTiepoint tags.");
            return null;
        }

        if (matrix is not null)
        {
            return FromMatrix(matrix, pixelIsPoint, diagnostics, path);
        }

        if (pixelScale is null && tiepoint is null)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.RasterMissingTransform,
                GeoStage.Raster,
                path,
                "GeoTIFF carries neither ModelTransformation nor ModelPixelScale plus ModelTiepoint tags.");
            return null;
        }

        if (pixelScale is null || tiepoint is null)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.RasterMissingTransform,
                GeoStage.Raster,
                path,
                "GeoTIFF geotransform requires both ModelPixelScale and ModelTiepoint tags.");
            return null;
        }

        return FromScaleAndTiepoint(pixelScale, tiepoint, pixelIsPoint, diagnostics, path);
    }

    private static AffineTransform? FromMatrix(double[] matrix, bool pixelIsPoint, GeoDiagnosticBag diagnostics, string path)
    {
        if (matrix.Length != 16)
        {
            AddInvalidTransform(diagnostics, path, $"ModelTransformation carries {matrix.Length} doubles, not the required 16.");
            return null;
        }

        foreach (var value in matrix)
        {
            if (!double.IsFinite(value))
            {
                AddInvalidTransform(diagnostics, path, "ModelTransformation carries a non-finite coefficient.");
                return null;
            }
        }

        // Row-major 4x4 mapping raster (I, J, K, 1) to model (X, Y, Z, 1); an affine
        // geotransform requires the last row to be exactly (0, 0, 0, 1).
        if (matrix[12] != 0d || matrix[13] != 0d || matrix[14] != 0d || matrix[15] != 1d)
        {
            AddInvalidTransform(diagnostics, path, "ModelTransformation is not homogeneous: the last row must be (0, 0, 0, 1).");
            return null;
        }

        // The matrix keeps the affine sign literally; only the scale tag reverses it.
        var scaleX = matrix[0];
        var rowOffsetX = matrix[1];
        var originX = matrix[3];
        var columnOffsetY = matrix[4];
        var scaleY = matrix[5];
        var originY = matrix[7];

        if (rowOffsetX != 0d || columnOffsetY != 0d)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.RasterRotationOrShear,
                GeoStage.Raster,
                path,
                $"Geotransform carries rotation or shear terms (row offset {rowOffsetX.ToString(CultureInfo.InvariantCulture)}, column offset {columnOffsetY.ToString(CultureInfo.InvariantCulture)}).");
            return null;
        }

        // Elevation must map identically (or be unused, Z scale 0): cross terms, scale, or
        // translation in Z would decode wrong heights, so they are rejected up front.
        if (matrix[2] != 0d || matrix[6] != 0d || matrix[8] != 0d || matrix[9] != 0d
            || matrix[11] != 0d || (matrix[10] != 0d && matrix[10] != 1d))
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping,
                GeoStage.Raster,
                path,
                "ModelTransformation maps elevation through cross terms, scale, or translation; only identity Z (or Z scale 0) is supported.");
            return null;
        }

        return BuildTransform(originX, scaleX, originY, scaleY, pixelIsPoint, diagnostics, path);
    }

    private static AffineTransform? FromScaleAndTiepoint(double[] pixelScale, double[] tiepoint, bool pixelIsPoint, GeoDiagnosticBag diagnostics, string path)
    {
        if (pixelScale.Length != 3)
        {
            AddInvalidTransform(diagnostics, path, $"ModelPixelScale carries {pixelScale.Length} doubles, not the required 3.");
            return null;
        }

        if (tiepoint.Length == 0 || tiepoint.Length % 6 != 0)
        {
            AddInvalidTransform(diagnostics, path, $"ModelTiepoint carries {tiepoint.Length} doubles, not a multiple of six.");
            return null;
        }

        foreach (var value in pixelScale.Concat(tiepoint))
        {
            if (!double.IsFinite(value))
            {
                AddInvalidTransform(diagnostics, path, "ModelPixelScale or ModelTiepoint carries a non-finite value.");
                return null;
            }
        }

        var scaleX = pixelScale[0];

        // GeoTIFF 1.1 (OGC 19-008r4) section 7.3.2: a positive tag ScaleY means model Y
        // decreases as raster row increases, so the affine row scale is the reversed sign.
        var scaleY = -pixelScale[1];

        if (scaleX == 0d || scaleY == 0d)
        {
            AddInvalidTransform(diagnostics, path, $"ModelPixelScale scale ({scaleX.ToString(CultureInfo.InvariantCulture)}, {pixelScale[1].ToString(CultureInfo.InvariantCulture)}) has a zero component.");
            return null;
        }

        // ScaleZ 0 is the common "elevation unused" encoding; 1 maps samples to metres. Any
        // other elevation scale would decode wrong heights, so it is rejected up front.
        var scaleZ = pixelScale[2];

        if (scaleZ != 0d && scaleZ != 1d)
        {
            AddError(
                diagnostics,
                GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping,
                GeoStage.Raster,
                path,
                $"ModelPixelScale ScaleZ {scaleZ.ToString(CultureInfo.InvariantCulture)} is not 0 (unused) or 1 (metres); elevation scaling is unsupported.");
            return null;
        }

        // ScaleZ 0 declares the Z axis unused, so tiepoints may only carry model Z 0 (the
        // common all-zero form); a nonzero Z would still shift heights and is rejected
        // exactly like a Z translation in the matrix form. With ScaleZ 1 every tiepoint
        // must declare the identity elevation mapping (model Z equals raster K).
        for (var index = 0; index < tiepoint.Length / 6; index++)
        {
            var offset = index * 6;
            var modelZ = tiepoint[offset + 5];

            if (scaleZ == 0d)
            {
                if (modelZ != 0d)
                {
                    AddError(
                        diagnostics,
                        GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping,
                        GeoStage.Raster,
                        path,
                        $"ModelTiepoint {index.ToString(CultureInfo.InvariantCulture)} declares model Z {modelZ.ToString(CultureInfo.InvariantCulture)} while ScaleZ 0 leaves elevation unused; a nonzero Z offset is unsupported.");
                    return null;
                }
            }
            else if (modelZ - tiepoint[offset + 2] != 0d)
            {
                AddError(
                    diagnostics,
                    GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping,
                    GeoStage.Raster,
                    path,
                    $"ModelTiepoint {index.ToString(CultureInfo.InvariantCulture)} maps raster K {tiepoint[offset + 2].ToString(CultureInfo.InvariantCulture)} to model Z {modelZ.ToString(CultureInfo.InvariantCulture)}; only the identity Z mapping is supported.");
                return null;
            }
        }

        // Prefer the first tiepoint anchored at a nonzero raster index; the origin then
        // back-solves along the scale basis.
        var entryIndex = 0;

        for (var index = 0; index < tiepoint.Length; index += 6)
        {
            if (tiepoint[index] != 0d || tiepoint[index + 1] != 0d)
            {
                entryIndex = index;
                break;
            }
        }

        var originX = tiepoint[entryIndex + 3] - tiepoint[entryIndex] * scaleX;
        var originY = tiepoint[entryIndex + 4] - tiepoint[entryIndex + 1] * scaleY;

        // Every tiepoint must agree with the derived origin and scale, not only the one the
        // origin was solved from; redundant ties are validated, contradictory ties rejected.
        for (var index = 0; index < tiepoint.Length / 6; index++)
        {
            var offset = index * 6;
            var derivedX = originX + tiepoint[offset] * scaleX;
            var derivedY = originY + tiepoint[offset + 1] * scaleY;

            if (!double.IsFinite(derivedX) || !double.IsFinite(derivedY))
            {
                AddInvalidTransform(diagnostics, path, "ModelTiepoint arithmetic derives a non-finite coordinate.");
                return null;
            }

            if (!CoordinatesAgree(derivedX, tiepoint[offset + 3]) || !CoordinatesAgree(derivedY, tiepoint[offset + 4]))
            {
                AddInvalidTransform(
                    diagnostics,
                    path,
                    $"ModelTiepoint {index.ToString(CultureInfo.InvariantCulture)} ({tiepoint[offset].ToString(CultureInfo.InvariantCulture)}, {tiepoint[offset + 1].ToString(CultureInfo.InvariantCulture)}) -> ({tiepoint[offset + 3].ToString(CultureInfo.InvariantCulture)}, {tiepoint[offset + 4].ToString(CultureInfo.InvariantCulture)}) contradicts the transform derived from the other tiepoints.");
                return null;
            }
        }

        return BuildTransform(originX, scaleX, originY, scaleY, pixelIsPoint, diagnostics, path);
    }

    /// <summary>Compares a derived and a declared coordinate to a 16-ULP tolerance of the coordinate magnitude.</summary>
    private static bool CoordinatesAgree(double derived, double declared)
    {
        var spread = Math.Abs(derived - declared);

        if (spread == 0d)
        {
            return true;
        }

        var magnitude = Math.Max(Math.Abs(derived), Math.Abs(declared));
        var unitInLastPlace = Math.Abs(Math.BitIncrement(magnitude) - magnitude);

        return spread <= 16d * unitInLastPlace;
    }

    private static AffineTransform? BuildTransform(
        double originX,
        double scaleX,
        double originY,
        double scaleY,
        bool pixelIsPoint,
        GeoDiagnosticBag diagnostics,
        string path)
    {
        if (pixelIsPoint)
        {
            // Shift from the point convention (origin at the first sample centre) to the area
            // convention the interchange uses (origin at the first sample corner).
            originX -= scaleX / 2d;
            originY -= scaleY / 2d;
        }

        try
        {
            return new AffineTransform(originX, scaleX, 0d, originY, 0d, scaleY);
        }
        catch (ArgumentException ex)
        {
            AddInvalidTransform(diagnostics, path, ex.Message, ex);
            return null;
        }
    }

    private static DecodedBufferPlan? GuardResources(
        Tiff tiff,
        int width,
        int height,
        RasterLayout layout,
        GeospatialImportLimits limits,
        GeoDiagnosticBag diagnostics,
        string path)
    {
        var sampleCount = (long)width * height * layout.BandCount;
        var normalizedBytes = sampleCount * sizeof(double);
        var decodeBytes = (long)(tiff.IsTiled() ? tiff.TileSize() : tiff.ScanlineSize());

        if (normalizedBytes > limits.MaxRasterDecodedBytes)
        {
            AddLimitError(diagnostics, path, $"Normalized sample buffer of {normalizedBytes} bytes exceeds the decoded-byte limit of {limits.MaxRasterDecodedBytes}.");
            return null;
        }

        if (decodeBytes > limits.MaxRasterDecodedBytes)
        {
            AddLimitError(diagnostics, path, $"Strip or tile decode buffer of {decodeBytes} bytes exceeds the decoded-byte limit of {limits.MaxRasterDecodedBytes}.");
            return null;
        }

        if (sampleCount > int.MaxValue)
        {
            AddLimitError(diagnostics, path, $"Sample count {sampleCount} exceeds the int-sized sample buffer capacity.");
            return null;
        }

        if (tiff.IsTiled())
        {
            var tileWidth = tiff.GetField(TiffTag.TILEWIDTH)?[0].ToInt() ?? 0;
            var expectedRowBytes = (long)tileWidth * layout.BandCount * layout.BytesPerSample;

            if (tiff.TileRowSize() != expectedRowBytes)
            {
                AddParseError(diagnostics, path, $"Tile row carries {tiff.TileRowSize()} bytes, expected {expectedRowBytes} for the declared layout.");
                return null;
            }
        }
        else
        {
            var expectedRowBytes = (long)width * layout.BandCount * layout.BytesPerSample;

            if (tiff.ScanlineSize() != expectedRowBytes)
            {
                AddParseError(diagnostics, path, $"Scanline carries {tiff.ScanlineSize()} bytes, expected {expectedRowBytes} for the declared layout.");
                return null;
            }
        }

        return new DecodedBufferPlan((int)sampleCount);
    }

    private static bool Decode(
        Tiff tiff,
        double[] samples,
        RasterLayout layout,
        int width,
        int height,
        GeoDiagnosticBag diagnostics,
        string path)
    {
        if (tiff.IsTiled())
        {
            return DecodeTiles(tiff, samples, layout, width, height, diagnostics, path);
        }

        return DecodeStrips(tiff, samples, layout, width, height, diagnostics, path);
    }

    private static bool DecodeStrips(
        Tiff tiff,
        double[] samples,
        RasterLayout layout,
        int width,
        int height,
        GeoDiagnosticBag diagnostics,
        string path)
    {
        var rowBuffer = new byte[tiff.ScanlineSize()];

        for (var row = 0; row < height; row++)
        {
            if (!tiff.ReadScanline(rowBuffer, row))
            {
                AddTruncatedError(diagnostics, path, $"strip decode failed at row {row}");
                return false;
            }

            DecodeContiguousRow(rowBuffer, samples, row, width, width, 0, layout);
        }

        return true;
    }

    private static bool DecodeTiles(
        Tiff tiff,
        double[] samples,
        RasterLayout layout,
        int width,
        int height,
        GeoDiagnosticBag diagnostics,
        string path)
    {
        var tileWidth = tiff.GetField(TiffTag.TILEWIDTH)?[0].ToInt() ?? 0;
        var tileHeight = tiff.GetField(TiffTag.TILELENGTH)?[0].ToInt() ?? 0;
        var tileBuffer = new byte[tiff.TileSize()];
        var tileRowSize = tiff.TileRowSize();

        for (var tileRow = 0; tileRow * tileHeight < height; tileRow++)
        {
            for (var tileColumn = 0; tileColumn * tileWidth < width; tileColumn++)
            {
                var tileX = tileColumn * tileWidth;
                var tileY = tileRow * tileHeight;
                var read = tiff.ReadTile(tileBuffer, 0, tileX, tileY, 0, 0);

                if (read < tileBuffer.Length)
                {
                    AddTruncatedError(diagnostics, path, $"tile decode failed at tile ({tileX}, {tileY})");
                    return false;
                }

                var rowsInTile = Math.Min(tileHeight, height - tileY);
                var columnsInTile = Math.Min(tileWidth, width - tileX);
                var rowBytes = columnsInTile * layout.BandCount * layout.BytesPerSample;

                for (var rowInTile = 0; rowInTile < rowsInTile; rowInTile++)
                {
                    DecodeContiguousRow(
                        tileBuffer.AsSpan(rowInTile * tileRowSize, rowBytes),
                        samples,
                        tileY + rowInTile,
                        width,
                        columnsInTile,
                        tileX,
                        layout);
                }
            }
        }

        return true;
    }

    private static void DecodeContiguousRow(
        ReadOnlySpan<byte> source,
        double[] samples,
        int row,
        int rasterWidth,
        int columnCount,
        int columnOffset,
        RasterLayout layout)
    {
        for (var column = 0; column < columnCount; column++)
        {
            for (var band = 0; band < layout.BandCount; band++)
            {
                var byteIndex = (column * layout.BandCount + band) * layout.BytesPerSample;
                var value = layout.Format switch
                {
                    RasterSampleFormat.UInt8 => (double)source[byteIndex],
                    RasterSampleFormat.Int16 => (double)BitConverter.ToInt16(source.Slice(byteIndex, 2)),
                    RasterSampleFormat.UInt16 => (double)BitConverter.ToUInt16(source.Slice(byteIndex, 2)),
                    RasterSampleFormat.Int32 => (double)BitConverter.ToInt32(source.Slice(byteIndex, 4)),
                    RasterSampleFormat.Float32 => (double)BitConverter.ToSingle(source.Slice(byteIndex, 4)),
                    _ => throw new NotSupportedException($"Sample format {layout.Format}."),
                };
                samples[((long)row * rasterWidth + columnOffset + column) * layout.BandCount + band] = value;
            }
        }
    }

    private static int? ReadIntTag(Tiff tiff, TiffTag tag)
    {
        var fields = tiff.GetField(tag);

        return fields is { Length: > 0 } ? fields[0].ToInt() : null;
    }

    private static double[]? ReadDoubleArrayTag(Tiff tiff, TiffTag tag)
    {
        var fields = tiff.GetField(tag);

        return fields is { Length: > 0 } ? fields[fields.Length - 1].ToDoubleArray() : null;
    }

    private static ushort[]? ReadShortArrayTag(Tiff tiff, TiffTag tag)
    {
        var fields = tiff.GetField(tag);

        if (fields is not { Length: > 0 })
        {
            return null;
        }

        var values = fields[fields.Length - 1].ToShortArray();
        var result = new ushort[values.Length];

        for (var index = 0; index < values.Length; index++)
        {
            result[index] = (ushort)values[index];
        }

        return result;
    }

    private static string? ReadAsciiTag(Tiff tiff, TiffTag tag)
    {
        var fields = tiff.GetField(tag);

        if (fields is not { Length: > 0 })
        {
            return null;
        }

        return fields[0].ToString()?.TrimEnd('\0', ' ');
    }

    private static void AddParseError(GeoDiagnosticBag diagnostics, string path, string message, Exception? detail = null)
    {
        diagnostics.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.ParseFailure,
            GeoStage.Parse,
            GeoSeverity.Error,
            message,
            path,
            detail));
    }

    private static void AddError(GeoDiagnosticBag diagnostics, string code, GeoStage stage, string path, string message)
    {
        diagnostics.Add(new GeoDiagnostic(code, stage, GeoSeverity.Error, message, path));
    }

    private static void AddInvalidTransform(GeoDiagnosticBag diagnostics, string path, string message, Exception? detail = null)
    {
        diagnostics.Add(new GeoDiagnostic(
            GeoTiffDiagnosticCodes.RasterInvalidTransform,
            GeoStage.Raster,
            GeoSeverity.Error,
            message,
            path,
            detail));
    }

    private static void AddLimitError(GeoDiagnosticBag diagnostics, string path, string message)
    {
        AddError(diagnostics, GeoTiffDiagnosticCodes.RasterLimitExceeded, GeoStage.Raster, path, message);
    }

    private static void AddTruncatedError(GeoDiagnosticBag diagnostics, string path, string context)
    {
        AddError(diagnostics, GeoTiffDiagnosticCodes.ParseRasterDataTruncated, GeoStage.Parse, path, $"GeoTIFF pixel data is truncated or corrupt: {context}.");
    }

    private readonly record struct RasterLayout(int BandCount, RasterSampleFormat Format)
    {
        public int BytesPerSample => Format switch
        {
            RasterSampleFormat.UInt8 => 1,
            RasterSampleFormat.Int16 or RasterSampleFormat.UInt16 => 2,
            RasterSampleFormat.Int32 or RasterSampleFormat.Float32 => 4,
            _ => throw new NotSupportedException($"Sample format {Format}."),
        };
    }

    private readonly record struct DecodedBufferPlan(int SampleCount);

    private readonly record struct ResolvedNoData(bool IsPresent, double Value);
}
