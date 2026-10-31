using System;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Projection;
using Xunit;

namespace Panopticon.Geospatial.Tests.Projection;

public class TransverseMercatorTests
{
    private const double Millimeter = 0.001d;
    private const double MetersPerDegree = 111319.9d;
    private const double EquatorialRadius = 6378137d;
    private const double FirstEccentricitySquared = 0.0066943799901413165d;

    private static readonly TransverseMercatorProjection Plain = new(0d, 1d, 0d, 0d);

    private static readonly TransverseMercatorProjection Utm31North = TransverseMercatorProjection.Utm(31, north: true);

    [Fact]
    public void ForwardThenInverseRoundTripsWithinOneMillimeter()
    {
        var worst = WorstGridRoundTripErrorMeters(Plain, centralMeridian: 0d);

        Assert.True(worst <= Millimeter, $"Worst round-trip error {worst} m exceeds 1 mm.");
    }

    [Fact]
    public void UtmRoundTripsWithinOneMillimeter()
    {
        var worst = WorstGridRoundTripErrorMeters(Utm31North, centralMeridian: 3d);

        Assert.True(worst <= Millimeter, $"Worst UTM round-trip error {worst} m exceeds 1 mm.");
    }

    [Fact]
    public void NorthingAt45NorthOnCentralMeridianMatchesMeridionalArc()
    {
        var projected = Plain.Forward(45d, 0d);

        Assert.True(Math.Abs(projected.X) <= 1e-9d, $"Easting {projected.X} m is not zero on the central meridian.");
        Assert.True(
            Math.Abs(projected.Y - 4984944.378d) <= Millimeter,
            $"Northing {projected.Y} m differs from the 4984944.378 m meridional arc.");
    }

    [Fact]
    public void EiffelTowerProjectsToKnownUtm31NorthCoordinates()
    {
        // Anchor from GeographicLib GeoConvert 2.5.2: 31n 448252.5905 5411939.3406.
        var projected = Utm31North.Forward(48.85826d, 2.29451d);

        Assert.True(Math.Abs(projected.X - 448252.59d) <= 5d, $"Easting {projected.X} m is not within 5 m of 448252.59.");
        Assert.True(Math.Abs(projected.Y - 5411939.34d) <= 5d, $"Northing {projected.Y} m is not within 5 m of 5411939.34.");
    }

    [Fact]
    public void ScaleFactorOnCentralMeridianEqualsK0()
    {
        foreach (var projection in new[] { Plain, Utm31North })
        {
            for (var latStep = 0; latStep <= 41; latStep++)
            {
                var latitude = -80d + 4d * latStep;
                var scale = projection.ScaleFactor(latitude, projection.CentralMeridianDegrees);

                Assert.True(
                    Math.Abs(scale - projection.K0) <= 1e-12d,
                    $"Scale {scale} at lat {latitude} differs from k0 {projection.K0}.");
            }
        }
    }

    [Fact]
    public void ScaleFactorMatchesProjectedDistanceRatios()
    {
        foreach (var (latitude, longitude) in new[] { (0d, 0d), (45d, 3d), (60d, -2d) })
        {
            var scale = Plain.ScaleFactor(latitude, longitude);
            var halfStep = 1e-5d;
            var halfStepRadians = halfStep * Math.PI / 180d;
            var sinLatitude = Math.Sin(latitude * Math.PI / 180d);
            var denominator = 1d - FirstEccentricitySquared * sinLatitude * sinLatitude;
            var meridionalRadius = EquatorialRadius * (1d - FirstEccentricitySquared)
                / (denominator * Math.Sqrt(denominator));
            var primeVerticalRadius = EquatorialRadius / Math.Sqrt(denominator);

            var north = Plain.Forward(latitude + halfStep, longitude);
            var south = Plain.Forward(latitude - halfStep, longitude);
            // The projected meridian is curved (grid convergence), so the scale
            // ratio uses the full projected element, not the northing alone.
            var meridionalScale = Distance(north.X - south.X, north.Y - south.Y)
                / (2d * halfStepRadians * meridionalRadius);

            var east = Plain.Forward(latitude, longitude + halfStep);
            var west = Plain.Forward(latitude, longitude - halfStep);
            var parallelScale = Distance(east.X - west.X, east.Y - west.Y)
                / (2d * halfStepRadians * primeVerticalRadius * Math.Cos(latitude * Math.PI / 180d));

            Assert.True(
                Math.Abs(meridionalScale - scale) <= 2e-9d,
                $"Meridional scale {meridionalScale} differs from k {scale} at ({latitude}, {longitude}).");
            Assert.True(
                Math.Abs(parallelScale - scale) <= 2e-9d,
                $"Parallel scale {parallelScale} differs from k {scale} at ({latitude}, {longitude}).");
        }
    }

