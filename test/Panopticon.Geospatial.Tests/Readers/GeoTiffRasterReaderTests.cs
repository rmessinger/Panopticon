using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Readers;
using Panopticon.Geospatial.Tests.Fixtures;
using Xunit;

namespace Panopticon.Geospatial.Tests.Readers;

public class GeoTiffRasterReaderTests
{
    private const ushort ImageWidthTag = 256;

    private const ushort ImageLengthTag = 257;

    private const ushort BitsPerSampleTag = 258;

    private const ushort CompressionTag = 259;

    private const ushort PhotometricTag = 262;

    private const ushort StripOffsetsTag = 273;

    private const ushort OrientationTag = 274;

    private const ushort SamplesPerPixelTag = 277;

    private const ushort RowsPerStripTag = 278;

    private const ushort StripByteCountsTag = 279;

    private const ushort PlanarConfigTag = 284;

    private const ushort SampleFormatTag = 339;

    private const ushort ExtraSamplesTag = 338;

    private const ushort PixelScaleTag = 33550;

    private const ushort TiepointTag = 33922;

    private const ushort ModelTransformationTag = 34264;

    private const ushort GeoKeyDirectoryTag = 34735;

    private const ushort GeoDoubleParamsTag = 34736;

    public static readonly TheoryData<RasterSampleFormat> ScalarFormats =
        new() { RasterSampleFormat.UInt8, RasterSampleFormat.Int16, RasterSampleFormat.UInt16, RasterSampleFormat.Int32, RasterSampleFormat.Float32 };

    private static (GeospatialRaster<double> Raster, GeoDiagnosticBag Diagnostics) ReadRaster(
        string path,
        GeospatialImportLimits limits = null)
    {
        var diagnostics = new GeoDiagnosticBag();
        var raster = new GeoTiffRasterReader().Read(path, limits ?? new GeospatialImportLimits(), diagnostics);

        return (raster, diagnostics);
    }

    private static GeoDiagnostic AssertSingleError(GeoDiagnosticBag diagnostics, string code, GeoStage stage)
    {
        var error = Assert.Single(diagnostics.Items, static item => item.Severity == GeoSeverity.Error);

        Assert.Equal(code, error.Code);
        Assert.Equal(stage, error.Stage);

        return error;
    }

    private static void AssertAllSamplesMatch(SyntheticGeoTiffFixture fixture, GeospatialRaster<double> raster)
    {
        Assert.Equal(fixture.Width, raster.Width);
        Assert.Equal(fixture.Height, raster.Height);
        Assert.Equal(fixture.BandCount, raster.BandCount);
        Assert.Equal(fixture.SampleFormat, raster.SampleFormat);
        Assert.Equal(fixture.ExpectedSamples.ToArray(), raster.Samples.ToArray());
    }

