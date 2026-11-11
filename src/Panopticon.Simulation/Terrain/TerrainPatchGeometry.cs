#nullable enable

using System;
using System.Numerics;
using Panopticon.Mathematics;
using Panopticon.Terrain.Cdlod;

namespace Panopticon.Terrain;

public class TerrainPatchGeometryData
{
    public Vector3[] Positions { get; }

    public Vector3[] Normals { get; }

    public Vector2[] UVs { get; }

    public ushort[] Indices { get; }

    public Triangle[] Triangles { get; }

    public AxisAlignedBoundingBox BoundingBox { get; }

    public TerrainPatchGeometryData(
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] uvs,
        ushort[] indices,
        Triangle[] triangles,
        AxisAlignedBoundingBox boundingBox)
    {
        Positions = positions;
        Normals = normals;
        UVs = uvs;
        Indices = indices;
        Triangles = triangles;
        BoundingBox = boundingBox;
    }
}

public sealed class TerrainPatchGeometryResult : TerrainPatchGeometryData
{
    public TerrainPatchGeometryResult(
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] uvs,
        ushort[] indices,
        Triangle[] triangles,
        AxisAlignedBoundingBox boundingBox)
        : base(positions, normals, uvs, indices, triangles, boundingBox)
    {
    }
}

public static class TerrainPatchGeometry
{
    public const int GridSize = 17;
    public const int Intervals = 16;
    public const int VertexCount = GridSize * GridSize;
    public const int QuadCount = Intervals * Intervals;
    public const int TriangleCount = QuadCount * 2;
    public const int IndexCount = TriangleCount * 3;

    public static TerrainPatchGeometryResult Build(
        HeightMap heightMap,
        CdlodNode node,
        float morph,
        CdlodEdges coarserEdges,
        Func<CdlodEdges, float?>? getNeighbourMorph = null,
        ITerrainLatticeSampler? sampler = null)
    {
        ArgumentNullException.ThrowIfNull(heightMap);

        var clampedMorph = Math.Clamp(morph, 0f, 1f);
        var positions = new Vector3[VertexCount];
        var normals = new Vector3[VertexCount];
        var uvs = new Vector2[VertexCount];

        var stitchNorth = coarserEdges.HasFlag(CdlodEdges.North);
        var stitchSouth = coarserEdges.HasFlag(CdlodEdges.South);
        var stitchEast = coarserEdges.HasFlag(CdlodEdges.East);
        var stitchWest = coarserEdges.HasFlag(CdlodEdges.West);

        var coarseNorth = stitchNorth ? GetCoarseNeighbour(node, CdlodEdges.North) : default;
        var coarseSouth = stitchSouth ? GetCoarseNeighbour(node, CdlodEdges.South) : default;
        var coarseEast = stitchEast ? GetCoarseNeighbour(node, CdlodEdges.East) : default;
        var coarseWest = stitchWest ? GetCoarseNeighbour(node, CdlodEdges.West) : default;

        var morphNorth = stitchNorth ? (getNeighbourMorph?.Invoke(CdlodEdges.North) ?? 0f) : 0f;
        var morphSouth = stitchSouth ? (getNeighbourMorph?.Invoke(CdlodEdges.South) ?? 0f) : 0f;
        var morphEast = stitchEast ? (getNeighbourMorph?.Invoke(CdlodEdges.East) ?? 0f) : 0f;
        var morphWest = stitchWest ? (getNeighbourMorph?.Invoke(CdlodEdges.West) ?? 0f) : 0f;

        for (var k = 0; k < GridSize; k++)
        {
            for (var i = 0; i < GridSize; i++)
            {
                var index = k * GridSize + i;
                var sampleCoords = GetSampleCoords(heightMap, node, i, k);
                normals[index] = heightMap.Normals[sampleCoords.X, sampleCoords.Y];
                uvs[index] = new Vector2(sampleCoords.X, sampleCoords.Y);

                var isCorner = (i == 0 || i == Intervals) && (k == 0 || k == Intervals);
                if (isCorner)
                {
                    positions[index] = GetRawPosition(heightMap, node, i, k, sampler);
                    continue;
                }

                if (k == Intervals && stitchNorth)
                {
                    positions[index] = ComputeStitchedEdgePosition(
                        heightMap,
                        node,
                        coarseNorth,
                        i,
                        node.X % 2 != 0,
                        true,
                        0,
                        morphNorth,
                        sampler);
                    continue;
                }

                if (k == 0 && stitchSouth)
                {
                    positions[index] = ComputeStitchedEdgePosition(
                        heightMap,
                        node,
                        coarseSouth,
                        i,
                        node.X % 2 != 0,
                        true,
                        Intervals,
                        morphSouth,
                        sampler);
                    continue;
                }

                if (i == Intervals && stitchEast)
                {
                    positions[index] = ComputeStitchedEdgePosition(
                        heightMap,
                        node,
                        coarseEast,
                        k,
                        node.Y % 2 != 0,
                        false,
                        0,
                        morphEast,
                        sampler);
                    continue;
                }

                if (i == 0 && stitchWest)
                {
                    positions[index] = ComputeStitchedEdgePosition(
                        heightMap,
                        node,
                        coarseWest,
                        k,
                        node.Y % 2 != 0,
                        false,
                        Intervals,
                        morphWest,
                        sampler);
                    continue;
                }

                positions[index] = ComputeRenderedPosition(heightMap, node, i, k, clampedMorph, sampler);
            }
        }

        var indices = new ushort[IndexCount];
        var triangles = new Triangle[TriangleCount];
        var triangleIndex = 0;
        var indexIndex = 0;

        for (var y = 0; y < Intervals; y++)
        {
            var yThis = y * GridSize;
            var yNext = (y + 1) * GridSize;

            for (var x = 0; x < Intervals; x++)
            {
                var i0 = (ushort)(yThis + x);
                var i1 = (ushort)(yThis + x + 1);
                var i2 = (ushort)(yNext + x);
                var i3 = (ushort)(yNext + x + 1);

                indices[indexIndex++] = i0;
                indices[indexIndex++] = i1;
                indices[indexIndex++] = i2;
                triangles[triangleIndex++] = new Triangle(positions[i0], positions[i1], positions[i2]);

                indices[indexIndex++] = i2;
                indices[indexIndex++] = i1;
                indices[indexIndex++] = i3;
                triangles[triangleIndex++] = new Triangle(positions[i2], positions[i1], positions[i3]);
            }
        }

        var boundingBox = AxisAlignedBoundingBox.CreateFromPoints(positions);

        return new TerrainPatchGeometryResult(
            positions,
            normals,
            uvs,
            indices,
            triangles,
            boundingBox);
    }