    [Fact]
    public void ForwardIsSymmetricAboutTheCentralMeridian()
    {
        var east = Plain.Forward(45d, 5d);
        var west = Plain.Forward(45d, -5d);

        Assert.True(Math.Abs(east.X + west.X) <= 1e-9d, $"Eastings {east.X} and {west.X} are not antisymmetric.");
        Assert.True(Math.Abs(east.Y - west.Y) <= 1e-9d, $"Northings {east.Y} and {west.Y} are not equal.");
        Assert.True(
            Math.Abs(Plain.ScaleFactor(45d, 5d) - Plain.ScaleFactor(45d, -5d)) <= 1e-12d,
            "Scale factors either side of the central meridian differ.");
    }

    [Fact]
    public void UtmPresetsUseZoneParameters()
    {
        Assert.Equal(-177d, TransverseMercatorProjection.Utm(1, north: true).CentralMeridianDegrees);
        Assert.Equal(3d, TransverseMercatorProjection.Utm(31, north: true).CentralMeridianDegrees);
        Assert.Equal(177d, TransverseMercatorProjection.Utm(60, north: true).CentralMeridianDegrees);
        Assert.Equal(0.9996d, Utm31North.K0);
        Assert.Equal(500000d, Utm31North.FalseEasting);
        Assert.Equal(0d, Utm31North.FalseNorthing);
        Assert.Equal(10000000d, TransverseMercatorProjection.Utm(31, north: false).FalseNorthing);
    }

    [Fact]
    public void UtmEquatorOnCentralMeridianMapsToFalseOrigins()
    {
        var north = Utm31North.Forward(0d, 3d);
        var south = TransverseMercatorProjection.Utm(31, north: false).Forward(0d, 3d);

        Assert.Equal(500000d, north.X, 6);
        Assert.Equal(0d, north.Y, 6);
        Assert.Equal(500000d, south.X, 6);
        Assert.Equal(10000000d, south.Y, 6);
        Assert.True(Math.Abs(Utm31North.ScaleFactor(0d, 3d) - 0.9996d) <= 1e-12d, "Equator scale is not k0.");
    }

    [Fact]
    public void UtmSouthNorthingComplementsNorthAcrossTheEquator()
    {
        const double latitude = -33.8688d;
        const double longitude = 151.2093d;
        var northern = TransverseMercatorProjection.Utm(56, north: true).Forward(latitude, longitude);
        var southern = TransverseMercatorProjection.Utm(56, north: false).Forward(latitude, longitude);

        Assert.True(Math.Abs(southern.X - northern.X) <= 1e-6d, "Eastings differ between north and south presets.");
        Assert.True(
            Math.Abs(southern.Y - northern.Y - 10000000d) <= 1e-6d,
            $"South-north northing difference {southern.Y - northern.Y} is not the 10000000 m offset.");
        var roundTrip = TransverseMercatorProjection.Utm(56, north: false).Inverse(southern.X, southern.Y);
        Assert.True(Math.Abs(roundTrip.LatitudeDegrees - latitude) <= Millimeter / MetersPerDegree);
        Assert.True(Math.Abs(roundTrip.LongitudeDegrees - longitude) <= Millimeter / MetersPerDegree);
    }

    [Fact]
    public void PolesProjectToMeridionalQuadrantAndRoundTrip()
    {
        var northPole = Plain.Forward(90d, 42d);
        var southPole = Plain.Forward(-90d, -17d);

        Assert.True(Math.Abs(northPole.X) <= 1e-6d, "North pole easting is not zero.");
        Assert.True(
            Math.Abs(northPole.Y - 10001965.7293d) <= 0.01d,
            $"North pole northing {northPole.Y} m differs from the meridional quadrant.");
        Assert.True(Math.Abs(southPole.Y + 10001965.7293d) <= 0.01d,
            $"South pole northing {southPole.Y} m differs from the negated meridional quadrant.");
        var roundTrip = Plain.Inverse(northPole.X, northPole.Y);
        Assert.True(Math.Abs(roundTrip.LatitudeDegrees - 90d) <= 1e-9d, $"Pole round-trip latitude {roundTrip.LatitudeDegrees}.");
    }

    [Fact]
    public void ForwardAcceptsLongitudesWrappedBeyond180()
    {
        var direct = Plain.Forward(30d, 2d);
        var wrapped = Plain.Forward(30d, 362d);

        Assert.True(Math.Abs(direct.X - wrapped.X) <= 1e-9d, "Wrapped easting differs.");
        Assert.True(Math.Abs(direct.Y - wrapped.Y) <= 1e-9d, "Wrapped northing differs.");
    }

