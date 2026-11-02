using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Tests.Fixtures;

/// <summary>
/// One synthetic GeoTIFF on disk plus the expectations a reader must reproduce. Each fixture
/// owns a per-test temporary directory removed by <see cref="Dispose"/>. <see cref="StoredTransform"/>
/// and <see cref="Transform"/> are null for malformed fixtures, whose tags are invalid by design.
/// </summary>
public sealed class SyntheticGeoTiffFixture : IDisposable
{
    private readonly string _rootDirectory;
    private readonly double[] _expectedSampleValues;

    internal SyntheticGeoTiffFixture(
        string directory,
        string filePath,
        int width,
        int height,
        int bandCount,
        RasterSampleFormat sampleFormat,
        SyntheticGeoTiffLayout layout,
        AffineTransform storedTransform,
        AffineTransform transform,
        bool pixelIsPoint,
        bool usesModelTransformation,
        CoordinateSystemId coordinateSystem,
        bool isSupportedCrs,
        string noDataText,
        SyntheticGeoTiffMalformation malformation,
        ushort[] geoKeys,
        double[] expectedSamples)
    {
        _rootDirectory = directory;
        _expectedSampleValues = expectedSamples;
        FilePath = filePath;
        Width = width;
        Height = height;
        BandCount = bandCount;
        SampleFormat = sampleFormat;
        Layout = layout;
        StoredTransform = storedTransform;
        Transform = transform;
        PixelIsPoint = pixelIsPoint;
        UsesModelTransformation = usesModelTransformation;
        CoordinateSystem = coordinateSystem;
        IsSupportedCrs = isSupportedCrs;
        NoDataText = noDataText;
        Malformation = malformation;
        GeoKeys = geoKeys;
        ExpectedSamples = expectedSamples;
        NoDataValue = NoDataText == null
            ? double.NaN
            : (double.TryParse(NoDataText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN);
    }

    public string FilePath { get; }

    public int Width { get; }

    public int Height { get; }

    public int BandCount { get; }

    public RasterSampleFormat SampleFormat { get; }

    public int BytesPerSample => SampleFormat switch
    {
        RasterSampleFormat.UInt8 => 1,
        RasterSampleFormat.Int16 => 2,
        RasterSampleFormat.UInt16 => 2,
        RasterSampleFormat.Int32 => 4,
        RasterSampleFormat.Float32 => 4,
        _ => throw new NotSupportedException($"Sample format {SampleFormat} has no fixed byte width."),
    };

    public SyntheticGeoTiffLayout Layout { get; }

    public bool IsTiled => Layout == SyntheticGeoTiffLayout.Tiled;

    /// <summary>Literal affine in tags 33550 plus 33922 or tag 34264, in file order.</summary>
    public AffineTransform StoredTransform { get; }

    /// <summary>Expected area-convention affine; equals <see cref="StoredTransform"/> unless <see cref="PixelIsPoint"/>.</summary>
    public AffineTransform Transform { get; }

    public bool PixelIsPoint { get; }

    public bool UsesModelTransformation { get; }

    public CoordinateSystemId CoordinateSystem { get; }

    /// <summary>False when the CRS is well-formed but unsupported by the import contract, EPSG:27700.</summary>
    public bool IsSupportedCrs { get; }

    /// <summary>Raw GDAL_NODATA text, or null when the tag is absent.</summary>
    public string NoDataText { get; }

    /// <summary><see cref="NoDataText"/> parsed invariantly; NaN when absent or unparseable.</summary>
    public double NoDataValue { get; }

    public bool HasNoData => NoDataText != null;

    public SyntheticGeoTiffMalformation Malformation { get; }

    public bool IsMalformed => Malformation != SyntheticGeoTiffMalformation.None;

    /// <summary>Full tag 34735 content as written, including the four header shorts.</summary>
    public IReadOnlyList<ushort> GeoKeys { get; }

    /// <summary>Row-major, band-interleaved expected sample values.</summary>
    public IReadOnlyList<double> ExpectedSamples { get; }

    public double GetExpectedSample(int row, int column, int band = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(row, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Height);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Width);
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(band, BandCount);

        return _expectedSampleValues[(row * Width + column) * BandCount + band];
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
