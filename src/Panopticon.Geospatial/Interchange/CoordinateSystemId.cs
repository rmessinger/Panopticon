using System;
using System.Globalization;

namespace Panopticon.Geospatial.Interchange;

/// <summary>
/// Coordinate reference system identity: either an EPSG code or an inline WGS84 Transverse
/// Mercator definition (central meridian, scale, false easting, false northing).
/// <see cref="ToString"/> is stable across cultures and runs.
/// </summary>
public readonly record struct CoordinateSystemId
{
    public const int Wgs84Epsg = 4326;

    private const int NorthernUtmEpsgBase = 32600;

    private const int SouthernUtmEpsgBase = 32700;

    private CoordinateSystemId(
        int? epsgCode,
        double centralMeridianDegrees,
        double scale,
        double falseEasting,
        double falseNorthing)
    {
        EpsgCode = epsgCode;
        CentralMeridianDegrees = centralMeridianDegrees;
        Scale = scale;
        FalseEasting = falseEasting;
        FalseNorthing = falseNorthing;
    }

    /// <summary>EPSG code, or null when this id is an inline Transverse Mercator definition.</summary>
    public int? EpsgCode { get; }

    /// <summary>Central meridian in degrees (Transverse Mercator only).</summary>
    public double CentralMeridianDegrees { get; }

    /// <summary>Scale factor at the central meridian (Transverse Mercator only).</summary>
    public double Scale { get; }

    /// <summary>False easting in meters (Transverse Mercator only).</summary>
    public double FalseEasting { get; }

    /// <summary>False northing in meters (Transverse Mercator only).</summary>
    public double FalseNorthing { get; }

    public bool IsCustomTransverseMercator => EpsgCode is null;

    public static CoordinateSystemId Wgs84 { get; } = new(Wgs84Epsg, 0d, 0d, 0d, 0d);

    public static CoordinateSystemId FromEpsg(int epsgCode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(epsgCode, 1);

        return new CoordinateSystemId(epsgCode, 0d, 0d, 0d, 0d);
    }

    public static CoordinateSystemId TransverseMercator(
        double centralMeridianDegrees,
        double scale,
        double falseEasting,
        double falseNorthing)
    {
        ThrowIfNotFinite(centralMeridianDegrees, nameof(centralMeridianDegrees));
        ThrowIfNotFinite(scale, nameof(scale));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ThrowIfNotFinite(falseEasting, nameof(falseEasting));
        ThrowIfNotFinite(falseNorthing, nameof(falseNorthing));

        return new CoordinateSystemId(null, centralMeridianDegrees, scale, falseEasting, falseNorthing);
    }

    /// <summary>Returns the WGS84 UTM zone CRS: EPSG:32601..32660 north, EPSG:32701..32760 south.</summary>
    public static CoordinateSystemId Utm(int zone, bool northernHemisphere)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(zone, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zone, 60);

        return FromEpsg((northernHemisphere ? NorthernUtmEpsgBase : SouthernUtmEpsgBase) + zone);
    }

    public override string ToString()
    {
        if (EpsgCode is { } epsgCode)
        {
            return "EPSG:" + epsgCode.ToString(CultureInfo.InvariantCulture);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"TM(cm={CentralMeridianDegrees:R},k={Scale:R},fe={FalseEasting:R},fn={FalseNorthing:R})");
    }

    private static void ThrowIfNotFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite.");
        }
    }
}
