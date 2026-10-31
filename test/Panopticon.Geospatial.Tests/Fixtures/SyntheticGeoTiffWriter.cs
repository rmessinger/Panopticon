using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BitMiracle.LibTiff.Classic;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Readers;

namespace Panopticon.Geospatial.Tests.Fixtures;

/// <summary>
/// Test-only writer producing small GeoTIFFs with known georeferencing, so readers and
/// generators never need downloaded data (deep dive B4b section 8, card WI-B4b-09). Tags
/// follow GeoTIFF 1.1 (OGC 19-008r4): 33550, 33922, 34264, 34735, and 42113. Tag 33550's
/// ScaleY uses the raster-space sign of section 7.3.2 Requirements 10.4 and 10.5 (positive
/// when model Y decreases with increasing raster row), the reverse of the affine row scale
/// that tag 34264 stores literally.
/// </summary>
public static class SyntheticGeoTiffWriter
{
    public const int RowsPerStrip = 4;

    public const int TileWidth = 16;

    public const int TileHeight = 16;

    private const ushort ModelTypeGeoKey = 1024;
    private const ushort RasterTypeGeoKey = 1025;
    private const ushort GeographicTypeGeoKey = 2048;
    private const ushort ProjectedCSTypeGeoKey = 3072;
    private const ushort VerticalCSTypeGeoKey = 4096;

    /// <summary>
    /// Registers GeoTIFF fields once through the production registration gate shared with
    /// <c>GeoTiffRasterReader</c>, so reader and fixture never install competing LibTiff.NET
    /// tag extenders.
    /// </summary>
    public static void EnsureGeoTiffTagsRegistered()
    {
        GeoTiffTagRegistration.EnsureRegistered();
    }

    /// <summary>
    /// North-up plane elevation z = 10 + slopeX * column + slopeY * row.
    /// </summary>
    public static SyntheticGeoTiffFixture Plane(double slopeX, double slopeY, SyntheticGeoTiffOptions options = null)
    {
        options ??= new SyntheticGeoTiffOptions();

        return Write(
            ScalarSamples(options, (row, column) => 10d + slopeX * column + slopeY * row),
            options);
    }

    /// <summary>Ridge elevation peaking at the middle column: 90 + 10 * row - 20 * |column - center|.</summary>
    public static SyntheticGeoTiffFixture Ridge(SyntheticGeoTiffOptions options = null)
    {
        options ??= new SyntheticGeoTiffOptions();
        var center = (options.Width - 1) / 2;

        return Write(
            ScalarSamples(options, (row, column) => 90d + 10d * row - 20d * Math.Abs(column - center)),
            options);
    }

    /// <summary>Plane(1, 2) with a centre hole set to the no-data sentinel and tag 42113 written.</summary>
    public static SyntheticGeoTiffFixture WithNoDataHole(SyntheticGeoTiffOptions options = null)
    {
        options ??= new SyntheticGeoTiffOptions();

        if (options.NoData == null)
        {
            options = options with { NoData = "-9999" };
        }

        var samples = ScalarSamples(options, (row, column) => 10d + column + 2d * row);
        var holeRow = options.Height / 2;
        var holeColumn = options.Width / 2;
        var sentinel = double.Parse(options.NoData, CultureInfo.InvariantCulture);

        for (var row = Math.Max(0, holeRow - 1); row <= Math.Min(holeRow, options.Height - 1); row++)
        {
            for (var column = Math.Max(0, holeColumn - 1); column <= Math.Min(holeColumn, options.Width - 1); column++)
            {
                samples[row * options.Width + column] = sentinel;
            }
        }

        return Write(samples, options);
    }

    /// <summary>Plane(2, -3) with positive row scale: row 0 is the southern edge.</summary>
    public static SyntheticGeoTiffFixture SouthUp(SyntheticGeoTiffOptions options = null)
    {
        options ??= new SyntheticGeoTiffOptions();

        return Plane(2d, -3d, options with { SouthUp = true });
    }

    /// <summary>
    /// Deterministic uint8 imagery with known control pixels. BandCount 3 writes RGB, 4 writes
    /// RGBA with unassociated alpha.
    /// </summary>
    public static SyntheticGeoTiffFixture Imagery(SyntheticGeoTiffOptions options = null)
    {
        options = (options ?? new SyntheticGeoTiffOptions()) with
        {
            SampleFormat = RasterSampleFormat.UInt8,
            BandCount = options?.BandCount is 3 or 4 ? options.BandCount : 3,
        };

        return Write(ImagerySamples(options), options);
    }

