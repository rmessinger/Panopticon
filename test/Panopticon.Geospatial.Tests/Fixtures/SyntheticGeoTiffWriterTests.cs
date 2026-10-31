using System;
using System.IO;
using System.Linq;
using BitMiracle.LibTiff.Classic;
using Panopticon.Geospatial.Interchange;
using Xunit;

namespace Panopticon.Geospatial.Tests.Fixtures;

public class SyntheticGeoTiffWriterTests
{
    [Fact]
    public void Plane_Geographic_WritesReopenableScaleTiepointAndGeoKeys()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(1d, 2d);

        Assert.True(File.Exists(fixture.FilePath));
        Assert.Equal(8, fixture.Width);
        Assert.Equal(6, fixture.Height);
        Assert.Equal(1, fixture.BandCount);
        Assert.Equal(RasterSampleFormat.Float32, fixture.SampleFormat);
        Assert.True(fixture.IsSupportedCrs);

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(8, tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt());
        Assert.Equal(6, tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt());
        Assert.Equal(32, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToShort());
        Assert.Equal(SampleFormat.IEEEFP, (SampleFormat)tiff.GetField(TiffTag.SAMPLEFORMAT)[0].ToShort());
        Assert.Equal(1, tiff.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt());
        Assert.Equal(SyntheticGeoTiffWriter.RowsPerStrip, tiff.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt());
        Assert.False(tiff.IsTiled());

