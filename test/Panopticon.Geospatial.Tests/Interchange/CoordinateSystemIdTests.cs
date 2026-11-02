using System;
using System.Globalization;
using Panopticon.Geospatial.Interchange;
using Xunit;

namespace Panopticon.Geospatial.Tests.Interchange;

public class CoordinateSystemIdTests
{
    [Fact]
    public void Wgs84IsEpsg4326()
    {
        Assert.Equal(4326, CoordinateSystemId.Wgs84.EpsgCode);
        Assert.Equal(CoordinateSystemId.FromEpsg(4326), CoordinateSystemId.Wgs84);
        Assert.False(CoordinateSystemId.Wgs84.IsCustomTransverseMercator);
    }

    [Fact]
    public void UtmZonesMapToEpsgCodes()
    {
        Assert.Equal(CoordinateSystemId.FromEpsg(32601), CoordinateSystemId.Utm(1, northernHemisphere: true));
        Assert.Equal(CoordinateSystemId.FromEpsg(32631), CoordinateSystemId.Utm(31, northernHemisphere: true));
        Assert.Equal(CoordinateSystemId.FromEpsg(32660), CoordinateSystemId.Utm(60, northernHemisphere: true));
        Assert.Equal(CoordinateSystemId.FromEpsg(32701), CoordinateSystemId.Utm(1, northernHemisphere: false));
        Assert.Equal(CoordinateSystemId.FromEpsg(32760), CoordinateSystemId.Utm(60, northernHemisphere: false));
    }

    [Fact]
    public void UtmRejectsOutOfRangeZones()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateSystemId.Utm(0, northernHemisphere: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateSystemId.Utm(61, northernHemisphere: false));
    }

    [Fact]
    public void FromEpsgRejectsNonPositiveCodes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateSystemId.FromEpsg(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateSystemId.FromEpsg(-32700));
    }

    [Fact]
    public void TransverseMercatorStoresParameters()
    {
        var id = CoordinateSystemId.TransverseMercator(3.5, 0.9996, 500000, 10000000);

        Assert.Null(id.EpsgCode);
        Assert.True(id.IsCustomTransverseMercator);
        Assert.Equal(3.5, id.CentralMeridianDegrees);
        Assert.Equal(0.9996, id.Scale);
        Assert.Equal(500000, id.FalseEasting);
        Assert.Equal(10000000, id.FalseNorthing);
    }

    [Fact]
    public void TransverseMercatorRejectsNonFiniteAndNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSystemId.TransverseMercator(double.NaN, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSystemId.TransverseMercator(double.PositiveInfinity, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSystemId.TransverseMercator(3.5, double.NaN, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSystemId.TransverseMercator(3.5, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSystemId.TransverseMercator(3.5, -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSystemId.TransverseMercator(3.5, 1, double.NegativeInfinity, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CoordinateSystemId.TransverseMercator(3.5, 1, 0, double.NaN));
    }

    [Fact]
    public void EqualIdsCompareByValue()
    {
        Assert.Equal(
            CoordinateSystemId.TransverseMercator(3.5, 0.9996, 500000, 0),
            CoordinateSystemId.TransverseMercator(3.5, 0.9996, 500000, 0));
        Assert.NotEqual(
            CoordinateSystemId.TransverseMercator(3.5, 0.9996, 500000, 0),
            CoordinateSystemId.TransverseMercator(3.51, 0.9996, 500000, 0));
    }

    [Fact]
    public void ToStringIsStableUnderNonDefaultCulture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("EPSG:4326", CoordinateSystemId.Wgs84.ToString());
            Assert.Equal("EPSG:32631", CoordinateSystemId.Utm(31, northernHemisphere: true).ToString());
            Assert.Equal(
                "TM(cm=3.5,k=0.9996,fe=500000,fn=0)",
                CoordinateSystemId.TransverseMercator(3.5, 0.9996, 500000, 0).ToString());
            Assert.Equal(
                "TM(cm=-1.25,k=1,fe=0,fn=10000000)",
                CoordinateSystemId.TransverseMercator(-1.25, 1, 0, 10000000).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