    [Fact]
    public void Rgba_AssociatedAlpha_IsRejectedBeforeDecode()
    {
        // Premultiplied alpha decoded as straight RGBA would darken colors downstream
        // (cross-card B4b-13: ImageSharp PNG expects straight alpha).
        var builder = ValidRawBuilder(bands: 4, photometric: 2, pixels: [10, 20, 30, 255, 40, 50, 60, 128]);
        builder.AddShortArray(ExtraSamplesTag, [1]);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterIncompatibleLayout, GeoStage.Raster);
        Assert.Contains("associated", error.Message);
    }

    [Fact]
    public void Rgba_UnassociatedAlpha_IsPreserved()
    {
        var builder = ValidRawBuilder(bands: 4, photometric: 2, pixels: [10, 20, 30, 255, 40, 50, 60, 128]);
        builder.AddShortArray(ExtraSamplesTag, [2]);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(4, raster!.BandCount);
        Assert.Equal(10d, raster.Get(0, 0, 0));
        Assert.Equal(255d, raster.Get(3, 0, 0));
        Assert.Equal(40d, raster.Get(0, 0, 1));
        Assert.Equal(128d, raster.Get(3, 0, 1));
    }

    [Fact]
    public void Rgba_AbsentExtraSamples_ReadsFourthBandAsUnassociatedAlpha()
    {
        // The WI-B4b-09 RGBA contract stores the fourth band as unassociated alpha, so an
        // absent descriptor is accepted under that documented assumption.
        var builder = ValidRawBuilder(bands: 4, photometric: 2, pixels: [10, 20, 30, 255, 40, 50, 60, 128]);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(4, raster!.BandCount);
        Assert.Equal(255d, raster.Get(3, 0, 0));
        Assert.Equal(128d, raster.Get(3, 0, 1));
    }

    [Fact]
    public void Plane_ReproducesSamplesTransformAndCrs()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(1d, 2d);
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(CoordinateSystemId.Wgs84, raster.CoordinateSystem);
        Assert.Equal(fixture.Transform, raster.Transform);
        Assert.True(raster.Transform.IsNorthUp);
        Assert.False(raster.HasNoData);
        Assert.Equal("unknown", raster.VerticalDatum);

        // The plane z = 10 + column + 2 * row reproduces at control pixels through the affine.
        Assert.Equal(10d, raster.Get(0, 0, 0));
        Assert.Equal(17d, raster.Get(0, 2, 3));
        Assert.Equal(27d, raster.Get(0, 5, 7));

        var corner = raster.Transform.PixelToWorld(new Vector2d(7.5d, 5.5d));
        Assert.Equal(2d + 7.5d * 0.25d, corner.X);
        Assert.Equal(49d - 5.5d * 0.125d, corner.Y);
        AssertAllSamplesMatch(fixture, raster);
    }

    [Fact]
    public void Plane_Utm31_ResolvesProjectedCrs()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Crs = SyntheticGeoTiffCrs.Wgs84Utm31North });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), raster.CoordinateSystem);
        Assert.Equal(fixture.Transform, raster.Transform);
        AssertAllSamplesMatch(fixture, raster);
    }

    [Fact]
    public void Plane_ModelTransformation_MatchesScaleAndTiepointResult()
    {
        using var scaleForm = SyntheticGeoTiffWriter.Plane(1d, 1d);
        using var matrixForm = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { UseModelTransformation = true });
        var (fromScale, scaleDiagnostics) = ReadRaster(scaleForm.FilePath);
        var (fromMatrix, matrixDiagnostics) = ReadRaster(matrixForm.FilePath);

        Assert.False(scaleDiagnostics.HasErrors);
        Assert.False(matrixDiagnostics.HasErrors);
        Assert.Equal(fromScale!.Transform, fromMatrix!.Transform);
        Assert.Equal(matrixForm.Transform, fromMatrix.Transform);
        AssertAllSamplesMatch(matrixForm, fromMatrix);
    }

    [Fact]
    public void SouthUp_KeepsPositiveRowScaleAndIsNotNorthUp()
    {
        using var fixture = SyntheticGeoTiffWriter.SouthUp();
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.False(raster.Transform.IsNorthUp);
        Assert.True(raster.Transform.ScaleY > 0d);
        Assert.Equal(fixture.Transform, raster.Transform);
        AssertAllSamplesMatch(fixture, raster!);
    }

    [Fact]
    public void PixelIsPoint_OriginShiftsByExactlyHalfAPixel()
    {
        using var area = SyntheticGeoTiffWriter.Plane(1d, 1d);
        using var point = SyntheticGeoTiffWriter.PixelIsPoint();
        var (areaRaster, areaDiagnostics) = ReadRaster(area.FilePath);
        var (pointRaster, pointDiagnostics) = ReadRaster(point.FilePath);

        Assert.False(areaDiagnostics.HasErrors);
        Assert.False(pointDiagnostics.HasErrors);
        Assert.Equal(point.Transform, pointRaster!.Transform);
        Assert.Equal(
            areaRaster!.Transform.OriginX - areaRaster.Transform.ScaleX / 2d,
            pointRaster.Transform.OriginX);
        Assert.Equal(
            areaRaster.Transform.OriginY - areaRaster.Transform.ScaleY / 2d,
            pointRaster.Transform.OriginY);
        Assert.Equal(areaRaster.Transform.ScaleX, pointRaster.Transform.ScaleX);
        Assert.Equal(areaRaster.Transform.ScaleY, pointRaster.Transform.ScaleY);
        AssertAllSamplesMatch(point, pointRaster);
    }

    [Fact]
    public void Epsg27700_FailsWithUnsupportedCrsCode()
    {
        using var fixture = SyntheticGeoTiffWriter.Epsg27700();
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.CoordinatesUnsupportedCrs, GeoStage.Coordinates);
        Assert.Contains("27700", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingCrs_FailsWithCoordinatesDiagnostic(int geoKeyVariant)
    {
        // Variant 0: no GeoKey directory at all; variant 1: keys 1024 and 1025 but no CRS key.
        var builder = ValidRawBuilder();

        if (geoKeyVariant == 1)
        {
            builder.AddGeoKeys((1024, 2), (1025, 1));
        }

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.CoordinatesFailure, GeoStage.Coordinates);
    }

    [Fact]
    public void UnsupportedUnits_FailWithCoordinatesDiagnostic()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 1), (1025, 1), (3076, 9002), (3072, 32631));

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.CoordinatesUnsupportedUnits, GeoStage.Coordinates);
    }

    [Fact]
    public void VerticalUnitsInFeet_AreRejectedWithCoordinatesDiagnostic()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326), (4099, 9002));

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.CoordinatesUnsupportedUnits, GeoStage.Coordinates);
    }

    [Fact]
    public void WithNoDataHole_CarriesNoDataSentinelAndHoleSamples()
    {
        using var fixture = SyntheticGeoTiffWriter.WithNoDataHole();
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.True(raster.HasNoData);
        Assert.Equal(-9999d, raster.NoData);
        Assert.Equal(-9999d, raster.Get(0, 2, 3));
        Assert.Equal(-9999d, raster.Get(0, 3, 4));
        AssertAllSamplesMatch(fixture, raster!);
    }

    [Fact]
    public void VerticalCrs_MapsKnownDatumNamesAndLeavesUnknownAsMetadata()
    {
        using (var egm96 = SyntheticGeoTiffWriter.Plane(1d, 1d, new SyntheticGeoTiffOptions { VerticalCrsEpsg = 5773 }))
        {
            var (raster, diagnostics) = ReadRaster(egm96.FilePath);

            Assert.False(diagnostics.HasErrors);
            Assert.Equal("EGM96", raster!.VerticalDatum);
        }

        using (var egm2008 = SyntheticGeoTiffWriter.Plane(1d, 1d, new SyntheticGeoTiffOptions { VerticalCrsEpsg = 3855 }))
        {
            var (raster, diagnostics) = ReadRaster(egm2008.FilePath);

            Assert.False(diagnostics.HasErrors);
            Assert.Equal("EGM2008", raster!.VerticalDatum);
        }

        using (var unknown = SyntheticGeoTiffWriter.Plane(1d, 1d, new SyntheticGeoTiffOptions { VerticalCrsEpsg = 9999 }))
        {
            var (raster, diagnostics) = ReadRaster(unknown.FilePath);

            Assert.False(diagnostics.HasErrors);
            Assert.Equal("unknown", raster!.VerticalDatum);
        }
    }

    [Theory]
    [MemberData(nameof(ScalarFormats))]
    public void Scalar_DecodesAllFiveFormatsWithMetadata(RasterSampleFormat format)
    {
        using var fixture = SyntheticGeoTiffWriter.Scalar(format);
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(format, raster.SampleFormat);
        Assert.Equal(1, raster.BandCount);
        Assert.Equal(3d, raster.Get(0, 1, 0));
        AssertAllSamplesMatch(fixture, raster!);
    }

    [Fact]
    public void Ridge_Int16_DecodesSlopeSamples()
    {
        using var fixture = SyntheticGeoTiffWriter.Ridge(
            new SyntheticGeoTiffOptions { SampleFormat = RasterSampleFormat.Int16 });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(RasterSampleFormat.Int16, raster!.SampleFormat);
        AssertAllSamplesMatch(fixture, raster);
    }

    [Fact]
    public void Classification_Uint8_DecodesGivenValues()
    {
        var values = Enumerable.Range(0, 8 * 6).Select(static i => i % 4 == 0 ? 0 : 255 - (i % 8)).ToArray();

        using var fixture = SyntheticGeoTiffWriter.Classification(values);
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(RasterSampleFormat.UInt8, raster!.SampleFormat);
        Assert.Equal(0d, raster.Get(0, 0, 0));
        Assert.Equal(254d, raster.Get(0, 0, 1));
        AssertAllSamplesMatch(fixture, raster);
    }

    [Fact]
    public void Imagery_RgbAndRgba_DecodeBandInterleavedControlPixels()
    {
        using var rgb = SyntheticGeoTiffWriter.Imagery();
        using var rgba = SyntheticGeoTiffWriter.Imagery(new SyntheticGeoTiffOptions { BandCount = 4 });
        var (rgbRaster, rgbDiagnostics) = ReadRaster(rgb.FilePath);
        var (rgbaRaster, rgbaDiagnostics) = ReadRaster(rgba.FilePath);

        Assert.False(rgbDiagnostics.HasErrors);
        Assert.False(rgbaDiagnostics.HasErrors);
        Assert.Equal(3, rgbRaster!.BandCount);
        Assert.Equal(4, rgbaRaster!.BandCount);
        Assert.Equal(RasterSampleFormat.UInt8, rgbRaster.SampleFormat);
        Assert.Equal(rgb.GetExpectedSample(5, 7, 0), rgbRaster.Get(0, 5, 7));
        Assert.Equal(rgb.GetExpectedSample(5, 7, 1), rgbRaster.Get(1, 5, 7));
        Assert.Equal(rgb.GetExpectedSample(5, 7, 2), rgbRaster.Get(2, 5, 7));
        Assert.Equal(rgba.GetExpectedSample(5, 7, 3), rgbaRaster.Get(3, 5, 7));
        AssertAllSamplesMatch(rgb, rgbRaster);
        AssertAllSamplesMatch(rgba, rgbaRaster);
    }

    [Fact]
    public void TiledPlane_DecodesAllSamplesAcrossTileBoundaries()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            2d,
            new SyntheticGeoTiffOptions { Width = 20, Height = 20, Layout = SyntheticGeoTiffLayout.Tiled });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(20, raster!.Width);
        Assert.Equal(fixture.GetExpectedSample(0, 15, 0), raster.Get(0, 0, 15));
        Assert.Equal(fixture.GetExpectedSample(0, 16, 0), raster.Get(0, 0, 16));
        Assert.Equal(fixture.GetExpectedSample(15, 0, 0), raster.Get(0, 15, 0));
        Assert.Equal(fixture.GetExpectedSample(16, 0, 0), raster.Get(0, 16, 0));
        AssertAllSamplesMatch(fixture, raster);
    }

    [Fact]
    public void TiledImagery_DecodesControlPixels()
    {
        using var fixture = SyntheticGeoTiffWriter.Imagery(
            new SyntheticGeoTiffOptions { Width = 20, Height = 20, Layout = SyntheticGeoTiffLayout.Tiled });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(3, raster!.BandCount);
        Assert.Equal(fixture.GetExpectedSample(7, 17, 2), raster.Get(2, 7, 17));
        AssertAllSamplesMatch(fixture, raster);
    }

    [Fact]
    public void NonIdentityScaleZ_IsRejectedAndNeverReturnsUnscaledSamples()
    {
        // ScaleZ 2 would halve every elevation if silently dropped; the reader must reject
        // the raster outright instead of returning sample 10 as height 10.
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 2d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping, GeoStage.Raster);
    }

    [Fact]
    public void MatrixZTranslation_IsRejectedWithElevationDiagnostic()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(
            ModelTransformationTag,
            [2d, 0d, 0d, 10d, 0d, -0.5d, 0d, 20d, 0d, 0d, 1d, 5d, 0d, 0d, 0d, 1d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping, GeoStage.Raster);
    }

    [Fact]
    public void MatrixZCrossAxis_IsRejectedWithElevationDiagnostic()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(
            ModelTransformationTag,
            [2d, 0d, 0d, 10d, 0d, -0.5d, 0d, 20d, 1d, 0d, 1d, 0d, 0d, 0d, 0d, 1d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping, GeoStage.Raster);
    }

    [Fact]
    public void NoData_InvalidText_IsRejected()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { NoData = "abc" });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
        Assert.Contains("GDAL_NODATA", error.Message);
    }

    [Fact]
    public void NoData_Float32SentinelIsNormalizedToFloat32Precision()
    {
        using var fixture = SyntheticGeoTiffWriter.WithNoDataHole(
            new SyntheticGeoTiffOptions { NoData = "-9999.1" });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.True(raster!.HasNoData);
        Assert.Equal(-9999.099609375d, raster.NoData);
        Assert.Equal((double)(float)-9999.1d, raster.Get(0, 2, 3));
    }

    [Fact]
    public void NoData_NaNSentinel_IsSupportedForFloatRasters()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { NoData = "NaN" });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.True(raster!.HasNoData);
        Assert.True(double.IsNaN(raster.NoData));
    }

    [Theory]
    [InlineData("300")]
    [InlineData("-1")]
    public void NoData_IntegerOutOfRange_IsRejected(string noDataText)
    {
        using var fixture = SyntheticGeoTiffWriter.Classification(
            Enumerable.Range(1, 48).ToArray(),
            new SyntheticGeoTiffOptions { NoData = noDataText });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
        Assert.Contains("range", error.Message);
    }

    [Fact]
    public void NoData_IntegerNonIntegral_IsRejected()
    {
        using var fixture = SyntheticGeoTiffWriter.Classification(
            Enumerable.Range(1, 48).ToArray(),
            new SyntheticGeoTiffOptions { NoData = "12.5" });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
        Assert.Contains("integer", error.Message);
    }

    [Fact]
    public void Read_DecodesBigEndianTiffWithoutExtraSwapping()
    {
        // LibTiff returns decoded samples in native endian even for MM files, so the reader
        // decodes with plain BitConverter; this regression pins that behaviour.
        var builder = ValidRawBuilder(width: 2, height: 2, pixels: [200, 7, 9, 1], bigEndian: true);
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(2, raster!.Width);
        Assert.Equal(2, raster.Height);
        Assert.Equal(200d, raster.Get(0, 0, 0));
        Assert.Equal(1d, raster.Get(0, 1, 1));
        Assert.Equal(new AffineTransform(10d, 2d, 0d, 20d, 0d, -0.5d), raster.Transform);
    }

    [Fact]
    public void TiepointNonZeroModelZ_IsRejectedWithElevationDiagnostic()
    {
        // ScaleZ 1 with a tiepoint Z of 100 would shift every elevation by 100 m; returning
        // sample 10 as height 10 would be wrong, so the raster is rejected before decode.
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 1d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 100d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping, GeoStage.Raster);
    }

    [Fact]
    public void TiepointNonZeroModelZWithScaleZUnused_IsRejectedWithElevationDiagnostic()
    {
        // ScaleZ 0 means the Z axis is unused, but a declared 100 m model Z is an offset,
        // not noise: letting it silently vanish would drop 100 m of elevation.
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 100d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterUnsupportedElevationMapping, GeoStage.Raster);
    }

    [Fact]
    public void TiepointIdentityZWithScaleOne_IsAccepted()
    {
        var builder = ValidRawBuilder(pixels: [7, 9]);
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 1d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(new AffineTransform(10d, 2d, 0d, 20d, 0d, -0.5d), raster!.Transform);
        Assert.Equal(7d, raster.Get(0, 0, 0));
    }

    [Fact]
    public void ContradictoryTiepoints_AreRejectedWithInvalidTransform()
    {
        // Tie (0, 0) -> (0, 0) with tie (1, 0) -> (100, 0) and scale X 1 is unsolvable.
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [1d, 1d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 0d, 0d, 0d, 1d, 0d, 0d, 100d, 0d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterInvalidTransform, GeoStage.Raster);
        Assert.Contains("contradicts", error.Message);
    }

    [Fact]
    public void RedundantConsistentTiepoints_AreAccepted()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d, 4d, 2d, 0d, 18d, 19d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(new AffineTransform(10d, 2d, 0d, 20d, 0d, -0.5d), raster!.Transform);
    }

    [Fact]
    public void TiepointDerivedArithmeticOverflow_IsRejectedWithInvalidTransform()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [1e308d, 1d, 0d]);
        builder.AddDoubleArray(TiepointTag, [1e308d, 0d, 0d, 0d, 0d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterInvalidTransform, GeoStage.Raster);
    }

    [Fact]
    public void Read_RegistersGeoTiffTagsItselfAndReadsKnownBytes()
    {
        // A hand-built TIFF read before any fixture writer call proves the reader's own tag
        // registration works; the raw builder never touches SyntheticGeoTiffWriter.
        var builder = ValidRawBuilder(pixels: [7, 9]);
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.True(GeoTiffTagRegistration.IsRegistered);
        Assert.NotNull(raster);
        Assert.Equal(2, raster!.Width);
        Assert.Equal(1, raster.Height);
        Assert.Equal(RasterSampleFormat.UInt8, raster.SampleFormat);
        Assert.Equal(CoordinateSystemId.Wgs84, raster.CoordinateSystem);

        // Tag 33550 ScaleY is reversed into the affine row scale (GeoTIFF 1.1 section 7.3.2).
        Assert.Equal(new AffineTransform(10d, 2d, 0d, 20d, 0d, -0.5d), raster.Transform);
        Assert.Equal(7d, raster.Get(0, 0, 0));
        Assert.Equal(9d, raster.Get(0, 0, 1));
    }

    [Fact]
    public void Read_UsesNonzeroTiepointIndicesToSolveTheOrigin()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [4d, 3d, 0d, 18d, 18.5d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(18d - 4d * 2d, raster!.Transform.OriginX);
        Assert.Equal(18.5d - 3d * -0.5d, raster.Transform.OriginY);
    }

    [Fact]
    public void CrsKeys_ProjectedWithConsistentGeographicBase_AreAccepted()
    {
        // A geographic key alongside a projected one names the projected CRS's base; 4326
        // under WGS84 UTM is consistent, not contradictory.
        var builder = ValidRawBuilder(pixels: [7, 9]);
        builder.AddGeoKeys((1024, 1), (1025, 1), (2048, 4326), (3072, 32631));
        AddScaleAndTiepoint(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), raster!.CoordinateSystem);
        Assert.Equal(7d, raster.Get(0, 0, 0));
    }

    [Fact]
    public void CrsModelGeographic_WithProjectedKey_IsRejected()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326), (3072, 32631));

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
    }

    [Fact]
    public void CrsModelProjected_WithOnlyGeographicKey_IsRejected()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 1), (1025, 1), (2048, 4326));

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
    }

    [Fact]
    public void CrsProjected_WithForeignGeographicBase_IsRejected()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 1), (1025, 1), (2048, 4269), (3072, 32631));

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
    }

    [Fact]
    public void TagRegistration_IsSharedByReaderAndFixtureWriter()
    {
        GeoTiffTagRegistration.EnsureRegistered();
        Assert.True(GeoTiffTagRegistration.IsRegistered);

        // The fixture delegates to the same gate instead of installing a second extender.
        SyntheticGeoTiffWriter.EnsureGeoTiffTagsRegistered();
        Assert.True(GeoTiffTagRegistration.IsRegistered);

        using var fixture = SyntheticGeoTiffWriter.Plane(1d, 1d);
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
    }

    [Fact]
    public void RotationMatrix_IsRejectedWithRotationDiagnostic()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(
            ModelTransformationTag,
            [2d, 0.5d, 0d, 10d, 0d, -0.5d, 0d, 20d, 0d, 0d, 1d, 0d, 0d, 0d, 0d, 1d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterRotationOrShear, GeoStage.Raster);
    }

    [Fact]
    public void NonHomogeneousMatrix_IsRejectedWithInvalidTransformDiagnostic()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(
            ModelTransformationTag,
            [2d, 0d, 0d, 10d, 0d, -0.5d, 0d, 20d, 0d, 0d, 1d, 0d, 0d, 0d, 0d, 2d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterInvalidTransform, GeoStage.Raster);
    }

    [Fact]
    public void MissingTransform_IsRejectedBeforeDecode()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterMissingTransform, GeoStage.Raster);
    }

    [Fact]
    public void BothTransformForms_IsRejectedAsMalformed()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d]);
        builder.AddDoubleArray(
            ModelTransformationTag,
            [2d, 0d, 0d, 10d, 0d, -0.5d, 0d, 20d, 0d, 0d, 1d, 0d, 0d, 0d, 0d, 1d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
    }

    [Fact]
    public void TruncatedPixelData_IsRejectedWithTruncatedDiagnostic()
    {
        var builder = ValidRawBuilder(stripOffsetAdd: 4096);
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.ParseRasterDataTruncated, GeoStage.Parse);
    }

    [Fact]
    public void UnsupportedCompression_IsRejectedWithCompressionDiagnostic()
    {
        var builder = ValidRawBuilder(compression: 34676);
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d]);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.ParseUnsupportedCompression, GeoStage.Parse);
    }

    [Fact]
    public void PlanarSeparate_IsRejectedExplicitly()
    {
        var builder = ValidRawBuilder(planarConfig: 2);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.ParseUnsupportedLayout, GeoStage.Parse);
        Assert.Contains("Planar", error.Message);
    }

    [Fact]
    public void BottomLeftOrientation_IsRejectedExplicitly()
    {
        var builder = ValidRawBuilder(orientation: 4);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.ParseUnsupportedLayout, GeoStage.Parse);
        Assert.Contains("Orientation", error.Message);
    }

    [Fact]
    public void PalettePhotometric_IsRejectedExplicitly()
    {
        var builder = ValidRawBuilder(photometric: 3);

        // LibTiff refuses to open palette files without a colormap, so carry a zeroed one and
        // let the reader's photometric check fire instead.
        builder.AddShortArray(320, new int[3 * 256]);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.ParseUnsupportedLayout, GeoStage.Parse);
        Assert.Contains("Photometric", error.Message);
    }

    [Theory]
    [InlineData(64, 3)]
    [InlineData(32, 1)]
    public void UnsupportedBitsAndSampleFormat_AreRejected(int bits, int sampleFormat)
    {
        var builder = ValidRawBuilder(bitsPerSample: bits, sampleFormat: sampleFormat);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.ParseUnsupportedSampleFormat, GeoStage.Parse);
    }

    [Fact]
    public void TwoBands_AreRejectedExplicitly()
    {
        var builder = ValidRawBuilder(bands: 2, bitsPerSample: 8, pixels: [1, 2, 3, 4]);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.ParseUnsupportedLayout, GeoStage.Parse);
        Assert.Contains("bands", error.Message);
    }

    [Theory]
    [InlineData(2, 1, 0, 0)]
    [InlineData(1, 2, 0, 0)]
    [InlineData(1, 1, 2, 0)]
    [InlineData(1, 1, 0, 1)]
    public void MalformedGeoKeyHeaders_AreRejected(int version, int revision, int minor, int declaredExtraKeys)
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeyEntries(version, revision, minor, 2 + declaredExtraKeys, 1024, 0, 1, 2, 2048, 0, 1, 4326);
        AddScaleAndTiepoint(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
    }

    [Fact]
    public void DuplicateGeoKey_IsRejected()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeyEntries(1, 1, 0, 3, 1024, 0, 1, 2, 2048, 0, 1, 4326, 2048, 0, 1, 4326);
        AddScaleAndTiepoint(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
        Assert.Contains("2048", error.Message);
    }

    [Fact]
    public void GeoDoubleParamsOffsetOutOfBounds_IsRejected()
    {
        var builder = ValidRawBuilder();
        builder.AddGeoKeyEntries(1, 1, 0, 2, 1024, 0, 1, 2, 2048, GeoDoubleParamsTag, 1, 99);
        builder.AddDoubleArray(GeoDoubleParamsTag, [4326d]);
        AddScaleAndTiepoint(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(file.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
        Assert.Contains("double params", error.Message);
    }

    [Fact]
    public void MalformedPixelScaleTooShort_IsRejectedWithInvalidTransform()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Malformation = SyntheticGeoTiffMalformation.PixelScaleTooShort });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterInvalidTransform, GeoStage.Raster);
    }

    [Fact]
    public void MalformedTiepointTooShort_IsRejectedWithInvalidTransform()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Malformation = SyntheticGeoTiffMalformation.TiepointTooShort });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterInvalidTransform, GeoStage.Raster);
    }

    [Fact]
    public void MalformedZeroPixelScale_IsRejectedWithInvalidTransform()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Malformation = SyntheticGeoTiffMalformation.ZeroPixelScale });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.Null(raster);
        AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterInvalidTransform, GeoStage.Raster);
    }

    [Fact]
    public void MalformedTruncatedGeoKeys_IsRejectedAsParseFailure()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Malformation = SyntheticGeoTiffMalformation.TruncatedGeoKeys });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath);

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoDiagnosticCodes.ParseFailure, GeoStage.Parse);
        Assert.Contains("keys", error.Message);
    }

    [Fact]
    public void Limits_DimensionsAreRejectedBeforeAllocation()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(1d, 1d);
        var (raster, diagnostics) = ReadRaster(fixture.FilePath, new GeospatialImportLimits { MaxRasterWidth = 7 });

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterLimitExceeded, GeoStage.Raster);
        Assert.Contains("dimensions", error.Message);
    }

    [Fact]
    public void Limits_NormalizedBufferIsRejectedBeforeAllocation()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(1d, 1d);
        var (raster, diagnostics) = ReadRaster(fixture.FilePath, new GeospatialImportLimits { MaxRasterDecodedBytes = 383 });

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterLimitExceeded, GeoStage.Raster);
        Assert.Contains("limit of 383", error.Message);
    }

    [Fact]
    public void Limits_TileDecodeBufferIsRejectedBeforeAllocation()
    {
        // A 1 x 1 tiled raster needs a full 16 x 16 x 4-byte tile: 1024 decode bytes for 8
        // normalized bytes, so only the decode-buffer guard rejects it.
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Width = 1, Height = 1, Layout = SyntheticGeoTiffLayout.Tiled });
        var (raster, diagnostics) = ReadRaster(fixture.FilePath, new GeospatialImportLimits { MaxRasterDecodedBytes = 512 });

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterLimitExceeded, GeoStage.Raster);
        Assert.Contains("tile", error.Message);
    }

    [Fact]
    public void Limits_SampleCountBeyondIntCapacityIsRejectedBeforeAllocation()
    {
        // Two compressed PackBits strips keep every per-strip declaration small enough for
        // LibTiff to open, while the 50000 x 50000 sample count exceeds int capacity; the
        // reader must reject before allocating or decoding anything.
        var builder = new RawTiffBuilder(
            width: 50_000,
            height: 50_000,
            bands: 1,
            bitsPerSample: 8,
            compression: 32773,
            pixels: [1, 1],
            stripCount: 2);
        AddDefaultGeoreferencing(builder);

        using var file = builder.Write();
        var (raster, diagnostics) = ReadRaster(
            file.FilePath,
            new GeospatialImportLimits { MaxRasterDecodedBytes = 100_000_000_000 });

        Assert.Null(raster);
        var error = AssertSingleError(diagnostics, GeoTiffDiagnosticCodes.RasterLimitExceeded, GeoStage.Raster);
        Assert.Contains("int", error.Message);
    }

    [Fact]
    public void NonexistentFile_ReportsParseFailure()
    {
        var (raster, diagnostics) = ReadRaster(Path.Combine(Path.GetTempPath(), "panopticon-geotiff-absent.tif"));

        Assert.Null(raster);
        Assert.True(diagnostics.HasErrors);
        Assert.All(diagnostics.Items, static item => Assert.Equal(GeoStage.Parse, item.Stage));
    }

    private static RawTiffBuilder ValidRawBuilder(
        int width = 2,
        int height = 1,
        int bands = 1,
        int bitsPerSample = 8,
        int sampleFormat = 1,
        int photometric = 1,
        int compression = 1,
        int planarConfig = 1,
        int orientation = 0,
        int stripOffsetAdd = 0,
        byte[] pixels = null,
        bool bigEndian = false)
    {
        return new RawTiffBuilder(width, height, bands, bitsPerSample, sampleFormat, photometric, compression, planarConfig, orientation, stripOffsetAdd, bigEndian, pixels);
    }

    private static void AddScaleAndTiepoint(RawTiffBuilder builder)
    {
        builder.AddDoubleArray(PixelScaleTag, [2d, 0.5d, 0d]);
        builder.AddDoubleArray(TiepointTag, [0d, 0d, 0d, 10d, 20d, 0d]);
    }

    private static void AddDefaultGeoreferencing(RawTiffBuilder builder)
    {
        builder.AddGeoKeys((1024, 2), (1025, 1), (2048, 4326));
        AddScaleAndTiepoint(builder);
    }

    private sealed class RawTiffFile(string directory, string filePath) : IDisposable
    {
        public string FilePath { get; } = filePath;

        private string DirectoryPath { get; } = directory;

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Builds minimal TIFFs byte by byte, little-endian by default or big-endian (MM) on
    /// request, so malformed metadata the fixture writer cannot produce (rotation matrices,
    /// exotic compression, stripped directories) is still testable against known bytes.
    /// </summary>
    private sealed class RawTiffBuilder(
        int width,
        int height,
        int bands,
        int bitsPerSample,
        int sampleFormat = 1,
        int photometric = 1,
        int compression = 1,
        int planarConfig = 1,
        int orientation = 0,
        int stripOffsetAdd = 0,
        bool bigEndian = false,
        byte[] pixels = null,
        int stripCount = 1)
    {
        private const int TypeShort = 3;

        private const int TypeLong = 4;

        private const int TypeDouble = 12;

        private readonly byte[] _pixels = pixels ?? new byte[checked(width * height * bands * (bitsPerSample / 8))];
        private readonly List<Entry> _entries = [];

        private sealed record Entry(ushort Tag, int Type, int Count, byte[] Value)
        {
            public long BlobOffset { get; set; }
        }

        public void AddShort(ushort tag, int value)
        {
            AddShortArray(tag, [value]);
        }

        public void AddShortArray(ushort tag, int[] values)
        {
            _entries.Add(new Entry(tag, TypeShort, values.Length, values.SelectMany(static v => BitConverter.GetBytes((ushort)v)).ToArray()));
        }

        public void AddLong(ushort tag, long value)
        {
            _entries.Add(new Entry(tag, TypeLong, 1, BitConverter.GetBytes((uint)value)));
        }

        public void AddLongArray(ushort tag, long[] values)
        {
            _entries.Add(new Entry(tag, TypeLong, values.Length, values.SelectMany(static v => BitConverter.GetBytes((uint)v)).ToArray()));
        }

        public void AddDoubleArray(ushort tag, double[] values)
        {
            _entries.Add(new Entry(tag, TypeDouble, values.Length, values.SelectMany(BitConverter.GetBytes).ToArray()));
        }

        /// <summary>Adds inline-valued keys: key id, value pairs become (id, 0, 1, value) entries.</summary>
        public void AddGeoKeys(params (ushort Id, ushort Value)[] keys)
        {
            var flat = new List<ushort>();

            foreach (var (id, value) in keys)
            {
                flat.AddRange([id, 0, 1, value]);
            }

            AddGeoKeyEntries(1, 1, 0, keys.Length, flat.ToArray());
        }

        /// <summary>Writes the directory verbatim: header (version, revision, minor, count) plus four-short entries.</summary>
        public void AddGeoKeyEntries(int version, int revision, int minor, int declaredCount, params ushort[] flatEntries)
        {
            var values = new List<ushort> { (ushort)version, (ushort)revision, (ushort)minor, (ushort)declaredCount };

            values.AddRange(flatEntries);
            AddShortArray(GeoKeyDirectoryTag, values.Select(static value => (int)value).ToArray());
        }

        public RawTiffFile Write()
        {
            // Distinct from the fixture writer's panopticon-geotiff-* directories so parallel
            // reader tests never disturb its temp-directory bookkeeping.
            var directory = Path.Combine(Path.GetTempPath(), "panopticon-raw-tiff-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, "raster.tif");
            File.WriteAllBytes(filePath, Build());

            return new RawTiffFile(directory, filePath);
        }

        private byte[] Build()
        {
            AddLong(ImageWidthTag, width);
            AddLong(ImageLengthTag, height);
            AddShortArray(BitsPerSampleTag, Enumerable.Repeat(bitsPerSample, bands).ToArray());
            AddShort(CompressionTag, compression);
            AddShort(PhotometricTag, photometric);

            if (orientation != 0)
            {
                AddShort(OrientationTag, orientation);
            }

            AddShort(SamplesPerPixelTag, bands);
            AddShort(PlanarConfigTag, planarConfig);
            AddShort(SampleFormatTag, sampleFormat);

            // Pixels lead the data area, so the first segment always starts right after the
            // eight-byte header.
            const int PixelOffset = 8;

            if (stripCount <= 1)
            {
                AddLong(RowsPerStripTag, height);
                _entries.Add(new Entry(StripOffsetsTag, TypeLong, 1, BitConverter.GetBytes((uint)(PixelOffset + stripOffsetAdd))));
                _entries.Add(new Entry(StripByteCountsTag, TypeLong, 1, BitConverter.GetBytes((uint)_pixels.Length)));
            }
            else
            {
                // Multi-strip mode: pixels carries stripCount equal encoded segments, one
                // offset and count per strip. Used for compressed images whose total sample
                // count exceeds what any single LibTiff strip may declare.
                if (_pixels.Length % stripCount != 0)
                {
                    throw new ArgumentException($"pixels length {_pixels.Length} must split into {stripCount} equal strips.");
                }

                var bytesPerStrip = _pixels.Length / stripCount;
                AddLong(RowsPerStripTag, (height + stripCount - 1) / stripCount);
                AddLongArray(StripOffsetsTag, Enumerable.Range(0, stripCount).Select(index => (long)index * bytesPerStrip + PixelOffset + stripOffsetAdd).ToArray());
                AddLongArray(StripByteCountsTag, Enumerable.Repeat((long)bytesPerStrip, stripCount).ToArray());
            }

            if (bigEndian)
            {
                foreach (var entry in _entries)
                {
                    ConvertValueToBigEndian(entry);
                }
            }

            using var data = new MemoryStream();
            data.Write(new byte[8]);
            data.Write(_pixels);

            foreach (var entry in _entries.Where(static entry => entry.Value.Length > 4))
            {
                if (data.Position % 2 == 1)
                {
                    data.WriteByte(0);
                }

                entry.BlobOffset = data.Position;
                data.Write(entry.Value);
            }

            var ifdOffset = data.Position;

            using var ifd = new MemoryStream();
            var ordered = _entries.OrderBy(static entry => entry.Tag, Comparer<ushort>.Default).ToList();

            ifd.Write(Word((ushort)ordered.Count));

            foreach (var entry in ordered)
            {
                ifd.Write(Word(entry.Tag));
                ifd.Write(Word((ushort)entry.Type));
                ifd.Write(Dword((uint)entry.Count));

                if (entry.Value.Length <= 4)
                {
                    ifd.Write(entry.Value);
                    ifd.Write(new byte[4 - entry.Value.Length]);
                }
                else
                {
                    ifd.Write(Dword((uint)entry.BlobOffset));
                }
            }

            ifd.Write(Dword(0u));
            data.Write(ifd.ToArray());

            var file = data.ToArray();
            var mark = bigEndian ? (byte)'M' : (byte)'I';
            file[0] = mark;
            file[1] = mark;
            Word(42).CopyTo(file, 2);
            Dword((uint)ifdOffset).CopyTo(file, 4);

            return file;
        }

        private static void ConvertValueToBigEndian(Entry entry)
        {
            var size = entry.Type switch
            {
                TypeShort => 2,
                TypeLong => 4,
                TypeDouble => 8,
                _ => 0,
            };

            if (size == 0)
            {
                return;
            }

            for (var index = 0; index + size <= entry.Value.Length; index += size)
            {
                Array.Reverse(entry.Value, index, size);
            }
        }

        private byte[] Word(ushort value)
        {
            var bytes = BitConverter.GetBytes(value);

            if (bigEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        private byte[] Dword(uint value)
        {
            var bytes = BitConverter.GetBytes(value);

            if (bigEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }
    }
}
