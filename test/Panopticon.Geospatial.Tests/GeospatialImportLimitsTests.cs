using Xunit;

namespace Panopticon.Geospatial.Tests;

public class GeospatialImportLimitsTests
{
    [Fact]
    public void DefaultsMatchDeepDiveSection7()
    {
        var limits = new GeospatialImportLimits();

        Assert.Equal(1024 * 1024, limits.MaxManifestBytes);
        Assert.Equal(64L * 1024 * 1024, limits.MaxKmlEntryBytes);
        Assert.Equal(1000, limits.MaxKmzEntryCount);
        Assert.Equal(512L * 1024 * 1024, limits.MaxKmzExpandedBytes);
        Assert.Equal(100, limits.MaxKmzCompressionRatio);
        Assert.Equal(65536, limits.MaxRasterWidth);
        Assert.Equal(65536, limits.MaxRasterHeight);
        Assert.Equal(4L * 1024 * 1024 * 1024, limits.MaxRasterDecodedBytes);
        Assert.Equal(1_000_000, limits.MaxFeatureCount);
        Assert.Equal(50_000_000, limits.MaxCoordinateCount);
        Assert.Equal(65536, limits.MaxChunkCount);
    }

    [Fact]
    public void OverridesKeepUnspecifiedLimits()
    {
        var limits = new GeospatialImportLimits(MaxChunkCount: 16, MaxManifestBytes: 2048);

        Assert.Equal(16, limits.MaxChunkCount);
        Assert.Equal(2048, limits.MaxManifestBytes);
        Assert.Equal(1000, limits.MaxKmzEntryCount);
        Assert.Equal(65536, limits.MaxRasterWidth);
    }
}