    [Fact]
    public void ForwardRejectsEquatorBranchPointAtNinetyDegrees()
    {
        // The exact projection maps the equator branch point to infinite
        // easting; longitudes near 90 degrees diverge past the series domain.
        Assert.Throws<ArgumentException>(() => Plain.Forward(0d, 90d));
        Assert.Throws<ArgumentException>(() => Plain.Forward(0d, -90d));
        Assert.Throws<ArgumentException>(() => Plain.Forward(0d, 89.999999999999d));
        Assert.Throws<ArgumentException>(() => Plain.ScaleFactor(0d, -89.999999999999d));
    }

    [Fact]
    public void ForwardRejectsParametersWhoseResultsOverflow()
    {
        var hugeScale = new TransverseMercatorProjection(0d, 1e303d, 0d, 0d);

        Assert.Throws<ArgumentException>(() => hugeScale.Forward(45d, 0d));
        Assert.Throws<ArgumentException>(() => hugeScale.ScaleFactor(1d, 1d));

        // The origin projection itself stays representable for large k0.
        var largeScale = new TransverseMercatorProjection(0d, 1e301d, 0d, 0d);
        Assert.Equal(0d, largeScale.Forward(0d, 0d).X);
        Assert.Equal(0d, largeScale.Forward(0d, 0d).Y);
    }

    [Fact]
    public void ForwardRejectsNonFiniteCoordinates()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => Plain.Forward(bad, 0d));
            Assert.Throws<ArgumentException>(() => Plain.Forward(45d, bad));
            Assert.Throws<ArgumentException>(() => Plain.ScaleFactor(bad, 0d));
            Assert.Throws<ArgumentException>(() => Plain.ScaleFactor(45d, bad));
        }
    }

    [Fact]
    public void ForwardRejectsLatitudeOutsideValidRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Plain.Forward(90.0000001d, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plain.Forward(-90.0000001d, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plain.ScaleFactor(91d, 0d));
    }

    [Fact]
    public void InverseRejectsNonFiniteCoordinates()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => Plain.Inverse(bad, 0d));
            Assert.Throws<ArgumentException>(() => Plain.Inverse(0d, bad));
        }
    }

    [Fact]
    public void InverseRejectsCoordinatesOutsideInvertibleDomain()
    {
        // 1e12 m overflows the hyperbolic functions to nonfinite values, which
        // the inverse reports as out of domain; it never iterates unboundedly.
        Assert.Throws<ArgumentException>(() => Plain.Inverse(1e12d, 0d));
        Assert.Throws<ArgumentException>(() => Plain.Inverse(0d, 1e12d));
        Assert.Throws<ArgumentException>(() => Utm31North.Inverse(-1e12d, 0d));
    }

    [Fact]
    public void ConstructorRejectsInvalidParameters()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => new TransverseMercatorProjection(bad, 1d, 0d, 0d));
            Assert.Throws<ArgumentException>(() => new TransverseMercatorProjection(0d, bad, 0d, 0d));
            Assert.Throws<ArgumentException>(() => new TransverseMercatorProjection(0d, 1d, bad, 0d));
            Assert.Throws<ArgumentException>(() => new TransverseMercatorProjection(0d, 1d, 0d, bad));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new TransverseMercatorProjection(0d, 0d, 0d, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransverseMercatorProjection(0d, -1d, 0d, 0d));
    }

    [Fact]
    public void UtmRejectsZoneOutsideOneToSixty()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TransverseMercatorProjection.Utm(0, north: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransverseMercatorProjection.Utm(-5, north: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransverseMercatorProjection.Utm(61, north: false));
    }

    private static double WorstGridRoundTripErrorMeters(
        TransverseMercatorProjection projection,
        double centralMeridian)
    {
        var worst = 0d;
        for (var latStep = 0; latStep <= 41; latStep++)
        {
            var latitude = -80d + 4d * latStep;
            for (var lonStep = -5; lonStep <= 5; lonStep++)
            {
                var longitude = centralMeridian + lonStep;
                var error = RoundTripErrorMeters(projection, latitude, longitude);
                worst = Math.Max(worst, error);
            }
        }

        return worst;
    }

    private static double RoundTripErrorMeters(
        TransverseMercatorProjection projection,
        double latitude,
        double longitude)
    {
        var projected = projection.Forward(latitude, longitude);
        var roundTrip = projection.Inverse(projected.X, projected.Y);
        var deltaLatitude = roundTrip.LatitudeDegrees - latitude;
        var deltaLongitude = roundTrip.LongitudeDegrees - longitude;

        return Distance(
            deltaLatitude * MetersPerDegree,
            deltaLongitude * Math.Cos(latitude * Math.PI / 180d) * MetersPerDegree);
    }

    private static double Distance(double x, double y)
    {
        return Math.Sqrt(x * x + y * y);
    }
}
