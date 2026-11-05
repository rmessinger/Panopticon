using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>Shared constructor-time validation for payload content values.</summary>
internal static class PayloadValidationRules
{
    private const int Sha256HexLength = 64;

    private const int NorthernUtmEpsgBase = 32600;

    private const int SouthernUtmEpsgBase = 32700;

    /// <summary>Known vertical datum names, compared case-insensitively after trimming.</summary>
    private static readonly HashSet<string> KnownVerticalDatumKeys = new(StringComparer.Ordinal)
    {
        "egm96",
        "egm2008",
    };

    /// <summary>
    /// Requires a normalized relative path: forward slashes only, no rooted or
    /// machine-qualified form, and no traversal or current-directory segments.
    /// </summary>
    public static void NormalizedRelativePath(string path, string paramName)
    {
        if (path.Length == 0 || path.Contains('\0'))
        {
            throw new ArgumentException("The path must be a non-empty relative path.", paramName);
        }

        if (path.Contains('\\') || PathIsRooted(path))
        {
            throw new ArgumentException($"The path '{path}' must be a relative path with '/' separators.", paramName);
        }

        if (path.StartsWith("/", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"The path '{path}' must not start or end with '/'.", paramName);
        }

        var segmentStart = 0;
        for (var index = 0; index <= path.Length; index++)
        {
            if (index != path.Length && path[index] != '/')
            {
                continue;
            }

            var length = index - segmentStart;
            if (length == 0)
            {
                throw new ArgumentException($"The path '{path}' contains an empty segment.", paramName);
            }

            var segment = path.Substring(segmentStart, length);
            if (segment is "." or "..")
            {
                throw new ArgumentException($"The path '{path}' must not contain '{segment}' segments.", paramName);
            }

            segmentStart = index + 1;
        }
    }

    public static void Sha256Hex(string value, string paramName)
    {
        if (value.Length != Sha256HexLength || !value.All(IsLowerCaseHexDigit))
        {
            throw new ArgumentException("SHA-256 must be 64 lowercase hexadecimal characters.", paramName);
        }
    }

    /// <summary>Requires an EPSG code or a complete finite Transverse Mercator definition.</summary>
    public static void CoordinateSystem(CoordinateSystemId value, string paramName)
    {
        if (value.EpsgCode is { } epsgCode)
        {
            if (epsgCode < 1)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "The EPSG code must be positive.");
            }

            return;
        }

        if (!double.IsFinite(value.Scale) || value.Scale <= 0d
            || !double.IsFinite(value.CentralMeridianDegrees)
            || !double.IsFinite(value.FalseEasting)
            || !double.IsFinite(value.FalseNorthing))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                "A Transverse Mercator coordinate system needs a positive finite scale and finite parameters.");
        }
    }

    /// <summary>Requires a supported source CRS: EPSG:4326, a WGS84 UTM system, or inline Transverse Mercator.</summary>
    public static void SourceCoordinateSystem(CoordinateSystemId value, string paramName)
    {
        if (IsWgs84(value) || IsWgs84Utm(value))
        {
            return;
        }

        if (value.EpsgCode is null)
        {
            CoordinateSystem(value, paramName);
            return;
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            value,
            "Source CRS must be EPSG:4326, a WGS84 UTM system, or an inline Transverse Mercator definition.");
    }

    /// <summary>Requires a projected game-grid CRS: a WGS84 UTM system or inline Transverse Mercator.</summary>
    public static void ProjectedGameGridCoordinateSystem(CoordinateSystemId value, string paramName)
    {
        if (IsWgs84Utm(value))
        {
            return;
        }

        if (value.EpsgCode is null)
        {
            CoordinateSystem(value, paramName);
            return;
        }

        if (IsWgs84(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                "EPSG:4326 is geographic; the projected grid CRS must be a WGS84 UTM system or Transverse Mercator.");
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            value,
            "The projected grid CRS must be a WGS84 UTM system or an inline Transverse Mercator definition.");
    }

    /// <summary>
    /// Requires a closed ring of at least three distinct finite corners with non-zero
    /// area; altitudes must be finite when present.
    /// </summary>
    public static void RingPositions(IReadOnlyList<GeoPosition> positions, string role, string paramName)
    {
        if (positions.Count < 4)
        {
            throw new ArgumentException($"{role} must hold at least four stored positions: three distinct corners plus the closing repeat.", paramName);
        }

        foreach (var position in positions)
        {
            if (!double.IsFinite(position.Coordinate.X) || !double.IsFinite(position.Coordinate.Y)
                || (position.AltitudeMeters is { } altitude && !double.IsFinite(altitude)))
            {
                throw new ArgumentException($"{role} contains a coordinate or altitude that is not finite.", paramName);
            }
        }

        var first = positions[0].Coordinate;
        var last = positions[^1].Coordinate;
        if (first.X != last.X || first.Y != last.Y)
        {
            throw new ArgumentException($"{role} must be closed: its first and last positions must coincide.", paramName);
        }

        var distinct = new HashSet<(double X, double Y)>();
        foreach (var position in positions)
        {
            distinct.Add((position.Coordinate.X, position.Coordinate.Y));
        }

        if (distinct.Count < 3)
        {
            throw new ArgumentException($"{role} must have at least three distinct positions.", paramName);
        }

        if (SignedTwiceArea(positions) == 0d)
        {
            throw new ArgumentException($"{role} has zero area.", paramName);
        }
    }

    /// <summary>Canonical key of a known datum; null for blank, 'unknown', or unrecognized values.</summary>
    public static string? TryKnownDatumKey(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();

        return KnownVerticalDatumKeys.Contains(trimmed) ? trimmed : null;
    }

    /// <summary>True when both values are known datums that differ; no geoid conversion exists.</summary>
    public static bool IsKnownDatumConflict(string declared, string? embedded)
    {
        if (embedded is null)
        {
            return false;
        }

        var declaredKey = TryKnownDatumKey(declared);
        var embeddedKey = TryKnownDatumKey(embedded);

        return declaredKey is not null && embeddedKey is not null
            && !string.Equals(declaredKey, embeddedKey, StringComparison.Ordinal);
    }

    private static bool IsWgs84(CoordinateSystemId value)
    {
        return value.EpsgCode == CoordinateSystemId.Wgs84Epsg;
    }

    private static bool IsWgs84Utm(CoordinateSystemId value)
    {
        if (value.EpsgCode is not { } epsgCode)
        {
            return false;
        }

        return (epsgCode > NorthernUtmEpsgBase && epsgCode <= NorthernUtmEpsgBase + 60)
            || (epsgCode > SouthernUtmEpsgBase && epsgCode <= SouthernUtmEpsgBase + 60);
    }

    // Translating avoids cancellation at large origins.
    private static double SignedTwiceArea(IReadOnlyList<GeoPosition> positions)
    {
        var origin = positions[0].Coordinate;
        var twiceArea = 0d;
        for (var index = 1; index < positions.Count - 1; index++)
        {
            var ax = positions[index].Coordinate.X - origin.X;
            var ay = positions[index].Coordinate.Y - origin.Y;
            var bx = positions[index + 1].Coordinate.X - origin.X;
            var by = positions[index + 1].Coordinate.Y - origin.Y;
            twiceArea += (ax * by) - (bx * ay);
        }

        return twiceArea;
    }

    private static bool PathIsRooted(string path)
    {
        return path.StartsWith("/", StringComparison.Ordinal)
            || (path.Length > 1 && path[1] == ':')
            || path.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsLowerCaseHexDigit(char character)
    {
        return character is >= '0' and <= '9' or >= 'a' and <= 'f';
    }
}
