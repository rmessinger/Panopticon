using System;
using System.Threading.Tasks;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Projection;
using Xunit;

namespace Panopticon.Geospatial.Tests.Projection;

public class CoordinateTransformerTests
{
    private const double Millimeter = 0.001d;

    private const double MetersPerDegree = 111319.9d;

    private static readonly WgsTransverseMercatorTransformer Transformer = new();

    private static readonly CoordinateSystemId Wgs84 = CoordinateSystemId.Wgs84;

    private static readonly CoordinateSystemId Utm31North = CoordinateSystemId.Utm(31, northernHemisphere: true);

    private static readonly CoordinateSystemId Utm32North = CoordinateSystemId.Utm(32, northernHemisphere: true);

    private static readonly CoordinateSystemId Custom = CoordinateSystemId.TransverseMercator(
        3.5d,
        0.9996d,
        500000d,
        10000000d);

    [Fact]
    public void Utm31ToUtm32NearSixEastRoundTripsWithinOneMillimeter()
    {
        // 6 E is the zone 31/32 boundary; a point just inside zone 31 exercises
        // the full UTM31 -> 4326 -> UTM32 pivot path.
        var geographic = new Vector2d(5.9d, 49.5d);
        var zone31 = Transformer.Transform(geographic, Wgs84, Utm31North);
        var zone32 = Transformer.Transform(zone31, Utm31North, Utm32North);
        var roundTrip = Transformer.Transform(zone32, Utm32North, Utm31North);

        Assert.True(
            Math.Abs(roundTrip.X - zone31.X) <= Millimeter,
            $"Easting round-trip error {Math.Abs(roundTrip.X - zone31.X)} m exceeds 1 mm.");
        Assert.True(
            Math.Abs(roundTrip.Y - zone31.Y) <= Millimeter,
            $"Northing round-trip error {Math.Abs(roundTrip.Y - zone31.Y)} m exceeds 1 mm.");

        var direct = Transformer.Transform(geographic, Wgs84, Utm32North);

        Assert.True(
            Math.Abs(zone32.X - direct.X) <= Millimeter,
            $"Pivoted easting differs from direct by {Math.Abs(zone32.X - direct.X)} m.");
        Assert.True(
            Math.Abs(zone32.Y - direct.Y) <= Millimeter,
            $"Pivoted northing differs from direct by {Math.Abs(zone32.Y - direct.Y)} m.");
    }

    [Fact]
    public void Epsg27700IsRejectedWithTheUnsupportedCrsDiagnostic()
    {
        var osgb = CoordinateSystemId.FromEpsg(27700);
        var point = new Vector2d(100000d, 500000d);

        Assert.False(Transformer.IsSupported(osgb));
        Assert.Throws<NotSupportedException>(() => Transformer.Transform(point, osgb, osgb));
        Assert.Throws<NotSupportedException>(() => Transformer.Transform(point, Wgs84, osgb));

        var transformed = Transformer.TryTransform(point, osgb, osgb, out var result, out var diagnostic);

        Assert.False(transformed);
        Assert.Equal(default(Vector2d), result);
        Assert.NotNull(diagnostic);
        Assert.Equal(GeoDiagnosticCodes.CoordinatesUnsupportedCrs, diagnostic.Code);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
        Assert.Equal("Coordinate system is not supported: EPSG:27700.", diagnostic.Message);
    }

    [Fact]
    public void CustomTransverseMercatorMatchesTheDirectProjection()
    {
        var projection = new TransverseMercatorProjection(3.5d, 0.9996d, 500000d, 10000000d);
        var geographic = new Vector2d(4.13d, -23.7d);

        var forward = Transformer.Transform(geographic, Wgs84, Custom);
        var expectedForward = projection.Forward(geographic.Y, geographic.X);

        Assert.Equal(expectedForward.X, forward.X);
        Assert.Equal(expectedForward.Y, forward.Y);

        var inverse = Transformer.Transform(forward, Custom, Wgs84);
        var expectedInverse = projection.Inverse(forward.X, forward.Y);

        Assert.Equal(expectedInverse.LongitudeDegrees, inverse.X);
        Assert.Equal(expectedInverse.LatitudeDegrees, inverse.Y);
    }

