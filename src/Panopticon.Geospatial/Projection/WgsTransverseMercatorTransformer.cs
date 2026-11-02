using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Projection;

/// <summary>
/// Transforms supported WGS84 geographic, UTM, and custom Transverse Mercator coordinates.
/// </summary>
public sealed class WgsTransverseMercatorTransformer : ICoordinateTransformer
{
    private const int NorthernUtmEpsgBase = 32600;

    private const int SouthernUtmEpsgBase = 32700;

    private readonly ConcurrentDictionary<CoordinateSystemId, TransverseMercatorProjection> _projectionCache = new();

    /// <summary>
    /// Returns whether <paramref name="id"/> is EPSG:4326, a WGS84 UTM zone, or a
    /// Transverse Mercator definition the id factory accepts. The default id and
    /// unsupported EPSG codes are rejected even when source equals target.
    /// </summary>
    public bool IsSupported(CoordinateSystemId id)
    {
        if (id.EpsgCode is { } epsgCode)
        {
            return epsgCode == CoordinateSystemId.Wgs84Epsg || IsUtmEpsg(epsgCode);
        }

        // The default id carries scale 0, which no valid custom definition uses.
        return double.IsFinite(id.Scale)
            && id.Scale > 0d
            && double.IsFinite(id.CentralMeridianDegrees)
            && double.IsFinite(id.FalseEasting)
            && double.IsFinite(id.FalseNorthing);
    }

    /// <inheritdoc />
    public Vector2d Transform(Vector2d point, CoordinateSystemId source, CoordinateSystemId target)
    {
        ThrowIfUnsupported(source);
        ThrowIfUnsupported(target);

        var geographic = ToGeographic(point, source);

        return FromGeographic(geographic, target);
    }

    /// <summary>
    /// Transforms <paramref name="point"/> without throwing: the failures that
    /// <see cref="Transform"/> reports as exceptions come back as a
    /// <see cref="GeoStage.Coordinates"/> diagnostic with a stable message.
    /// </summary>
    public bool TryTransform(
        Vector2d point,
        CoordinateSystemId source,
        CoordinateSystemId target,
        out Vector2d result,
        [NotNullWhen(false)] out GeoDiagnostic? diagnostic)
    {
        if (!IsSupported(source))
        {
            return Fail(
                GeoDiagnosticCodes.CoordinatesUnsupportedCrs,
                UnsupportedMessage(source),
                out result,
                out diagnostic);
        }

        if (!IsSupported(target))
        {
            return Fail(
                GeoDiagnosticCodes.CoordinatesUnsupportedCrs,
                UnsupportedMessage(target),
                out result,
                out diagnostic);
        }

        try
        {
            var geographic = ToGeographic(point, source);
            var transformed = FromGeographic(geographic, target);
            result = transformed;
            diagnostic = null;

            return true;
        }
        catch (ArgumentException ex)
        {
            return Fail(
                GeoDiagnosticCodes.CoordinatesFailure,
                $"Coordinate transform from {source} to {target} failed: coordinates are not finite or outside the transformable domain.",
                out result,
                out diagnostic,
                ex);
        }
    }

    private static bool IsUtmEpsg(int epsgCode)
    {
        return (epsgCode > NorthernUtmEpsgBase && epsgCode <= NorthernUtmEpsgBase + 60)
            || (epsgCode > SouthernUtmEpsgBase && epsgCode <= SouthernUtmEpsgBase + 60);
    }

    private static bool IsWgs84(CoordinateSystemId id)
    {
        return id.EpsgCode == CoordinateSystemId.Wgs84Epsg;
    }

    private static string UnsupportedMessage(CoordinateSystemId id)
    {
        return $"Coordinate system is not supported: {id}.";
    }

    private static bool Fail(
        string code,
        string message,
        out Vector2d result,
        [NotNullWhen(false)] out GeoDiagnostic? diagnostic,
        Exception? detail = null)
    {
        result = default;
        diagnostic = new GeoDiagnostic(code, GeoStage.Coordinates, GeoSeverity.Error, message, detail: detail);

        return false;
    }

    private void ThrowIfUnsupported(CoordinateSystemId id)
    {
        if (!IsSupported(id))
        {
            throw new NotSupportedException(UnsupportedMessage(id));
        }
    }

    private GeoPoint ToGeographic(Vector2d point, CoordinateSystemId source)
    {
        if (IsWgs84(source))
        {
            ValidateGeographic(point);

            return GeoPoint.FromVector2d(point);
        }

        return ProjectionFor(source).Inverse(point.X, point.Y);
    }

    private Vector2d FromGeographic(GeoPoint geographic, CoordinateSystemId target)
    {
        if (IsWgs84(target))
        {
            return geographic.ToVector2d();
        }

        var projected = ProjectionFor(target).Forward(geographic.LatitudeDegrees, geographic.LongitudeDegrees);
        if (!double.IsFinite(projected.X) || !double.IsFinite(projected.Y))
        {
            throw new ArgumentException("Projected coordinates are not finite.");
        }

        return projected;
    }

    private static void ValidateGeographic(Vector2d point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentException("Geographic coordinates must be finite degrees.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(point.Y, -90d);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(point.Y, 90d);
    }

    private TransverseMercatorProjection ProjectionFor(CoordinateSystemId id)
    {
        // Duplicate constructions under contention are safe because projections are immutable.
        return _projectionCache.GetOrAdd(id, CreateProjection);
    }

    private static TransverseMercatorProjection CreateProjection(CoordinateSystemId id)
    {
        if (id.EpsgCode is { } epsgCode)
        {
            var north = epsgCode <= NorthernUtmEpsgBase + 60;

            return TransverseMercatorProjection.Utm(epsgCode - (north ? NorthernUtmEpsgBase : SouthernUtmEpsgBase), north);
        }

        return new TransverseMercatorProjection(
            id.CentralMeridianDegrees,
            id.Scale,
            id.FalseEasting,
            id.FalseNorthing);
    }
}