    /// <summary>Single-band uint8 classification; values default to 10, 20, 30, 40 cycling by column + row.</summary>
    public static SyntheticGeoTiffFixture Classification(int[] values = null, SyntheticGeoTiffOptions options = null)
    {
        options = (options ?? new SyntheticGeoTiffOptions()) with { SampleFormat = RasterSampleFormat.UInt8 };

        if (values != null && values.Length != options.Width * options.Height)
        {
            throw new ArgumentException(
                $"values length {values.Length} must equal width x height = {options.Width * options.Height}.");
        }

        return Write(
            ScalarSamples(options, (row, column) =>
                values != null ? values[row * options.Width + column] : 10d * (1d + ((column + row) % 4))),
            options);
    }

    /// <summary>Plane(1, 1) in EPSG:27700, a CRS the import contract does not support.</summary>
    public static SyntheticGeoTiffFixture Epsg27700(SyntheticGeoTiffOptions options = null)
    {
        options ??= new SyntheticGeoTiffOptions();

        return Plane(1d, 1d, options with { Crs = SyntheticGeoTiffCrs.Epsg27700 });
    }

    /// <summary>Plane(1, 1) with geokey 1025 = 2 and a tiepoint at the pixel center.</summary>
    public static SyntheticGeoTiffFixture PixelIsPoint(SyntheticGeoTiffOptions options = null)
    {
        options ??= new SyntheticGeoTiffOptions();

        return Plane(1d, 1d, options with { PixelIsPoint = true });
    }

    /// <summary>Single-band raster of the given format with values (column + 1) * (row + 2).</summary>
    public static SyntheticGeoTiffFixture Scalar(RasterSampleFormat sampleFormat, SyntheticGeoTiffOptions options = null)
    {
        options = (options ?? new SyntheticGeoTiffOptions()) with { SampleFormat = sampleFormat, BandCount = 1 };

        return Write(
            ScalarSamples(options, (row, column) => (double)((column + 1) * (row + 2))),
            options);
    }

    /// <summary>Writes samples (row-major, band-interleaved) as a GeoTIFF fixture.</summary>
    public static SyntheticGeoTiffFixture Write(double[] samples, SyntheticGeoTiffOptions options = null)
    {
        return Write(samples, options, Path.GetTempPath());
    }

