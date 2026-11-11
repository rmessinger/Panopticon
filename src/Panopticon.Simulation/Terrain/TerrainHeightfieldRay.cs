using System;
using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Terrain;

/// <summary>
/// Provides ray intersection traversal over a heightfield lattice using grid DDA (Amanatides-Woo).
/// Traverses lattice cells in ray order in O(W + H) steps.
/// Picking evaluates the high-resolution source-ground intersection consistent with normal (small) maps,
/// rather than the CPU-morphed/stitched LOD render surface.
/// </summary>
public static class TerrainHeightfieldRay
{
    /// <summary>
    /// Intersects a ray against the heightfield lattice, returning the closest world-space hit point,
    /// or null if the ray misses the heightfield.
    /// </summary>
    public static Vector3? Intersect(HeightMap heightMap, Ray ray)
    {
        if (heightMap == null)
        {
            return null;
        }

        var numCellsX = heightMap.Width - 1;
        var numCellsY = heightMap.Height - 1;
        if (numCellsX <= 0 || numCellsY <= 0)
        {
            return null;
        }

        var p = ray.Position;
        var d = ray.Direction;

        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) ||
            !float.IsFinite(d.X) || !float.IsFinite(d.Y) || !float.IsFinite(d.Z))
        {
            return null;
        }

        if (d.LengthSquared() < 1e-12f)
        {
            return null;
        }

        const float cellSize = HeightMap.HorizontalScale;
        var origin00 = heightMap.GetPosition(0, 0);
        var minX = origin00.X;
        var minY = origin00.Y;
        var maxX = minX + (numCellsX * cellSize);
        var maxY = minY + (numCellsY * cellSize);

        if (heightMap.MaxZ > heightMap.MinZ)
        {
            if (p.Z > heightMap.MaxZ && d.Z >= 0f)
            {
                return null;
            }

            if (p.Z < heightMap.MinZ && d.Z <= 0f)
            {
                return null;
            }
        }

        float tMinX;
        float tMaxX;
        if (MathF.Abs(d.X) > 1e-9f)
        {
            var invDx = 1.0f / d.X;
            var t1 = (minX - p.X) * invDx;
            var t2 = (maxX - p.X) * invDx;
            tMinX = MathF.Min(t1, t2);
            tMaxX = MathF.Max(t1, t2);
        }
        else
        {
            if (p.X < minX || p.X > maxX)
            {
                return null;
            }

            tMinX = float.NegativeInfinity;
            tMaxX = float.PositiveInfinity;
        }

        float tMinY;
        float tMaxY;
        if (MathF.Abs(d.Y) > 1e-9f)
        {
            var invDy = 1.0f / d.Y;
            var t1 = (minY - p.Y) * invDy;
            var t2 = (maxY - p.Y) * invDy;
            tMinY = MathF.Min(t1, t2);
            tMaxY = MathF.Max(t1, t2);
        }
        else
        {
            if (p.Y < minY || p.Y > maxY)
            {
                return null;
            }

            tMinY = float.NegativeInfinity;
            tMaxY = float.PositiveInfinity;
        }

        var tEnter = MathF.Max(tMinX, tMinY);
        var tExit = MathF.Min(tMaxX, tMaxY);

        if (tEnter > tExit || tExit < 0f)
        {
            return null;
        }

        var horizontalLenSq = (d.X * d.X) + (d.Y * d.Y);
        if (horizontalLenSq < 1e-12f)
        {
            return IntersectVertical(heightMap, in ray, minX, minY, numCellsX, numCellsY, cellSize);
        }

        var tStart = MathF.Max(0f, tEnter);
        var startX = p.X + (tStart * d.X);
        var startY = p.Y + (tStart * d.Y);

        var cx = (int)MathF.Floor((startX - minX) / cellSize);
        var cy = (int)MathF.Floor((startY - minY) / cellSize);

        cx = Math.Clamp(cx, 0, numCellsX - 1);
        cy = Math.Clamp(cy, 0, numCellsY - 1);

        int stepX;
        float tNextX;
        float tDeltaX;

        if (d.X > 1e-9f)
        {
            stepX = 1;
            var cellBoundaryX = minX + ((cx + 1) * cellSize);
            tNextX = (cellBoundaryX - p.X) / d.X;
            tDeltaX = cellSize / d.X;
        }
        else if (d.X < -1e-9f)
        {
            stepX = -1;
            var cellBoundaryX = minX + (cx * cellSize);
            tNextX = (cellBoundaryX - p.X) / d.X;
            tDeltaX = -cellSize / d.X;
        }
        else
        {
            stepX = 0;
            tNextX = float.PositiveInfinity;
            tDeltaX = float.PositiveInfinity;
        }

        int stepY;
        float tNextY;
        float tDeltaY;

        if (d.Y > 1e-9f)
        {
            stepY = 1;
            var cellBoundaryY = minY + ((cy + 1) * cellSize);
            tNextY = (cellBoundaryY - p.Y) / d.Y;
            tDeltaY = cellSize / d.Y;
        }
        else if (d.Y < -1e-9f)
        {
            stepY = -1;
            var cellBoundaryY = minY + (cy * cellSize);
            tNextY = (cellBoundaryY - p.Y) / d.Y;
            tDeltaY = -cellSize / d.Y;
        }
        else
        {
            stepY = 0;
            tNextY = float.PositiveInfinity;
            tDeltaY = float.PositiveInfinity;
        }

        while (tNextX < tStart && cx + stepX >= 0 && cx + stepX < numCellsX)
        {
            cx += stepX;
            tNextX += tDeltaX;
        }

        while (tNextY < tStart && cy + stepY >= 0 && cy + stepY < numCellsY)
        {
            cy += stepY;
            tNextY += tDeltaY;
        }

        var maxSteps = 2 * (numCellsX + numCellsY) + 16;
        var steps = 0;

        float? hitT = null;

        while (cx >= 0 && cx < numCellsX && cy >= 0 && cy < numCellsY && steps++ < maxSteps)
        {
            if (TestCell(heightMap, in ray, cx, cy, ref hitT))
            {
                return p + (d * hitT.Value);
            }

            if (tNextX < tNextY)
            {
                cx += stepX;
                tNextX += tDeltaX;
            }
            else if (tNextY < tNextX)
            {
                cy += stepY;
                tNextY += tDeltaY;
            }
            else
            {
                cx += stepX;
                cy += stepY;
                tNextX += tDeltaX;
                tNextY += tDeltaY;
            }
        }

        return null;
    }

    private static Vector3? IntersectVertical(
        HeightMap heightMap,
        in Ray ray,
        float minX,
        float minY,
        int numCellsX,
        int numCellsY,
        float cellSize)
    {
        var cx = (int)MathF.Floor((ray.Position.X - minX) / cellSize);
        var cy = (int)MathF.Floor((ray.Position.Y - minY) / cellSize);

        cx = Math.Clamp(cx, 0, numCellsX - 1);
        cy = Math.Clamp(cy, 0, numCellsY - 1);

        float? closestT = null;
        TestCell(heightMap, in ray, cx, cy, ref closestT);

        var fracX = ((ray.Position.X - minX) / cellSize) - cx;
        var fracY = ((ray.Position.Y - minY) / cellSize) - cy;

        if (fracX < 1e-4f && cx > 0)
        {
            TestCell(heightMap, in ray, cx - 1, cy, ref closestT);
        }
        else if (fracX > 1f - 1e-4f && cx < numCellsX - 1)
        {
            TestCell(heightMap, in ray, cx + 1, cy, ref closestT);
        }

        if (fracY < 1e-4f && cy > 0)
        {
            TestCell(heightMap, in ray, cx, cy - 1, ref closestT);
        }
        else if (fracY > 1f - 1e-4f && cy < numCellsY - 1)
        {
            TestCell(heightMap, in ray, cx, cy + 1, ref closestT);
        }

        if (closestT == null)
        {
            return null;
        }

        return ray.Position + (ray.Direction * closestT.Value);
    }

    private static bool TestCell(HeightMap heightMap, in Ray ray, int cx, int cy, ref float? closestT)
    {
        var p00 = heightMap.GetPosition(cx, cy);
        var p10 = heightMap.GetPosition(cx + 1, cy);
        var p01 = heightMap.GetPosition(cx, cy + 1);
        var p11 = heightMap.GetPosition(cx + 1, cy + 1);

        var tri1 = new Triangle(p00, p10, p01);
        var tri2 = new Triangle(p01, p10, p11);

        var hit = false;
        if (ray.Intersects(tri1, out var t1) && t1.HasValue)
        {
            if (closestT == null || t1.Value < closestT.Value)
            {
                closestT = t1.Value;
                hit = true;
            }
        }

        if (ray.Intersects(tri2, out var t2) && t2.HasValue)
        {
            if (closestT == null || t2.Value < closestT.Value)
            {
                closestT = t2.Value;
                hit = true;
            }
        }

        return hit;
    }
}
