using System;
using System.Numerics;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Projection;

/// <summary>
/// WGS84 Transverse Mercator projection using the order-6 Krueger series
/// (Karney, C. F. F. (2011), "Transverse Mercator with an accuracy of a few
/// nanometers", J. Geodesy 85, 445-455, eqs. 35-36). The Gauss-Schreiber
/// folding, Clenshaw series summation, integer Horner coefficient
/// polynomials, and Newton iteration for the conformal latitude follow the
/// MIT-licensed GeographicLib TransverseMercator.cpp by Charles Karney
/// (2008-2023), https://geographiclib.sourceforge.io/; the full copyright and
/// license notice is retained in GeographicLib.LICENSE.txt in this directory.
/// Series accuracy is about 5 nm within the UTM longitude range; precision
/// degrades as longitude approaches 90 degrees from the central meridian.
/// </summary>
public sealed class TransverseMercatorProjection
{
    private const int SeriesOrder = 6;
    private const double EquatorialRadius = 6378137d;
    private const double Flattening = 1d / 298.257223563;
    private const double DegreesPerRadian = 180d / Math.PI;
    private const double RadiansPerDegree = Math.PI / 180d;
    private const int MaxConformalIterations = 8;
    private const double ConformalConvergence = 1e-15d;
    // Beyond asinh(double.MaxValue)/2 in normalized coordinates the hyperbolic
    // series terms overflow; ~2.3e9 m from the false origin.
    private const double SeriesDomainLimit = 355d;

    // Horner coefficient polynomials in n (highest power first); the final
    // entry of each group is the divisor. Transcribed from GeographicLib
    // TransverseMercator.cpp, GEOGRAPHICLIB_TRANSVERSEMERCATOR_ORDER = 6.
    private static readonly double[] RectifyingRadiusPolynomial = [1d, 4d, 64d, 256d];

    private static readonly double[][] AlphaPolynomials =
    [
        [31564d, -66675d, 34440d, 47250d, -100800d, 75600d, 151200d],
        [-1983433d, 863232d, 748608d, -1161216d, 524160d, 1935360d],
        [670412d, 406647d, -533952d, 184464d, 725760d],
        [6601661d, -7732800d, 2230245d, 7257600d],
        [-13675556d, 3438171d, 7983360d],
        [212378941d, 319334400d],
    ];

    private static readonly double[][] BetaPolynomials =
    [
        [384796d, -382725d, -6720d, 932400d, -1612800d, 1209600d, 2419200d],
        [-1118711d, 1695744d, -1174656d, 258048d, 80640d, 3870720d],
        [22276d, -16929d, -15984d, 12852d, 362880d],
        [-830251d, -158400d, 197865d, 7257600d],
        [-435388d, 453717d, 15966720d],
        [20648693d, 638668800d],
    ];

    private readonly double _e2;
    private readonly double _e2m;
    private readonly double _eccentricity;
    private readonly double _poleScale;
    private readonly double _b1;
    private readonly double _a1;
    private readonly double[] _alpha = new double[SeriesOrder + 1];
    private readonly double[] _beta = new double[SeriesOrder + 1];

