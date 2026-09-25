using System.Collections.Generic;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiTriShapeData at 10.1.0.0: triangle indices as u16 triplets and
/// per-vertex match groups.
/// </summary>
public sealed record NifTriShapeData : NifTriBasedGeometryData
{
    public int NumTrianglePoints { get; }
    public IReadOnlyList<(ushort V1, ushort V2, ushort V3)> Triangles { get; }
    public IReadOnlyList<NifMatchGroup> MatchGroups { get; }

    private NifTriShapeData(NifBinaryReader reader)
        : base(reader)
    {
        NumTrianglePoints = reader.ReadInt32("NumTrianglePoints");
        if (NumTrianglePoints != 3 * NumTriangles)
        {
            throw new InvalidDataException(
                $"NIF geometry data declares {NumTrianglePoints} triangle points for " +
                $"{NumTriangles} triangles; expected {3 * NumTriangles}.");
        }

        var hasTriangles = reader.ReadByte("HasTriangles");
        if (hasTriangles != 1)
        {
            throw new InvalidDataException(
                $"NIF geometry data declares HasTriangles={hasTriangles}; only 1 is valid.");
        }

        var triangles = new (ushort, ushort, ushort)[NumTriangles];
        for (var i = 0; i < NumTriangles; i++)
        {
            var v1 = reader.ReadUInt16($"Triangles[{i}].V1");
            var v2 = reader.ReadUInt16($"Triangles[{i}].V2");
            var v3 = reader.ReadUInt16($"Triangles[{i}].V3");
            if (v1 >= NumVertices || v2 >= NumVertices || v3 >= NumVertices)
            {
                throw new InvalidDataException(
                    $"NIF triangle {i} ({v1}, {v2}, {v3}) references a vertex beyond " +
                    $"the declared count {NumVertices}.");
            }

            triangles[i] = (v1, v2, v3);
        }

        Triangles = triangles;

        var numMatchGroups = reader.ReadUInt16("NumMatchGroups");
        var matchGroups = new NifMatchGroup[numMatchGroups];
        for (var i = 0; i < numMatchGroups; i++)
        {
            var count = reader.ReadUInt16($"MatchGroups[{i}].NumVertices");
            var indices = new ushort[count];
            for (var j = 0; j < count; j++)
            {
                // Preserved for diagnostics only; the EE2 cooker emits indices
                // beyond the declared vertex count (e.g. body_LOD1), so they are
                // recorded verbatim without range validation.
                indices[j] = reader.ReadUInt16($"MatchGroups[{i}].VertexIndices[{j}]");
            }

            matchGroups[i] = new NifMatchGroup(indices);
        }

        MatchGroups = matchGroups;
    }

    internal static NifTriShapeData Read(NifBinaryReader reader)
    {
        return new NifTriShapeData(reader);
    }
}