    /// <summary>
    /// Writes a fixture under an explicit temp root a single test owns, so directory-lifecycle
    /// assertions never depend on process-global temp state or environment mutation.
    /// </summary>
    internal static SyntheticGeoTiffFixture Write(double[] samples, SyntheticGeoTiffOptions options, string tempRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(tempRoot);
        options ??= new SyntheticGeoTiffOptions();
        ValidateOptions(options, samples);
        EnsureGeoTiffTagsRegistered();

        var georeferencing = ResolveGeoreferencing(options);
        var geoKeys = BuildGeoKeyDirectory(options);
        var directory = Path.Combine(tempRoot, "panopticon-geotiff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "raster.tif");

        try
        {
            using (var tiff = Tiff.Open(filePath, "w"))
            {
                WriteBasicTags(tiff, options);
                WriteGeoTags(tiff, options, georeferencing, geoKeys);
                WritePixels(tiff, options, samples);
            }
        }
        catch
        {
            Directory.Delete(directory, recursive: true);

            throw;
        }

        var isMalformed = options.Malformation != SyntheticGeoTiffMalformation.None;
        var stored = isMalformed ? null : new AffineTransform(
            georeferencing.OriginX,
            georeferencing.PixelSizeX,
            0d,
            georeferencing.OriginY,
            0d,
            georeferencing.RowScale);
        var transform = options.PixelIsPoint && stored != null
            ? new AffineTransform(
                stored.OriginX - georeferencing.PixelSizeX / 2d,
                stored.ScaleX,
                0d,
                stored.OriginY - georeferencing.RowScale / 2d,
                0d,
                stored.ScaleY)
            : stored;

        return new SyntheticGeoTiffFixture(
            directory,
            filePath,
            options.Width,
            options.Height,
            options.BandCount,
            options.SampleFormat,
            options.Layout,
            stored,
            transform,
            options.PixelIsPoint,
            options.UseModelTransformation,
            ToCoordinateSystem(options.Crs),
            options.Crs != SyntheticGeoTiffCrs.Epsg27700,
            options.NoData,
            options.Malformation,
            geoKeys,
            samples);
    }

    private static double[] ScalarSamples(SyntheticGeoTiffOptions options, Func<int, int, double> value)
    {
        if (options.BandCount != 1)
        {
            throw new ArgumentException("Scalar builders require BandCount 1.");
        }

        var samples = new double[options.Width * options.Height];

        for (var row = 0; row < options.Height; row++)
        {
            for (var column = 0; column < options.Width; column++)
            {
                samples[row * options.Width + column] = value(row, column);
            }
        }

        return samples;
    }

    private static double[] ImagerySamples(SyntheticGeoTiffOptions options)
    {
        var samples = new double[options.Width * options.Height * options.BandCount];

        for (var row = 0; row < options.Height; row++)
        {
            for (var column = 0; column < options.Width; column++)
            {
                var index = (row * options.Width + column) * options.BandCount;
                samples[index] = (29d * column + 7d * row) % 251d;
                samples[index + 1] = (11d * column + 61d * row) % 241d;
                samples[index + 2] = (3d * column + 13d * row) % 253d;

                if (options.BandCount == 4)
                {
                    samples[index + 3] = (5d * column + 17d * row) % 256d;
                }
            }
        }

        return samples;
    }

    private static void ValidateOptions(SyntheticGeoTiffOptions options, double[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Height, 1);

        if (options.BandCount is not (1 or 3 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.BandCount, "BandCount must be 1, 3, or 4.");
        }

        if (options.SampleFormat is not (RasterSampleFormat.UInt8 or RasterSampleFormat.Int16
            or RasterSampleFormat.UInt16 or RasterSampleFormat.Int32 or RasterSampleFormat.Float32))
        {
            throw new ArgumentException($"Sample format {options.SampleFormat} is not supported.");
        }

        if (options.BandCount != 1 && options.SampleFormat != RasterSampleFormat.UInt8)
        {
            throw new ArgumentException("Multi-band rasters must be UInt8.");
        }

        var expectedSampleCount = (long)options.Width * options.Height * options.BandCount;

        if (samples.Length != expectedSampleCount)
        {
            throw new ArgumentException(
                $"Samples length {samples.Length} must equal width x height x bands = {expectedSampleCount}.");
        }
    }

    private static Georeferencing ResolveGeoreferencing(SyntheticGeoTiffOptions options)
    {
        var (originX, originY, pixelSizeX, pixelSizeY) = options.Crs switch
        {
            SyntheticGeoTiffCrs.Wgs84Geographic => (2.0, 49.0, 0.25, 0.125),
            SyntheticGeoTiffCrs.Wgs84Utm31North => (600000.0, 5600000.0, 32.0, 16.0),
            SyntheticGeoTiffCrs.Epsg27700 => (400000.0, 200000.0, 25.0, 12.5),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Crs, "Unknown CRS."),
        };

        return new Georeferencing(
            options.OriginX ?? originX,
            options.OriginY ?? originY,
            options.PixelSizeX ?? pixelSizeX,
            (options.SouthUp ? 1d : -1d) * (options.PixelSizeY ?? pixelSizeY));
    }

    private static ushort[] BuildGeoKeyDirectory(SyntheticGeoTiffOptions options)
    {
        var directory = new List<ushort> { 1, 1, options.VerticalCrsEpsg.HasValue ? (ushort)1 : (ushort)0, 0 };

        void AddKey(ushort keyId, ushort value)
        {
            directory.Add(keyId);
            directory.Add(0);
            directory.Add(1);
            directory.Add(value);
        }

        AddKey(ModelTypeGeoKey, options.Crs == SyntheticGeoTiffCrs.Wgs84Geographic ? (ushort)2 : (ushort)1);
        AddKey(RasterTypeGeoKey, options.PixelIsPoint ? (ushort)2 : (ushort)1);

        if (options.Crs == SyntheticGeoTiffCrs.Wgs84Geographic)
        {
            AddKey(GeographicTypeGeoKey, 4326);
        }
        else
        {
            AddKey(ProjectedCSTypeGeoKey, options.Crs == SyntheticGeoTiffCrs.Wgs84Utm31North ? (ushort)32631 : (ushort)27700);
        }

        if (options.VerticalCrsEpsg is { } verticalCrs)
        {
            AddKey(VerticalCSTypeGeoKey, checked((ushort)verticalCrs));
        }

        var keyCount = (directory.Count - 4) / 4;

        if (options.Malformation == SyntheticGeoTiffMalformation.TruncatedGeoKeys)
        {
            keyCount++;
        }

        directory[3] = checked((ushort)keyCount);

        return directory.ToArray();
    }