    /// <summary>Creates a projection with fixed WGS84 ellipsoid parameters.</summary>
    /// <param name="centralMeridianDegrees">Longitude of the central meridian in degrees.</param>
    /// <param name="k0">Scale factor on the central meridian; must be positive.</param>
    /// <param name="falseEasting">Easting added at the origin, in meters.</param>
    /// <param name="falseNorthing">Northing added at the origin, in meters.</param>
    public TransverseMercatorProjection(double centralMeridianDegrees, double k0, double falseEasting, double falseNorthing)
    {
        if (!double.IsFinite(centralMeridianDegrees) || !double.IsFinite(k0)
            || !double.IsFinite(falseEasting) || !double.IsFinite(falseNorthing))
        {
            throw new ArgumentException("Projection parameters must be finite.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k0);
        CentralMeridianDegrees = centralMeridianDegrees;
        K0 = k0;
        FalseEasting = falseEasting;
        FalseNorthing = falseNorthing;

        _e2 = Flattening * (2d - Flattening);
        _e2m = 1d - _e2;
        _eccentricity = Math.Sqrt(_e2);
        _poleScale = Math.Sqrt(_e2m) * Math.Exp(_eccentricity * Math.Atanh(_eccentricity));

        var n = Flattening / (2d - Flattening);
        _b1 = PolyVal(RectifyingRadiusPolynomial, n * n) / (256d * (1d + n));
        _a1 = EquatorialRadius * _b1;

        var power = n;
        for (var index = 0; index < SeriesOrder; index++)
        {
            _alpha[index + 1] = power * PolyVal(AlphaPolynomials[index][..^1], n) / AlphaPolynomials[index][^1];
            _beta[index + 1] = power * PolyVal(BetaPolynomials[index][..^1], n) / BetaPolynomials[index][^1];
            power *= n;
        }
    }

    /// <summary>Longitude of the central meridian in degrees.</summary>
    public double CentralMeridianDegrees { get; }

    /// <summary>Scale factor on the central meridian.</summary>
    public double K0 { get; }

    /// <summary>False easting in meters.</summary>
    public double FalseEasting { get; }

    /// <summary>False northing in meters.</summary>
    public double FalseNorthing { get; }

    /// <summary>Creates the UTM preset for <paramref name="zone"/>: central meridian 6*zone-183, k0 0.9996, false easting 500000, false northing 0 (north) or 10000000 (south).</summary>
    public static TransverseMercatorProjection Utm(int zone, bool north)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(zone, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zone, 60);

        return new TransverseMercatorProjection(6d * zone - 183d, 0.9996d, 500000d, north ? 0d : 10000000d);
    }

    /// <summary>Projects a geographic point. Returns X = easting and Y = northing in meters.</summary>
    public Vector2d Forward(double latitudeDegrees, double longitudeDegrees)
    {
        var (easting, northing, _) = ComputeForward(latitudeDegrees, longitudeDegrees);

        return new Vector2d(easting, northing);
    }

    /// <summary>Returns the analytic point scale factor k of the projection at a geographic point.</summary>
    public double ScaleFactor(double latitudeDegrees, double longitudeDegrees)
    {
        var (_, _, scale) = ComputeForward(latitudeDegrees, longitudeDegrees);

        return scale;
    }

    /// <summary>Inverts projected coordinates in meters back to a WGS84 geographic point.</summary>
    public GeoPoint Inverse(double easting, double northing)
    {
        if (!double.IsFinite(easting) || !double.IsFinite(northing))
        {
            throw new ArgumentException("Projected coordinates must be finite.");
        }

        var xi = (northing - FalseNorthing) / (_a1 * K0);
        var eta = (easting - FalseEasting) / (_a1 * K0);
        var xiSign = double.IsNegative(xi) ? -1 : 1;
        var etaSign = double.IsNegative(eta) ? -1 : 1;
        xi = Math.Abs(xi);
        eta = Math.Abs(eta);

        if (xi > SeriesDomainLimit || eta > SeriesDomainLimit)
        {
            throw new ArgumentException("Projected coordinates are outside the invertible domain.");
        }

        var backside = xi > Math.PI / 2d;

        if (backside)
        {
            xi = Math.PI - xi;
        }

        var (xiPrime, etaPrime, _) = ApplySeries(xi, eta, _beta, invert: true);
        var sinhEta = Math.Sinh(etaPrime);
        var cosXiPrime = Math.Max(0d, Math.Cos(xiPrime));
        var radius = Hypot(sinhEta, cosXiPrime);

        double latitude;
        double longitude;
        if (radius != 0d)
        {
            latitude = Math.Atan(SolveConformalTangent(Math.Sin(xiPrime) / radius)) * DegreesPerRadian;
            longitude = Math.Atan2(sinhEta, cosXiPrime) * DegreesPerRadian;
        }
        else
        {
            latitude = 90d;
            longitude = 0d;
        }

        latitude *= xiSign;

        if (backside)
        {
            longitude = 180d - longitude;
        }

        longitude *= etaSign;
        longitude = NormalizeLongitude(longitude + CentralMeridianDegrees);

        if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
        {
            throw new ArgumentException("Projected coordinates are outside the invertible domain.");
        }

        return new GeoPoint(latitude, longitude);
    }

    private (double Easting, double Northing, double Scale) ComputeForward(double latitudeDegrees, double longitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) || !double.IsFinite(longitudeDegrees))
        {
            throw new ArgumentException("Geographic coordinates must be finite degrees.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(latitudeDegrees, -90d);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(latitudeDegrees, 90d);

        var latSign = double.IsNegative(latitudeDegrees) ? -1 : 1;
        var latitude = latitudeDegrees * latSign;
        var longitudeOffset = NormalizeLongitude(longitudeDegrees - CentralMeridianDegrees);
        var lonSign = double.IsNegative(longitudeOffset) ? -1 : 1;
        var longitude = Math.Abs(longitudeOffset);
        var backside = longitude > 90d;

        if (backside)
        {
            if (latitude == 0d)
            {
                latSign = -latSign;
            }

            longitude = 180d - longitude;
        }

        var phi = latitude * RadiansPerDegree;
        var lam = longitude * RadiansPerDegree;
        var sinPhi = Math.Sin(phi);
        var cosPhi = Math.Cos(phi);
        var sinLam = Math.Sin(lam);
        var cosLam = Math.Cos(lam);

        double xiPrime;
        double etaPrime;
        double gaussSchreiberScale;
        if (latitude != 90d)
        {
            var tau = sinPhi / cosPhi;
            var tauPrime = ConformalTangent(tau);
            var denominator = Hypot(tauPrime, cosLam);
            xiPrime = Math.Atan2(tauPrime, cosLam);
            etaPrime = Math.Asinh(sinLam / denominator);
            gaussSchreiberScale = Math.Sqrt(_e2m + _e2 * cosPhi * cosPhi) * Hypot(1d, tau) / denominator;
        }
        else
        {
            xiPrime = Math.PI / 2d;
            etaPrime = 0d;
            gaussSchreiberScale = _poleScale;
        }

        var (xi, eta, seriesScale) = ApplySeries(xiPrime, etaPrime, _alpha, invert: false);
        var easting = FalseEasting + _a1 * K0 * eta * lonSign;
        var northing = FalseNorthing + _a1 * K0 * (backside ? Math.PI - xi : xi) * latSign;
        var scale = K0 * gaussSchreiberScale * seriesScale;

        // The equator branch point at 90 degrees from the central meridian
        // pushes the normalized easting past the series domain, and extreme
        // k0 or false-origin values overflow the meter results.
        if (Math.Abs(eta) > SeriesDomainLimit || !double.IsFinite(easting) || !double.IsFinite(northing)
            || !double.IsFinite(scale) || scale <= 0d)
        {
            throw new ArgumentException("Projection result is not finite or the point is outside the projectable domain.");
        }

        return (easting, northing, scale);
    }

    // Applies zeta = zeta' + sum(c[j] * sin(2*j*zeta'), j = 1..6) via Clenshaw
    // (or the reverted series zeta' = zeta - sum(c[j] * sin(2*j*zeta)) when
    // invert), plus the series derivative whose magnitude times b1 folds the
    // Gauss-Schreiber scale into the Gauss-Krueger scale.
    private (double Xi, double Eta, double ScaleCorrection) ApplySeries(double xi, double eta, double[] coefficients, bool invert)
    {
        var cos2Xi = Math.Cos(2d * xi);
        var cosh2Eta = Math.Cosh(2d * eta);
        var sin2Xi = Math.Sin(2d * xi);
        var sinh2Eta = Math.Sinh(2d * eta);
        var sign = invert ? -1d : 1d;
        Complex twoCos2Zeta = new(2d * cos2Xi * cosh2Eta, -2d * sin2Xi * sinh2Eta);
        Complex sum = Complex.Zero;
        Complex previous = Complex.Zero;
        Complex derivativeSum = Complex.Zero;
        Complex derivativePrevious = Complex.Zero;

        for (var j = SeriesOrder; j > 0; j -= 2)
        {
            sum = twoCos2Zeta * previous - sum + sign * coefficients[j];
            derivativeSum = twoCos2Zeta * derivativePrevious - derivativeSum + sign * 2d * j * coefficients[j];
            previous = twoCos2Zeta * sum - previous + sign * coefficients[j - 1];
            derivativePrevious = twoCos2Zeta * derivativeSum - derivativePrevious + sign * 2d * (j - 1) * coefficients[j - 1];
        }

        Complex cos2Zeta = twoCos2Zeta / 2d;
        Complex derivative = 1d - derivativeSum + cos2Zeta * derivativePrevious;
        Complex sin2Zeta = new(sin2Xi * cosh2Eta, cos2Xi * sinh2Eta);
        Complex zeta = new Complex(xi, eta) + sin2Zeta * previous;

        return (zeta.Real, zeta.Imaginary, _b1 * Complex.Abs(derivative));
    }

    // tan of the conformal latitude from tan of the geodetic latitude;
    // GeographicLib Math::taupf.
    private double ConformalTangent(double tau)
    {
        var tau1 = Hypot(1d, tau);
        var sig = Math.Sinh(_eccentricity * Math.Atanh(_eccentricity * tau / tau1));

        return Hypot(1d, sig) * tau - sig * tau1;
    }

    // Solves ConformalTangent(tau) = tauPrime for tau by Newton's method with
    // the analytic derivative; iterations are capped so the solve always ends.
    private double SolveConformalTangent(double tauPrime)
    {
        var tau = tauPrime / _e2m;
        for (var iteration = 0; iteration < MaxConformalIterations; iteration++)
        {
            var tau1 = Hypot(1d, tau);
            var sig = Math.Sinh(_eccentricity * Math.Atanh(_eccentricity * tau / tau1));
            var estimate = Hypot(1d, sig) * tau - sig * tau1;

            if (Math.Abs(estimate - tauPrime) <= ConformalConvergence * Math.Max(1d, Math.Abs(tauPrime)))
            {
                break;
            }

            var derivative = Hypot(1d, estimate) * _e2m * tau1 / (1d + _e2m * tau * tau);
            tau += (tauPrime - estimate) / derivative;
        }

        return tau;
    }

    private static double NormalizeLongitude(double degrees)
    {
        var normalized = (degrees + 180d) % 360d;

        if (normalized < 0d)
        {
            normalized += 360d;
        }

        return normalized - 180d;
    }

    // Overflow-safe sqrt(x*x + y*y); .NET has no Math.Hypot.
    private static double Hypot(double x, double y)
    {
        var larger = Math.Max(Math.Abs(x), Math.Abs(y));
        var smaller = Math.Min(Math.Abs(x), Math.Abs(y));

        if (larger == 0d || double.IsInfinity(larger))
        {
            return larger;
        }

        var ratio = smaller / larger;

        return larger * Math.Sqrt(1d + ratio * ratio);
    }

    private static double PolyVal(double[] coefficientsDescending, double x)
    {
        var y = 0d;
        for (var i = 0; i < coefficientsDescending.Length; i++)
        {
            y = y * x + coefficientsDescending[i];
        }

        return y;
    }
}
