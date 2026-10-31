using System;
using Panopticon.Geospatial.Interchange;
using Xunit;

namespace Panopticon.Geospatial.Tests.Interchange;

public class GeospatialRasterTests
{
    private static readonly AffineTransform Identity = new(0d, 1d, 0d, 0d, 0d, -1d);

    [Fact]
    public void ConstructorRejectsMismatchedSampleBuffer()
    {
        var shortBuffer = new double[] { 1d, 2d, 3d };
        var longBuffer = new double[] { 1d, 2d, 3d, 4d, 5d };

        Assert.Throws<ArgumentException>(
            () => new GeospatialRaster<double>(2, 2, 1, Identity, CoordinateSystemId.Wgs84, shortBuffer));
        Assert.Throws<ArgumentException>(
            () => new GeospatialRaster<double>(2, 2, 1, Identity, CoordinateSystemId.Wgs84, longBuffer));
    }

    [Fact]
    public void ConstructorRejectsDimensionsBeyondAnyBuffer()
    {
        var buffer = new double[] { 1d };

        Assert.Throws<ArgumentException>(
            () => new GeospatialRaster<double>(65536, 65536, 3, Identity, CoordinateSystemId.Wgs84, buffer));
    }

    [Fact]
    public void ConstructorUsesCheckedSampleCountArithmetic()
    {
        var buffer = new double[] { 1d };

        Assert.Throws<OverflowException>(
            () => new GeospatialRaster<double>(int.MaxValue, int.MaxValue, 3, Identity, CoordinateSystemId.Wgs84, buffer));
    }

    [Fact]
    public void ConstructorRejectsInvalidDimensionsTransformAndDatum()
    {
        var buffer = new double[] { 1d };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GeospatialRaster<double>(0, 1, 1, Identity, CoordinateSystemId.Wgs84, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GeospatialRaster<double>(1, -1, 1, Identity, CoordinateSystemId.Wgs84, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GeospatialRaster<double>(1, 1, 0, Identity, CoordinateSystemId.Wgs84, buffer));
        Assert.Throws<ArgumentNullException>(
            () => new GeospatialRaster<double>(1, 1, 1, null!, CoordinateSystemId.Wgs84, buffer));
        Assert.Throws<ArgumentException>(
            () => new GeospatialRaster<double>(1, 1, 1, Identity, CoordinateSystemId.Wgs84, buffer, verticalDatum: ""));
    }

    [Fact]
    public void GetIndexesRowMajorBandInterleavedSamples()
    {
        // Band 0 carries the tens, band 1 the units; the buffer interleaves bands per sample.
        var samples = new double[] { 10, 1, 20, 2, 30, 3, 40, 4 };
        var raster = new GeospatialRaster<double>(2, 2, 2, Identity, CoordinateSystemId.Wgs84, samples);

        Assert.Equal(8, raster.Samples.Length);
        Assert.Equal(10d, raster.Get(0, 0, 0));
        Assert.Equal(1d, raster.Get(1, 0, 0));
        Assert.Equal(20d, raster.Get(0, 0, 1));
        Assert.Equal(2d, raster.Get(1, 0, 1));
        Assert.Equal(30d, raster.Get(0, 1, 0));
        Assert.Equal(3d, raster.Get(1, 1, 0));
        Assert.Equal(40d, raster.Get(0, 1, 1));
        Assert.Equal(4d, raster.Get(1, 1, 1));
    }

    [Fact]
    public void GetRejectsOutOfBoundsBandRowColumn()
    {
        var raster = new GeospatialRaster<double>(3, 2, 2, Identity, CoordinateSystemId.Wgs84, new double[12]);

        Assert.Throws<ArgumentOutOfRangeException>(() => raster.Get(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.Get(2, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.Get(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.Get(0, 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.Get(0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.Get(0, 0, 3));
    }

    [Fact]
    public void SamplesAreDefensiveCopies()
    {
        var buffer = new double[] { 1d, 2d, 3d, 4d, 5d, 6d };
        var raster = new GeospatialRaster<double>(3, 2, 1, Identity, CoordinateSystemId.Wgs84, buffer);

        buffer[0] = 99d;

        Assert.Equal(1d, raster.Get(0, 0, 0));
        Assert.Equal(1d, raster.Samples.ToArray()[0]);
        Assert.Equal(1d, raster.Samples.Span[0]);
    }

    [Fact]
    public void NoDataIsOptionalAndExplicit()
    {
        var plain = new GeospatialRaster<double>(1, 1, 1, Identity, CoordinateSystemId.Wgs84, new double[] { 5d });
        var withNoData = new GeospatialRaster<double>(
            1, 1, 1, Identity, CoordinateSystemId.Wgs84, new double[] { 5d }, noData: -9999d, hasNoData: true);

        Assert.False(plain.HasNoData);
        Assert.Equal(default(double), plain.NoData);
        Assert.True(withNoData.HasNoData);
        Assert.Equal(-9999d, withNoData.NoData);
    }

    [Fact]
    public void MetadataDefaultsAndOverridesAreRetained()
    {
        var defaults = new GeospatialRaster<double>(1, 1, 1, Identity, CoordinateSystemId.Wgs84, new double[] { 1d });
        var custom = new GeospatialRaster<double>(
            1,
            1,
            1,
            Identity,
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            new double[] { 1d },
            sampleFormat: RasterSampleFormat.Float32,
            verticalDatum: "EGM96");

        Assert.Equal(RasterSampleFormat.Unknown, defaults.SampleFormat);
        Assert.Equal("unknown", defaults.VerticalDatum);
        Assert.Equal(CoordinateSystemId.Wgs84, defaults.CoordinateSystem);
        Assert.Same(Identity, custom.Transform);
        Assert.Equal(RasterSampleFormat.Float32, custom.SampleFormat);
        Assert.Equal("EGM96", custom.VerticalDatum);
        Assert.Equal(CoordinateSystemId.FromEpsg(32631), custom.CoordinateSystem);
    }
}