    private static void WriteBasicTags(Tiff tiff, SyntheticGeoTiffOptions options)
    {
        tiff.SetField(TiffTag.IMAGEWIDTH, options.Width);
        tiff.SetField(TiffTag.IMAGELENGTH, options.Height);
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, options.BandCount);
        tiff.SetField(TiffTag.BITSPERSAMPLE, (short)BitsPerSample(options.SampleFormat));
        tiff.SetField(TiffTag.SAMPLEFORMAT, LibTiffSampleFormat(options.SampleFormat));
        tiff.SetField(TiffTag.PHOTOMETRIC, options.BandCount >= 3 ? Photometric.RGB : Photometric.MINISBLACK);
        tiff.SetField(TiffTag.COMPRESSION, Compression.NONE);
        tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);

        if (options.BandCount == 4)
        {
            tiff.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { 2 });
        }

        if (options.Layout == SyntheticGeoTiffLayout.Tiled)
        {
            tiff.SetField(TiffTag.TILEWIDTH, TileWidth);
            tiff.SetField(TiffTag.TILELENGTH, TileHeight);
        }
        else
        {
            tiff.SetField(TiffTag.ROWSPERSTRIP, RowsPerStrip);
        }
    }

    private static void WriteGeoTags(Tiff tiff, SyntheticGeoTiffOptions options, Georeferencing georeferencing, ushort[] geoKeys)
    {
        if (options.UseModelTransformation)
        {
            tiff.SetField(TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG, 16, ModelTransformationMatrix(georeferencing));
        }
        else
        {
            var pixelScale = PixelScaleValues(options, georeferencing);
            var tiepoint = TiepointValues(options, georeferencing);
            tiff.SetField(TiffTag.GEOTIFF_MODELPIXELSCALETAG, pixelScale.Length, pixelScale);
            tiff.SetField(TiffTag.GEOTIFF_MODELTIEPOINTTAG, tiepoint.Length, tiepoint);
        }

        tiff.SetField(TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG, geoKeys.Length, geoKeys);

        if (options.NoData != null)
        {
            tiff.SetField(TiffTag.GDAL_NODATA, options.NoData);
        }
    }

    private static double[] PixelScaleValues(SyntheticGeoTiffOptions options, Georeferencing georeferencing)
    {
        // GeoTIFF 1.1 (OGC 19-008r4) section 7.3.2, Requirements 10.4 and 10.5: a positive tag
        // ScaleY means model Y decreases as raster row J increases, and an orientation flip is
        // encoded by reversing the sign. The encoded value is therefore the reverse of the
        // affine row scale: positive for north-up, negative for south-up rasters.
        var tagScaleY = -georeferencing.RowScale;

        if (options.Malformation == SyntheticGeoTiffMalformation.PixelScaleTooShort)
        {
            return new[] { georeferencing.PixelSizeX, tagScaleY };
        }

        if (options.Malformation == SyntheticGeoTiffMalformation.ZeroPixelScale)
        {
            return new[] { 0d, tagScaleY, 0d };
        }

        return new[] { georeferencing.PixelSizeX, tagScaleY, 0d };
    }

    private static double[] TiepointValues(SyntheticGeoTiffOptions options, Georeferencing georeferencing)
    {
        if (options.Malformation == SyntheticGeoTiffMalformation.TiepointTooShort)
        {
            return new[] { 0d, 0d, 0d, georeferencing.OriginX };
        }

        return new[] { 0d, 0d, 0d, georeferencing.OriginX, georeferencing.OriginY, 0d };
    }

    private static double[] ModelTransformationMatrix(Georeferencing georeferencing) => new[]
    {
        // Row-major per section 7.3.3 Requirement 11.3; the matrix keeps the affine sign
        // literally, so the row-scale term is the (possibly negative) affine row scale.
        georeferencing.PixelSizeX, 0d, 0d, georeferencing.OriginX,
        0d, georeferencing.RowScale, 0d, georeferencing.OriginY,
        0d, 0d, 1d, 0d,
        0d, 0d, 0d, 1d,
    };

    private static void WritePixels(Tiff tiff, SyntheticGeoTiffOptions options, double[] samples)
    {
        if (options.Layout == SyntheticGeoTiffLayout.Tiled)
        {
            WriteTiledPixels(tiff, options, samples);

            return;
        }

        for (var row = 0; row < options.Height; row++)
        {
            tiff.WriteScanline(EncodeRow(samples, options, row, options.Width, 0), row);
        }
    }

    private static void WriteTiledPixels(Tiff tiff, SyntheticGeoTiffOptions options, double[] samples)
    {
        var tilesAcross = (options.Width + TileWidth - 1) / TileWidth;
        var tilesDown = (options.Height + TileHeight - 1) / TileHeight;

        for (var tileRow = 0; tileRow < tilesDown; tileRow++)
        {
            for (var tileColumn = 0; tileColumn < tilesAcross; tileColumn++)
            {
                var tile = new byte[TileWidth * TileHeight * options.BandCount * BytesPerSample(options.SampleFormat)];

                for (var y = 0; y < TileHeight; y++)
                {
                    var row = tileRow * TileHeight + y;

                    if (row >= options.Height)
                    {
                        break;
                    }

                    var encoded = EncodeRow(samples, options, row, TileWidth, tileColumn * TileWidth);
                    Buffer.BlockCopy(encoded, 0, tile, y * encoded.Length, encoded.Length);
                }

                tiff.WriteTile(tile, tileColumn * TileWidth, tileRow * TileHeight, 0, (short)0);
            }
        }
    }

    private static byte[] EncodeRow(double[] samples, SyntheticGeoTiffOptions options, int row, int width, int columnOffset)
    {
        var rowBytes = new byte[width * options.BandCount * BytesPerSample(options.SampleFormat)];

        for (var column = 0; column < width; column++)
        {
            for (var band = 0; band < options.BandCount; band++)
            {
                var value = columnOffset + column < options.Width
                    ? samples[(row * options.Width + columnOffset + column) * options.BandCount + band]
                    : 0d;

                WriteSample(rowBytes, (column * options.BandCount + band) * BytesPerSample(options.SampleFormat), value, options.SampleFormat);
            }
        }

        return rowBytes;
    }

    private static void WriteSample(byte[] buffer, int offset, double value, RasterSampleFormat format)
    {
        switch (format)
        {
            case RasterSampleFormat.UInt8:
                buffer[offset] = (byte)value;
                break;
            case RasterSampleFormat.Int16:
                Buffer.BlockCopy(BitConverter.GetBytes((short)value), 0, buffer, offset, 2);
                break;
            case RasterSampleFormat.UInt16:
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)value), 0, buffer, offset, 2);
                break;
            case RasterSampleFormat.Int32:
                Buffer.BlockCopy(BitConverter.GetBytes((int)value), 0, buffer, offset, 4);
                break;
            case RasterSampleFormat.Float32:
                Buffer.BlockCopy(BitConverter.GetBytes((float)value), 0, buffer, offset, 4);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported sample format.");
        }
    }

    private static int BitsPerSample(RasterSampleFormat format) => format switch
    {
        RasterSampleFormat.UInt8 => 8,
        RasterSampleFormat.Int16 or RasterSampleFormat.UInt16 => 16,
        RasterSampleFormat.Int32 or RasterSampleFormat.Float32 => 32,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported sample format."),
    };

    private static SampleFormat LibTiffSampleFormat(RasterSampleFormat format) => format switch
    {
        RasterSampleFormat.UInt8 or RasterSampleFormat.UInt16 => SampleFormat.UINT,
        RasterSampleFormat.Int16 or RasterSampleFormat.Int32 => SampleFormat.INT,
        RasterSampleFormat.Float32 => SampleFormat.IEEEFP,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported sample format."),
    };

    private static int BytesPerSample(RasterSampleFormat format) => BitsPerSample(format) / 8;

    private static CoordinateSystemId ToCoordinateSystem(SyntheticGeoTiffCrs crs) => crs switch
    {
        SyntheticGeoTiffCrs.Wgs84Geographic => CoordinateSystemId.Wgs84,
        SyntheticGeoTiffCrs.Wgs84Utm31North => CoordinateSystemId.Utm(31, northernHemisphere: true),
        SyntheticGeoTiffCrs.Epsg27700 => CoordinateSystemId.FromEpsg(27700),
        _ => throw new ArgumentOutOfRangeException(nameof(crs), crs, "Unknown CRS."),
    };

    private readonly record struct Georeferencing(double OriginX, double OriginY, double PixelSizeX, double RowScale);
}
