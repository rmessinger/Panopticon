using System;
using System.IO;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Readers;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Proves the BCL-only float32 GeoTIFF writer against the production reader (preflight +
/// LibTiff + GeoTIFF key parsing), with no fixture-project dependency: dimensions, sample
/// format, CRS, vertical datum, georeferencing, the four raster corners, and the north-first
/// row orientation are all asserted from values the real reader returned.
/// </summary>
public class BclFloat32GeoTiffTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencode", "bcl-geotiff-tests", Guid.NewGuid().ToString("N"));

    private readonly ITestOutputHelper _output;

    public BclFloat32GeoTiffTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Write_ProducesReaderAcceptedNorthUpFloat32Raster()
    {
        const int width = 4;
        const int height = 3;
        const double originX = 499975d;
        const double originY = 6002585d;

        // Every cell distinct so any orientation mix-up fails a value assertion.
        float Value(int row, int column) => 100f + (10f * column) + row;

        var samples = new float[width * height];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                samples[(row * width) + column] = Value(row, column);
            }
        }

        var path = Path.Combine(_root, "raster.tif");
        BclFloat32GeoTiff.Write(path, width, height, samples, originX, originY, 10d, 32631, 5773);

        var diagnostics = new GeoDiagnosticBag();
        var raster = new GeoTiffRasterReader().Read(path, new GeospatialImportLimits(), diagnostics);

        foreach (var diagnostic in diagnostics.Items)
        {
            _output.WriteLine($"{diagnostic.Code} [{diagnostic.Stage}/{diagnostic.Severity}] {diagnostic.Message}");
        }

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(raster);
        Assert.Equal(width, raster!.Width);
        Assert.Equal(height, raster.Height);
        Assert.Equal(1, raster.BandCount);
        Assert.Equal(RasterSampleFormat.Float32, raster.SampleFormat);
        Assert.False(raster.HasNoData);
        Assert.Equal("EGM96", raster.VerticalDatum);
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), raster.CoordinateSystem);

        Assert.True(raster.Transform.IsNorthUp);
        Assert.Equal(10d, raster.Transform.ScaleX);
        Assert.Equal(-10d, raster.Transform.ScaleY);
        Assert.Equal(originX, raster.Transform.OriginX);
        Assert.Equal(originY, raster.Transform.OriginY);

        // The four raster corners in model space: row 0 is the northern edge, column 0 the western.
        Assert.Equal((originX, originY), (raster.Transform.PixelToWorld(new Vector2d(0d, 0d)).X, raster.Transform.PixelToWorld(new Vector2d(0d, 0d)).Y));
        Assert.Equal(originX + (width * 10d), raster.Transform.PixelToWorld(new Vector2d(width, 0d)).X);
        Assert.Equal(originY - (height * 10d), raster.Transform.PixelToWorld(new Vector2d(0d, height)).Y);

        // The four value corners: north-west, north-east, south-west, south-east. Row 0 holds
        // the smaller row component, which together with the north-up transform proves the
        // source orientation is north-first rather than inferred.
        Assert.Equal(100f, raster.Get(0, 0, 0));
        Assert.Equal(130f, raster.Get(0, 0, width - 1));
        Assert.Equal(102f, raster.Get(0, height - 1, 0));
        Assert.Equal(132f, raster.Get(0, height - 1, width - 1));
        Assert.True(raster.Get(0, 0, 0) < raster.Get(0, height - 1, 0), "Row 0 must be the northern edge of a north-up raster, so it holds the smaller row component.");
    }

    [Fact]
    public void Write_RejectsMismatchedSampleBuffer()
    {
        var path = Path.Combine(_root, "invalid.tif");

        Assert.Throws<ArgumentException>(
            () => BclFloat32GeoTiff.Write(path, 3, 3, new float[8], 0d, 0d, 10d, 32631));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