        Assert.Null(tiff.GetField(TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG));
        Assert.Null(tiff.GetField(TiffTag.GDAL_NODATA));
        // North-up: tag ScaleY is positive even though the affine row scale is negative
        // (GeoTIFF 1.1 section 7.3.2, Requirements 10.4 and 10.5).
        Assert.Equal(new[] { 0.25d, 0.125d, 0d }, ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG));
        Assert.Equal(new[] { 0d, 0d, 0d, 2d, 49d, 0d }, ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELTIEPOINTTAG));
        Assert.Equal(fixture.GeoKeys.ToArray(), ReadGeoKeyTag(tiff));

        AssertGeoKey(fixture, 1024, 2);
        AssertGeoKey(fixture, 1025, 1);
        AssertGeoKey(fixture, 2048, 4326);
        Assert.Equal(-1, GeoKeyIndex(fixture, 3072));

        Assert.True(fixture.Transform.IsNorthUp);
        Assert.Equal(CoordinateSystemId.Wgs84, fixture.CoordinateSystem);
        Assert.Equal(2d, fixture.Transform.OriginX);
        Assert.Equal(49d, fixture.Transform.OriginY);
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void Plane_Utm31_WritesProjectedGeoKeys()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Crs = SyntheticGeoTiffCrs.Wgs84Utm31North });

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(new[] { 32d, 16d, 0d }, ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG));
        Assert.Equal(new[] { 0d, 0d, 0d, 600000d, 5600000d, 0d }, ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELTIEPOINTTAG));

        AssertGeoKey(fixture, 1024, 1);
        AssertGeoKey(fixture, 3072, 32631);
        Assert.Equal(-1, GeoKeyIndex(fixture, 2048));
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), fixture.CoordinateSystem);
        Assert.True(fixture.IsSupportedCrs);
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void Plane_ModelTransformation_WritesMatrixInsteadOfScaleAndTiepoint()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { UseModelTransformation = true });

        Assert.True(fixture.UsesModelTransformation);

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Null(tiff.GetField(TiffTag.GEOTIFF_MODELPIXELSCALETAG));
        Assert.Null(tiff.GetField(TiffTag.GEOTIFF_MODELTIEPOINTTAG));
        Assert.Equal(
            new[]
            {
                0.25d, 0d, 0d, 2d,
                0d, -0.125d, 0d, 49d,
                0d, 0d, 1d, 0d,
                0d, 0d, 0d, 1d,
            },
            ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG));
        Assert.Equal(fixture.StoredTransform, fixture.Transform);
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void PixelScaleTag_SignFollowsSpecAndMatchesMatrixEncoding()
    {
        // GeoTIFF 1.1 (OGC 19-008r4) section 7.3.2, Requirements 10.4 and 10.5: a positive
        // tag ScaleY means model Y decreases as raster row J increases, and flipped rasters
        // reverse the tag sign. Decode the tags independently of the fixture expectations and
        // verify both encodings describe the same raster-to-world mapping.
        using var northUp = SyntheticGeoTiffWriter.Plane(1d, 1d);
        using var southUp = SyntheticGeoTiffWriter.SouthUp();
        using var matrixForm = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { UseModelTransformation = true });

        AffineTransform fromScaleAndTiepoint;

        using (var tiff = Tiff.Open(northUp.FilePath, "r"))
        {
            var scale = ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG);
            var tiepoint = ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELTIEPOINTTAG);
            Assert.True(scale[1] > 0d);
            fromScaleAndTiepoint = new AffineTransform(tiepoint[3], scale[0], 0d, tiepoint[4], 0d, -scale[1]);
            Assert.Equal(northUp.StoredTransform, fromScaleAndTiepoint);
        }

        using (var tiff = Tiff.Open(southUp.FilePath, "r"))
        {
            var scale = ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG);
            var tiepoint = ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELTIEPOINTTAG);
            Assert.True(scale[1] < 0d);
            Assert.Equal(southUp.StoredTransform, new AffineTransform(tiepoint[3], scale[0], 0d, tiepoint[4], 0d, -scale[1]));
        }

        using (var tiff = Tiff.Open(matrixForm.FilePath, "r"))
        {
            var matrix = ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG);
            var fromMatrix = new AffineTransform(matrix[3], matrix[0], matrix[1], matrix[7], matrix[4], matrix[5]);
            Assert.Equal(matrixForm.StoredTransform, fromMatrix);

            var pixel = new Vector2d(3.5d, 2.5d);
            Assert.Equal(fromScaleAndTiepoint.PixelToWorld(pixel), fromMatrix.PixelToWorld(pixel));
        }
    }

    [Fact]
    public void Ridge_WritesInt16Samples()
    {
        using var fixture = SyntheticGeoTiffWriter.Ridge(
            new SyntheticGeoTiffOptions { SampleFormat = RasterSampleFormat.Int16 });

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(16, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToShort());
        Assert.Equal(SampleFormat.INT, (SampleFormat)tiff.GetField(TiffTag.SAMPLEFORMAT)[0].ToShort());
        Assert.Equal(30d, fixture.GetExpectedSample(0, 0));
        Assert.Equal(110d, fixture.GetExpectedSample(2, 3));
        Assert.Equal(60d, fixture.GetExpectedSample(5, 7));
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void WithNoDataHole_WritesNoDataTagAndHoleSamples()
    {
        using var fixture = SyntheticGeoTiffWriter.WithNoDataHole();

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal("-9999", tiff.GetField(TiffTag.GDAL_NODATA)[0].ToString());
        Assert.True(fixture.HasNoData);
        Assert.Equal("-9999", fixture.NoDataText);
        Assert.Equal(-9999d, fixture.NoDataValue);
        Assert.Equal(10d, fixture.GetExpectedSample(0, 0));
        Assert.Equal(-9999d, fixture.GetExpectedSample(2, 3));
        Assert.Equal(-9999d, fixture.GetExpectedSample(3, 4));
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void SouthUp_WritesPositiveRowScale()
    {
        using var fixture = SyntheticGeoTiffWriter.SouthUp();

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        var pixelScale = ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG);

        // South-up is a flipped orientation: the tag reverses the sign (negative), while the
        // affine row scale itself stays positive.
        Assert.Equal(-0.125d, pixelScale[1]);

        Assert.False(fixture.StoredTransform.IsNorthUp);
        Assert.Equal(0.125d, fixture.Transform.ScaleY);
        Assert.True(fixture.Transform.ScaleY > 0d);
        AssertGeoKey(fixture, 1025, 1);
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void Imagery_Rgb_WritesThreeControlPixelBands()
    {
        using var fixture = SyntheticGeoTiffWriter.Imagery();

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(3, tiff.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt());
        Assert.Equal(8, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToShort());
        Assert.Equal(Photometric.RGB, (Photometric)tiff.GetField(TiffTag.PHOTOMETRIC)[0].ToShort());
        Assert.Null(tiff.GetField(TiffTag.EXTRASAMPLES));
        Assert.Equal(3, fixture.BandCount);
        Assert.Equal(RasterSampleFormat.UInt8, fixture.SampleFormat);
        Assert.Equal((29d * 7 + 7d * 5) % 251d, fixture.GetExpectedSample(5, 7, 0));
        Assert.Equal((11d * 7 + 61d * 5) % 241d, fixture.GetExpectedSample(5, 7, 1));
        Assert.Equal((3d * 7 + 13d * 5) % 253d, fixture.GetExpectedSample(5, 7, 2));
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void Imagery_Rgba_WritesAlphaExtraSample()
    {
        using var fixture = SyntheticGeoTiffWriter.Imagery(
            new SyntheticGeoTiffOptions { BandCount = 4 });

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(4, tiff.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt());
        var extraSamples = tiff.GetField(TiffTag.EXTRASAMPLES);
        Assert.NotNull(extraSamples);
        Assert.Equal(1, extraSamples[0].ToInt());
        Assert.Equal((5d * 0 + 17d * 0) % 256d, fixture.GetExpectedSample(0, 0, 3));
        Assert.Equal((5d * 7 + 17d * 5) % 256d, fixture.GetExpectedSample(5, 7, 3));
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void Classification_WritesGivenUint8Values()
    {
        var values = Enumerable.Range(0, 8 * 6).Select(static i => i % 4 == 0 ? 0 : 255 - (i % 8)).ToArray();

        using var fixture = SyntheticGeoTiffWriter.Classification(values);

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(8, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToShort());
        Assert.Equal(SampleFormat.UINT, (SampleFormat)tiff.GetField(TiffTag.SAMPLEFORMAT)[0].ToShort());
        Assert.Equal(1, tiff.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt());
        Assert.Equal(0d, fixture.GetExpectedSample(0, 0));
        Assert.Equal(254d, fixture.GetExpectedSample(0, 1));
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void Epsg27700_WritesUnsupportedCrs()
    {
        using var fixture = SyntheticGeoTiffWriter.Epsg27700();

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        AssertGeoKey(fixture, 1024, 1);
        AssertGeoKey(fixture, 3072, 27700);
        Assert.False(fixture.IsSupportedCrs);
        Assert.Equal(27700, fixture.CoordinateSystem.EpsgCode);
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void PixelIsPoint_ShiftsExpectedTransformByHalfPixel()
    {
        using var fixture = SyntheticGeoTiffWriter.PixelIsPoint();

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        AssertGeoKey(fixture, 1025, 2);
        Assert.NotNull(tiff.GetField(TiffTag.GEOTIFF_MODELPIXELSCALETAG));

        Assert.True(fixture.PixelIsPoint);
        Assert.NotEqual(fixture.StoredTransform, fixture.Transform);
        Assert.Equal(-0.125d, fixture.Transform.OriginX - fixture.StoredTransform.OriginX);
        Assert.Equal(0.0625d, fixture.Transform.OriginY - fixture.StoredTransform.OriginY);
        Assert.Equal(fixture.StoredTransform.ScaleX, fixture.Transform.ScaleX);
        Assert.Equal(fixture.StoredTransform.ScaleY, fixture.Transform.ScaleY);
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void VerticalCrsKey_IsWrittenWhenRequested()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { VerticalCrsEpsg = 5773 });

        AssertGeoKey(fixture, 4096, 5773);
        Assert.Equal((ushort)1, fixture.GeoKeys[2]);

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(fixture.GeoKeys.ToArray(), ReadGeoKeyTag(tiff));
    }

    [Fact]
    public void TiledLayout_StoresSamplesInTiles()
    {
        // 20 x 20 with 16 x 16 tiles exercises two tile columns and two tile rows, with padding.
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            2d,
            new SyntheticGeoTiffOptions { Width = 20, Height = 20, Layout = SyntheticGeoTiffLayout.Tiled });

        Assert.True(fixture.IsTiled);

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.True(tiff.IsTiled());
        Assert.Equal(SyntheticGeoTiffWriter.TileWidth, tiff.GetField(TiffTag.TILEWIDTH)[0].ToInt());
        Assert.Equal(SyntheticGeoTiffWriter.TileHeight, tiff.GetField(TiffTag.TILELENGTH)[0].ToInt());
        AssertSamplesMatchFile(fixture, tiff);
    }

    [Fact]
    public void Scalar_WritesInt32AndUint16Formats()
    {
        using (var int32 = SyntheticGeoTiffWriter.Scalar(RasterSampleFormat.Int32))
        {
            using var tiff = Tiff.Open(int32.FilePath, "r");
            Assert.Equal(32, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToShort());
            Assert.Equal(SampleFormat.INT, (SampleFormat)tiff.GetField(TiffTag.SAMPLEFORMAT)[0].ToShort());
            Assert.Equal(3d, int32.GetExpectedSample(1, 0));
            AssertSamplesMatchFile(int32, tiff);
        }

        using (var uint16 = SyntheticGeoTiffWriter.Scalar(RasterSampleFormat.UInt16))
        {
            using var tiff = Tiff.Open(uint16.FilePath, "r");
            Assert.Equal(16, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToShort());
            Assert.Equal(SampleFormat.UINT, (SampleFormat)tiff.GetField(TiffTag.SAMPLEFORMAT)[0].ToShort());
            AssertSamplesMatchFile(uint16, tiff);
        }
    }

    [Fact]
    public void Malformed_PixelScaleTooShort_LeavesTwoValueScale()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Malformation = SyntheticGeoTiffMalformation.PixelScaleTooShort });

        Assert.True(fixture.IsMalformed);
        Assert.Null(fixture.StoredTransform);
        Assert.Null(fixture.Transform);

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(2, ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG).Length);
    }

    [Fact]
    public void Malformed_TiepointTooShort_LeavesFourValueTiepoint()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Malformation = SyntheticGeoTiffMalformation.TiepointTooShort });

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        Assert.Equal(4, ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELTIEPOINTTAG).Length);
    }

    [Fact]
    public void Malformed_TruncatedGeoKeys_HeadersMoreKeysThanPresent()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Malformation = SyntheticGeoTiffMalformation.TruncatedGeoKeys });

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        var directory = ReadGeoKeyTag(tiff);
        Assert.Equal(fixture.GeoKeys.ToArray(), directory);
        Assert.NotEqual((int)directory[3], (directory.Length - 4) / 4);
    }

    [Fact]
    public void Malformed_ZeroPixelScale_WritesDegenerateScale()
    {
        using var fixture = SyntheticGeoTiffWriter.Plane(
            1d,
            1d,
            new SyntheticGeoTiffOptions { Malformation = SyntheticGeoTiffMalformation.ZeroPixelScale });

        using var tiff = Tiff.Open(fixture.FilePath, "r");
        var pixelScale = ReadDoubleTag(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG);
        Assert.Equal(0d, pixelScale[0]);
    }

    [Fact]
    public void Write_FailureLeavesNoTempDirectory()
    {
        // VerticalCrsEpsg beyond ushort range fails during fixture assembly (checked cast);
        // a failed write must not leave a panopticon-geotiff directory behind.
        var before = Directory.GetDirectories(Path.GetTempPath(), "panopticon-geotiff-*");

        Assert.Throws<OverflowException>(
            () => SyntheticGeoTiffWriter.Plane(1d, 1d, new SyntheticGeoTiffOptions { VerticalCrsEpsg = 65536 }));

        var after = Directory.GetDirectories(Path.GetTempPath(), "panopticon-geotiff-*");
        Assert.Equal(before.Length, after.Length);
    }

    [Fact]
    public void Dispose_RemovesTheTempDirectory()
    {
        SyntheticGeoTiffFixture fixture = SyntheticGeoTiffWriter.Plane(1d, 1d);
        var directory = Path.GetDirectoryName(fixture.FilePath);

        Assert.True(Directory.Exists(directory));

        fixture.Dispose();

        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(fixture.FilePath));
    }

    private static double[] ReadDoubleTag(Tiff tiff, TiffTag tag)
    {
        var fields = tiff.GetField(tag);
        Assert.NotNull(fields);

        return fields[fields.Length - 1].ToDoubleArray();
    }

    private static ushort[] ReadGeoKeyTag(Tiff tiff)
    {
        var fields = tiff.GetField(TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG);
        Assert.NotNull(fields);

        return fields[fields.Length - 1].ToShortArray().Select(static value => (ushort)value).ToArray();
    }

    private static int GeoKeyIndex(SyntheticGeoTiffFixture fixture, ushort keyId)
    {
        for (var i = 4; i < fixture.GeoKeys.Count; i += 4)
        {
            if (fixture.GeoKeys[i] == keyId)
            {
                return i;
            }
        }

        return -1;
    }

    private static void AssertGeoKey(SyntheticGeoTiffFixture fixture, ushort keyId, ushort expectedValue)
    {
        var index = GeoKeyIndex(fixture, keyId);
        Assert.True(index >= 0, $"GeoKey {keyId} is absent.");
        Assert.Equal(1, (int)fixture.GeoKeys[index + 2]);
        Assert.Equal(expectedValue, fixture.GeoKeys[index + 3]);
    }

    private static void AssertSamplesMatchFile(SyntheticGeoTiffFixture fixture, Tiff tiff)
    {
        Assert.Equal(fixture.ExpectedSamples.ToArray(), ReadBackSamples(tiff, fixture));
    }

    private static double[] ReadBackSamples(Tiff tiff, SyntheticGeoTiffFixture fixture)
    {
        var result = new double[fixture.Width * fixture.Height * fixture.BandCount];

        if (fixture.IsTiled)
        {
            ReadBackTiled(tiff, fixture, result);
        }
        else
        {
            ReadBackStripped(tiff, fixture, result);
        }

        return result;
    }

    private static void ReadBackStripped(Tiff tiff, SyntheticGeoTiffFixture fixture, double[] result)
    {
        var rowBytes = new byte[fixture.Width * fixture.BandCount * fixture.BytesPerSample];

        for (var row = 0; row < fixture.Height; row++)
        {
            Assert.True(tiff.ReadScanline(rowBytes, row));
            DecodeRow(rowBytes, result, fixture, row, fixture.Width, 0);
        }
    }

    private static void ReadBackTiled(Tiff tiff, SyntheticGeoTiffFixture fixture, double[] result)
    {
        var tileWidth = tiff.GetField(TiffTag.TILEWIDTH)[0].ToInt();
        var tileHeight = tiff.GetField(TiffTag.TILELENGTH)[0].ToInt();
        var tileBytes = new byte[tileWidth * tileHeight * fixture.BandCount * fixture.BytesPerSample];
        var rowBytes = new byte[tileWidth * fixture.BandCount * fixture.BytesPerSample];

        for (var tileRow = 0; tileRow * tileHeight < fixture.Height; tileRow++)
        {
            for (var tileColumn = 0; tileColumn * tileWidth < fixture.Width; tileColumn++)
            {
                Assert.Equal(
                    tileBytes.Length,
                    tiff.ReadTile(tileBytes, 0, tileColumn * tileWidth, tileRow * tileHeight, 0, (short)0));

                for (var y = 0; y < tileHeight; y++)
                {
                    var row = tileRow * tileHeight + y;

                    if (row >= fixture.Height)
                    {
                        break;
                    }

                    Buffer.BlockCopy(tileBytes, y * rowBytes.Length, rowBytes, 0, rowBytes.Length);
                    DecodeRow(rowBytes, result, fixture, row, tileWidth, tileColumn * tileWidth);
                }
            }
        }
    }

    private static void DecodeRow(byte[] rowBytes, double[] result, SyntheticGeoTiffFixture fixture, int row, int bufferWidth, int columnOffset)
    {
        for (var column = 0; column < bufferWidth && columnOffset + column < fixture.Width; column++)
        {
            for (var band = 0; band < fixture.BandCount; band++)
            {
                var byteIndex = (column * fixture.BandCount + band) * fixture.BytesPerSample;
                var value = fixture.SampleFormat switch
                {
                    RasterSampleFormat.UInt8 => (double)rowBytes[byteIndex],
                    RasterSampleFormat.Int16 => (double)BitConverter.ToInt16(rowBytes, byteIndex),
                    RasterSampleFormat.UInt16 => (double)BitConverter.ToUInt16(rowBytes, byteIndex),
                    RasterSampleFormat.Int32 => (double)BitConverter.ToInt32(rowBytes, byteIndex),
                    RasterSampleFormat.Float32 => (double)BitConverter.ToSingle(rowBytes, byteIndex),
                    _ => throw new NotSupportedException($"Sample format {fixture.SampleFormat}."),
                };
                result[(row * fixture.Width + columnOffset + column) * fixture.BandCount + band] = value;
            }
        }
    }
}
