#nullable enable

using System;

namespace Panopticon.Content.Importing.Maps;

public static class GeneratedBuildingPicker
{
    public static GeneratedBuilding? Pick(BuildingLayerSet buildings, double x, double y)
    {
        foreach (var building in buildings.Buildings)
        {
            foreach (var part in building.Parts)
            {
                if (ContainsPoint(part.Footprint, x, y))
                {
                    return building;
                }
            }
        }

        return null;
    }

    private static bool ContainsPoint(VectorGeometry geometry, double x, double y)
    {
        if (geometry is VectorPolygonGeometry polygon)
        {
            return ContainsPolygon(polygon, x, y);
        }

        if (geometry is VectorMultiGeometry multi)
        {
            foreach (var child in multi.Geometries)
            {
                if (ContainsPoint(child, x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsPolygon(VectorPolygonGeometry polygon, double x, double y)
    {
        if (!RingContains(polygon.OuterRing, x, y))
        {
            return false;
        }

        foreach (var hole in polygon.InnerRings)
        {
            if (RingContains(hole, x, y))
            {
                return false;
            }
        }

        return true;
    }

    // Boundary-inclusive even-odd test: a point on any edge counts as inside the ring.
    private static bool RingContains(VectorRing ring, double x, double y)
    {
        var positions = ring.Positions;
        var previous = positions[positions.Count - 1];
        var inside = false;

        foreach (var current in positions)
        {
            if (IsOnSegment(previous, current, x, y))
            {
                return true;
            }

            if ((previous.Y > y) != (current.Y > y)
                && x < (current.X - previous.X) * (y - previous.Y) / (current.Y - previous.Y) + previous.X)
            {
                inside = !inside;
            }

            previous = current;
        }

        return inside;
    }

    private static bool IsOnSegment(VectorPosition a, VectorPosition b, double x, double y)
    {
        var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

        return cross == 0
            && x >= Math.Min(a.X, b.X) && x <= Math.Max(a.X, b.X)
            && y >= Math.Min(a.Y, b.Y) && y <= Math.Max(a.Y, b.Y);
    }
}