    public static CdlodNode GetCoarseNeighbour(CdlodNode node, CdlodEdges edge)
    {
        var coarseLevel = node.Level + 1;
        return edge switch
        {
            CdlodEdges.North => new CdlodNode(node.X / 2, (node.Y + 1) / 2, coarseLevel),
            CdlodEdges.South => new CdlodNode(node.X / 2, node.Y / 2 - 1, coarseLevel),
            CdlodEdges.East => new CdlodNode((node.X + 1) / 2, node.Y / 2, coarseLevel),
            CdlodEdges.West => new CdlodNode(node.X / 2 - 1, node.Y / 2, coarseLevel),
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };
    }

    public static Vector3 ComputeRenderedPosition(
        HeightMap heightMap,
        CdlodNode node,
        int i,
        int k,
        float morph,
        ITerrainLatticeSampler? sampler = null)
    {
        var raw = GetRawPosition(heightMap, node, i, k, sampler);
        if (morph <= 0f)
        {
            return raw;
        }

        var isOddX = (i % 2 != 0);
        var isOddY = (k % 2 != 0);

        if (!isOddX && !isOddY)
        {
            return raw;
        }

        if (isOddX && !isOddY)
        {
            var p0 = GetRawPosition(heightMap, node, i - 1, k, sampler);
            var p1 = GetRawPosition(heightMap, node, i + 1, k, sampler);

            var x0 = GetSampleCoords(heightMap, node, i - 1, k).X;
            var x1 = GetSampleCoords(heightMap, node, i + 1, k).X;
            var xm = GetSampleCoords(heightMap, node, i, k).X;

            var t = (x1 > x0) ? (float)(xm - x0) / (x1 - x0) : 0.5f;
            var target = Vector3.Lerp(p0, p1, t);
            return Vector3.Lerp(raw, target, morph);
        }

        if (!isOddX && isOddY)
        {
            var p0 = GetRawPosition(heightMap, node, i, k - 1, sampler);
            var p1 = GetRawPosition(heightMap, node, i, k + 1, sampler);

            var y0 = GetSampleCoords(heightMap, node, i, k - 1).Y;
            var y1 = GetSampleCoords(heightMap, node, i, k + 1).Y;
            var ym = GetSampleCoords(heightMap, node, i, k).Y;

            var t = (y1 > y0) ? (float)(ym - y0) / (y1 - y0) : 0.5f;
            var target = Vector3.Lerp(p0, p1, t);
            return Vector3.Lerp(raw, target, morph);
        }

        // Odd in both X and Y: bilinear interpolation toward four even corners
        var c00 = GetRawPosition(heightMap, node, i - 1, k - 1, sampler);
        var c10 = GetRawPosition(heightMap, node, i + 1, k - 1, sampler);
        var c01 = GetRawPosition(heightMap, node, i - 1, k + 1, sampler);
        var c11 = GetRawPosition(heightMap, node, i + 1, k + 1, sampler);

        var xCoords0 = GetSampleCoords(heightMap, node, i - 1, k).X;
        var xCoords1 = GetSampleCoords(heightMap, node, i + 1, k).X;
        var xCoordsM = GetSampleCoords(heightMap, node, i, k).X;

        var yCoords0 = GetSampleCoords(heightMap, node, i, k - 1).Y;
        var yCoords1 = GetSampleCoords(heightMap, node, i, k + 1).Y;
        var yCoordsM = GetSampleCoords(heightMap, node, i, k).Y;

        var tX = (xCoords1 > xCoords0) ? (float)(xCoordsM - xCoords0) / (xCoords1 - xCoords0) : 0.5f;
        var tY = (yCoords1 > yCoords0) ? (float)(yCoordsM - yCoords0) / (yCoords1 - yCoords0) : 0.5f;

        var targetBottom = Vector3.Lerp(c00, c10, tX);
        var targetTop = Vector3.Lerp(c01, c11, tX);
        var fullTarget = Vector3.Lerp(targetBottom, targetTop, tY);

        return Vector3.Lerp(raw, fullTarget, morph);
    }

