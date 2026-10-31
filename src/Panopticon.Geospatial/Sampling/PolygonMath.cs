using System;
using System.Collections.Generic;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Sampling;

/// <summary>Convex-polygon helpers for footprint-versus-cell overlap in source pixel space.</summary>
internal static class PolygonMath
{
    /// <summary>Convex hull (Andrew monotone chain, counter-clockwise, collinear points dropped).</summary>
    public static IReadOnlyList<Vector2d> ConvexHull(IReadOnlyList<Vector2d> points)
    {
        var sorted = new List<Vector2d>(points);

        sorted.Sort(static (left, right) => left.X != right.X
            ? left.X.CompareTo(right.X)
            : left.Y.CompareTo(right.Y));

        if (sorted.Count <= 2)
        {
            return sorted;
        }

        var lower = new List<Vector2d>();

        foreach (var point in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0d)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(point);
        }

        var upper = new List<Vector2d>();

        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var point = sorted[i];

            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0d)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);

        return lower;
    }

    /// <summary>
    /// Absolute polygon area via a triangle fan around the first vertex. Vertices are
    /// differenced against that vertex before multiplying, so tiny polygons far from the
    /// coordinate origin keep their precision instead of cancelling large shoelace products.
    /// </summary>
    public static double PolygonArea(IReadOnlyList<Vector2d> polygon)
    {
        if (polygon.Count < 3)
        {
            return 0d;
        }

        var origin = polygon[0];
        double doubledArea = 0d;

        for (var i = 1; i < polygon.Count - 1; i++)
        {
            var first = polygon[i];
            var second = polygon[i + 1];
            doubledArea += ((first.X - origin.X) * (second.Y - origin.Y))
                - ((first.Y - origin.Y) * (second.X - origin.X));
        }

        return Math.Abs(doubledArea) / 2d;
    }

    /// <summary>Area of a convex polygon clipped against an axis-aligned rectangle (Sutherland-Hodgman).</summary>
    public static double ClippedArea(
        IReadOnlyList<Vector2d> convexPolygon,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        if (convexPolygon.Count < 3)
        {
            return 0d;
        }

        var clipped = ClipHalfPlane(
            convexPolygon,
            point => point.X >= minX,
            (a, b) =>
            {
                var t = (minX - a.X) / (b.X - a.X);

                return new Vector2d(minX, a.Y + t * (b.Y - a.Y));
            });
        clipped = ClipHalfPlane(
            clipped,
            point => point.X <= maxX,
            (a, b) =>
            {
                var t = (maxX - a.X) / (b.X - a.X);

                return new Vector2d(maxX, a.Y + t * (b.Y - a.Y));
            });
        clipped = ClipHalfPlane(
            clipped,
            point => point.Y >= minY,
            (a, b) =>
            {
                var t = (minY - a.Y) / (b.Y - a.Y);

                return new Vector2d(a.X + t * (b.X - a.X), minY);
            });
        clipped = ClipHalfPlane(
            clipped,
            point => point.Y <= maxY,
            (a, b) =>
            {
                var t = (maxY - a.Y) / (b.Y - a.Y);

                return new Vector2d(a.X + t * (b.X - a.X), maxY);
            });

        return PolygonArea(clipped);
    }

    private static List<Vector2d> ClipHalfPlane(
        IReadOnlyList<Vector2d> polygon,
        Func<Vector2d, bool> isInside,
        Func<Vector2d, Vector2d, Vector2d> intersect)
    {
        var result = new List<Vector2d>(polygon.Count + 1);

        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            var currentInside = isInside(current);
            var nextInside = isInside(next);

            if (currentInside)
            {
                result.Add(current);
            }

            if (currentInside != nextInside)
            {
                result.Add(intersect(current, next));
            }
        }

        return result;
    }

    private static double Cross(Vector2d origin, Vector2d first, Vector2d second)
    {
        return (first.X - origin.X) * (second.Y - origin.Y)
            - (first.Y - origin.Y) * (second.X - origin.X);
    }
}