    [Fact]
    public void AllNorthAndSouthUtmZonesAreSupportedAndRoundTrip()
    {
        for (var zone = 1; zone <= 60; zone++)
        {
            RoundTripUtmZone(CoordinateSystemId.Utm(zone, northernHemisphere: true), zone, 20d + 0.5d * zone);
            RoundTripUtmZone(CoordinateSystemId.Utm(zone, northernHemisphere: false), zone, -(20d + 0.5d * zone));
        }
    }

    [Fact]
    public void IsSupportedAcceptsOnlyTheSupportedSystems()
    {
        foreach (var id in new[]
                 {
                     Wgs84,
                     CoordinateSystemId.FromEpsg(32601),
                     CoordinateSystemId.FromEpsg(32660),
                     CoordinateSystemId.FromEpsg(32701),
                     CoordinateSystemId.FromEpsg(32760),
                     Custom,
                     CoordinateSystemId.TransverseMercator(0d, 1d, 0d, 0d),
                 })
        {
            Assert.True(Transformer.IsSupported(id), $"{id} should be supported.");
        }

        foreach (var id in new[]
                 {
                     default(CoordinateSystemId),
                     CoordinateSystemId.FromEpsg(27700),
                     CoordinateSystemId.FromEpsg(3857),
                     CoordinateSystemId.FromEpsg(32600),
                     CoordinateSystemId.FromEpsg(32661),
                     CoordinateSystemId.FromEpsg(32700),
                     CoordinateSystemId.FromEpsg(32761),
                 })
        {
            Assert.False(Transformer.IsSupported(id), $"{id} should not be supported.");
        }
    }

    [Fact]
    public void GeographicIdentityReturnsThePointUnchanged()
    {
        var geographic = new Vector2d(2.29451d, 48.85826d);

        Assert.Equal(geographic, Transformer.Transform(geographic, Wgs84, Wgs84));

        var transformed = Transformer.TryTransform(geographic, Wgs84, Wgs84, out var result, out var diagnostic);

        Assert.True(transformed);
        Assert.Null(diagnostic);
        Assert.Equal(geographic, result);
    }

    [Fact]
    public void GeographicLatitudeRangeIsEnforcedEvenForIdentity()
    {
        foreach (var latitude in new[] { 90.0000001d, -90.0000001d, 91d, -91d })
        {
            var point = new Vector2d(0d, latitude);

            Assert.ThrowsAny<ArgumentException>(() => Transformer.Transform(point, Wgs84, Wgs84));
            FailsWithCoordinatesDiagnostic(point, Wgs84, Wgs84);
        }

        // Inclusive poles stay valid.
        Assert.Equal(new Vector2d(0d, 90d), Transformer.Transform(new Vector2d(0d, 90d), Wgs84, Wgs84));
    }

    [Fact]
    public void DefaultCoordinateSystemIdIsRejected()
    {
        var id = default(CoordinateSystemId);

        Assert.False(Transformer.IsSupported(id));
        Assert.Throws<NotSupportedException>(() => Transformer.Transform(new Vector2d(0d, 0d), id, Wgs84));
        Assert.Throws<NotSupportedException>(() => Transformer.Transform(new Vector2d(0d, 0d), Wgs84, id));

        var transformed = Transformer.TryTransform(new Vector2d(0d, 0d), id, id, out _, out var diagnostic);

        Assert.False(transformed);
        Assert.NotNull(diagnostic);
        Assert.Equal(GeoDiagnosticCodes.CoordinatesUnsupportedCrs, diagnostic.Code);
        Assert.Equal("Coordinate system is not supported: TM(cm=0,k=0,fe=0,fn=0).", diagnostic.Message);
    }