    private static Vector3 ComputeStitchedEdgePosition(
        HeightMap heightMap,
        CdlodNode fineNode,
        CdlodNode coarseNode,
        int fineIndex,
        bool isSecondHalf,
        bool isHorizontalEdge,
        int coarseFixedCoord,
        float coarseMorph,
        ITerrainLatticeSampler? sampler)
    {
        var j = fineIndex / 2;
        var coarseBase = isSecondHalf ? 8 : 0;

        if (fineIndex % 2 == 0)
        {
            var coarseV = coarseBase + j;
            return isHorizontalEdge
                ? ComputeRenderedPosition(heightMap, coarseNode, coarseV, coarseFixedCoord, coarseMorph, sampler)
                : ComputeRenderedPosition(heightMap, coarseNode, coarseFixedCoord, coarseV, coarseMorph, sampler);
        }

        var coarseV0 = coarseBase + j;
        var coarseV1 = coarseV0 + 1;

        var p0 = isHorizontalEdge
            ? ComputeRenderedPosition(heightMap, coarseNode, coarseV0, coarseFixedCoord, coarseMorph, sampler)
            : ComputeRenderedPosition(heightMap, coarseNode, coarseFixedCoord, coarseV0, coarseMorph, sampler);

        var p1 = isHorizontalEdge
            ? ComputeRenderedPosition(heightMap, coarseNode, coarseV1, coarseFixedCoord, coarseMorph, sampler)
            : ComputeRenderedPosition(heightMap, coarseNode, coarseFixedCoord, coarseV1, coarseMorph, sampler);

        float t;
        if (isHorizontalEdge)
        {
            var c0 = GetSampleCoords(heightMap, coarseNode, coarseV0, coarseFixedCoord).X;
            var c1 = GetSampleCoords(heightMap, coarseNode, coarseV1, coarseFixedCoord).X;
            var cm = GetSampleCoords(heightMap, fineNode, fineIndex, coarseFixedCoord == 0 ? Intervals : 0).X;
            t = (c1 > c0) ? (float)(cm - c0) / (c1 - c0) : 0.5f;
        }
        else
        {
            var c0 = GetSampleCoords(heightMap, coarseNode, coarseFixedCoord, coarseV0).Y;
            var c1 = GetSampleCoords(heightMap, coarseNode, coarseFixedCoord, coarseV1).Y;
            var cm = GetSampleCoords(heightMap, fineNode, coarseFixedCoord == 0 ? Intervals : 0, fineIndex).Y;
            t = (c1 > c0) ? (float)(cm - c0) / (c1 - c0) : 0.5f;
        }

        return Vector3.Lerp(p0, p1, t);
    }

    public static (int X, int Y) GetSampleCoords(HeightMap heightMap, CdlodNode node, int i, int k)
    {
        var step = 1 << node.Level;
        var span = (long)Intervals << node.Level;
        var originX = (long)node.X * span;
        var originY = (long)node.Y * span;
        var lastX = heightMap.Width - 1;
        var lastY = heightMap.Height - 1;

        var sampleX = (int)Math.Clamp(originX + (long)i * step, 0, lastX);
        var sampleY = (int)Math.Clamp(originY + (long)k * step, 0, lastY);

        return (sampleX, sampleY);
    }

    public static Vector3 GetRawPosition(
        HeightMap heightMap,
        CdlodNode node,
        int i,
        int k,
        ITerrainLatticeSampler? sampler = null)
    {
        var coords = GetSampleCoords(heightMap, node, i, k);
        var pos = heightMap.GetPosition(coords.X, coords.Y);

        if (sampler != null)
        {
            return new Vector3(pos.X, pos.Y, sampler.GetHeight(coords.X, coords.Y));
        }

        return pos;
    }
}
