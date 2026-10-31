using System;
using System.Collections.Generic;
using Panopticon.Geospatial.Interchange;
using Xunit;

namespace Panopticon.Geospatial.Tests.Interchange;

public class SourceProvenanceAndMapTransformTests
{
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ProvenanceStoresValues()
    {
        var provenance = new SourceProvenance("dem-1", "sources/dem.tif", ValidSha256, 4096);

        Assert.Equal("dem-1", provenance.SourceId);
        Assert.Equal("sources/dem.tif", provenance.RelativePath);
        Assert.Equal(ValidSha256, provenance.Sha256Hex);
        Assert.Equal(4096, provenance.LengthBytes);
    }

    [Fact]
    public void ProvenanceNormalizesUppercaseHexToLowerCase()
    {
        var provenance = new SourceProvenance("dem-1", "sources/dem.tif", ValidSha256.ToUpperInvariant(), 4096);

        Assert.Equal(ValidSha256, provenance.Sha256Hex);
    }

    [Fact]
    public void ProvenanceRejectsInvalidHashAndLength()
    {
        Assert.Throws<ArgumentException>(
            () => new SourceProvenance("dem-1", "sources/dem.tif", "abc", 4096));
        Assert.Throws<ArgumentException>(
            () => new SourceProvenance(
                "dem-1",
                "sources/dem.tif",
                "zzzz456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                4096));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SourceProvenance("dem-1", "sources/dem.tif", ValidSha256, -1));
        Assert.Throws<ArgumentException>(
            () => new SourceProvenance("", "sources/dem.tif", ValidSha256, 4096));
    }

    [Fact]
    public void MapTransformStoresValuesAndCopiesSourceSystems()
    {
        var sources = new List<CoordinateSystemId> { CoordinateSystemId.Wgs84 };
        var transform = new GeospatialMapTransform(
            sources,
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            new Vector2d(500000, 5400000),
            metersPerGameUnit: 2.5,
            verticalDatum: "EGM96",
            verticalOriginMeters: 100,
            verticalExaggeration: 1.5);

        sources.Clear();

        Assert.Equal([CoordinateSystemId.Wgs84], transform.SourceCoordinateSystems);
        Assert.Equal(CoordinateSystemId.FromEpsg(32631), transform.ProjectedCoordinateSystem);
        Assert.Equal(new Vector2d(500000, 5400000), transform.OriginEastingNorthing);
        Assert.Equal(2.5, transform.MetersPerGameUnit);
        Assert.Equal("EGM96", transform.VerticalDatum);
        Assert.Equal(100, transform.VerticalOriginMeters);
        Assert.Equal(1.5, transform.VerticalExaggeration);
    }

    [Fact]
    public void MapTransformRejectsInvalidValues()
    {
        static GeospatialMapTransform Create(
            double metersPerGameUnit = 1,
            double verticalExaggeration = 1,
            string verticalDatum = "EGM96",
            double verticalOriginMeters = 0,
            Vector2d? originEastingNorthing = null)
        {
            return new GeospatialMapTransform(
                [CoordinateSystemId.Wgs84],
                CoordinateSystemId.Utm(31, northernHemisphere: true),
                originEastingNorthing ?? new Vector2d(0, 0),
                metersPerGameUnit,
                verticalDatum,
                verticalOriginMeters,
                verticalExaggeration);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(metersPerGameUnit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(metersPerGameUnit: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(metersPerGameUnit: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(metersPerGameUnit: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(verticalExaggeration: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(verticalExaggeration: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(verticalOriginMeters: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(originEastingNorthing: new Vector2d(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(originEastingNorthing: new Vector2d(0, double.PositiveInfinity)));
        Assert.Throws<ArgumentException>(() => Create(verticalDatum: ""));
    }
}