    [Fact]
    public void NonFiniteCoordinatesFailWithTheCoordinatesDiagnostic()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            FailsWithCoordinatesDiagnostic(new Vector2d(bad, 10d), Wgs84, Utm31North);
            FailsWithCoordinatesDiagnostic(new Vector2d(3d, bad), Wgs84, Wgs84);
            FailsWithCoordinatesDiagnostic(new Vector2d(bad, 5000000d), Utm31North, Wgs84);
            FailsWithCoordinatesDiagnostic(new Vector2d(500000d, bad), Utm31North, Custom);
        }
    }

    [Fact]
    public void PointsOutsideTheProjectionDomainFailWithTheCoordinatesDiagnostic()
    {
        // 1e12 m easting is far outside the invertible domain of the inverse.
        FailsWithCoordinatesDiagnostic(new Vector2d(1e12d, 0d), Utm31North, Wgs84);

        // The equator branch point 90 degrees from the central meridian is not projectable.
        var plain = CoordinateSystemId.TransverseMercator(0d, 1d, 0d, 0d);

        FailsWithCoordinatesDiagnostic(new Vector2d(90d, 0d), Wgs84, plain);
    }

    [Fact]
    public void NorthAndSouthUtmTransformThroughTheGeographicPivot()
    {
        var geographic = new Vector2d(151.2093d, -33.8688d);
        var utm56South = CoordinateSystemId.Utm(56, northernHemisphere: false);
        var utm56North = CoordinateSystemId.Utm(56, northernHemisphere: true);

        var southern = Transformer.Transform(geographic, Wgs84, utm56South);
        var northern = Transformer.Transform(southern, utm56South, utm56North);

        Assert.True(Math.Abs(northern.X - southern.X) <= 1e-6d, "Eastings differ between hemisphere zones.");
        Assert.True(
            Math.Abs(southern.Y - northern.Y - 10000000d) <= 1e-6d,
            $"Northing difference {southern.Y - northern.Y} m is not the 10000000 m offset.");

        var roundTrip = Transformer.Transform(northern, utm56North, utm56South);

        Assert.True(Math.Abs(roundTrip.X - southern.X) <= Millimeter, "Round-trip easting differs.");
        Assert.True(Math.Abs(roundTrip.Y - southern.Y) <= Millimeter, "Round-trip northing differs.");
    }

    [Fact]
    public void ConcurrentTransformsAcrossZonesStayCorrect()
    {
        Parallel.For(1, 61, zone =>
        {
            var utm = CoordinateSystemId.Utm(zone, northernHemisphere: zone % 2 == 0);
            var latitude = zone % 2 == 0 ? 20d + 0.5d * zone : -(20d + 0.5d * zone);
            RoundTripUtmZone(utm, zone, latitude);
        });
    }

    [Fact]
    public void WrappedGeographicLongitudeTransformsLikeItsNormalizedForm()
    {
        var plain = CoordinateSystemId.TransverseMercator(0d, 1d, 0d, 0d);
        var direct = Transformer.Transform(new Vector2d(2d, 30d), Wgs84, plain);
        var wrapped = Transformer.Transform(new Vector2d(362d, 30d), Wgs84, plain);

        Assert.Equal(direct.X, wrapped.X);
        Assert.Equal(direct.Y, wrapped.Y);
    }

    private static void RoundTripUtmZone(CoordinateSystemId utm, int zone, double latitude)
    {
        Assert.True(Transformer.IsSupported(utm), $"{utm} is not supported.");

        var centralMeridian = 6d * zone - 183d;
        var geographic = new Vector2d(centralMeridian, latitude);
        var projected = Transformer.Transform(geographic, Wgs84, utm);
        var expected = TransverseMercatorProjection.Utm(zone, latitude >= 0d).Forward(latitude, centralMeridian);

        Assert.Equal(expected.X, projected.X);
        Assert.Equal(expected.Y, projected.Y);

        var roundTrip = Transformer.Transform(projected, utm, Wgs84);
        var tolerance = Millimeter / MetersPerDegree;

        Assert.True(
            Math.Abs(roundTrip.Y - geographic.Y) <= tolerance,
            $"{utm} latitude round-trip error {Math.Abs(roundTrip.Y - geographic.Y)} deg.");
        Assert.True(
            Math.Abs(roundTrip.X - geographic.X) <= tolerance,
            $"{utm} longitude round-trip error {Math.Abs(roundTrip.X - geographic.X)} deg.");
    }

    private static void FailsWithCoordinatesDiagnostic(Vector2d point, CoordinateSystemId source, CoordinateSystemId target)
    {
        Assert.ThrowsAny<ArgumentException>(() => Transformer.Transform(point, source, target));

        var transformed = Transformer.TryTransform(point, source, target, out var result, out var diagnostic);

        Assert.False(transformed);
        Assert.Equal(default(Vector2d), result);
        Assert.NotNull(diagnostic);
        Assert.Equal(GeoDiagnosticCodes.CoordinatesFailure, diagnostic.Code);
        Assert.Equal(GeoStage.Coordinates, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
        Assert.NotNull(diagnostic.Detail);
        Assert.Equal(
            $"Coordinate transform from {source} to {target} failed: coordinates are not finite or outside the transformable domain.",
            diagnostic.Message);
    }
}
