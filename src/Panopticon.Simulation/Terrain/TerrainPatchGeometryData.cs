#nullable enable

using System.Numerics;
using Panopticon.Mathematics;

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
