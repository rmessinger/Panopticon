#nullable enable

using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Terrain;

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
