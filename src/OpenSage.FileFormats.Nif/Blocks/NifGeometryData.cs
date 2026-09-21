using System.Collections.Generic;
using System.Numerics;

using System;
using System.IO;

namespace OpenSage.FileFormats.Nif;

/// <summary>
/// NiGeometryData at 10.1.0.0: vertex positions, optional normals, source
/// bounds, optional vertex colors, UV sets, and consistency flags.
/// </summary>
public abstract record NifGeometryData : NifBlock
{
    public const ushort TangentsFlag = 0x1000;
    private const int UvSetCountMask = 0x3F;

    public int NumVertices { get; }
    public byte KeepFlags { get; }
    public byte CompressFlags { get; }
    public IReadOnlyList<Vector3> Vertices { get; }
    public ushort DataFlags { get; }
    public int NumUvSets => DataFlags & UvSetCountMask;
    public IReadOnlyList<Vector3>? Normals { get; }
    public Vector3 BoundCenter { get; }
    public float BoundRadius { get; }
    public IReadOnlyList<Vector2[]> UvSets { get; }
    public ushort ConsistencyFlags { get; }

    protected NifGeometryData(NifBinaryReader reader)
    {
        NumVertices = reader.ReadUInt16("NumVertices");
        KeepFlags = reader.ReadByte("KeepFlags");
        CompressFlags = reader.ReadByte("CompressFlags");
        var hasVertices = reader.ReadByte("HasVertices");
        if (hasVertices != 1)
        {
            throw new InvalidDataException(
                $"NIF geometry data declares HasVertices={hasVertices}; only 1 is valid.");
        }

        var vertices = new Vector3[NumVertices];
        for (var i = 0; i < NumVertices; i++)
        {
            vertices[i] = reader.ReadVector3($"Vertices[{i}]");
        }
        Vertices = vertices;

        DataFlags = reader.ReadUInt16("DataFlags");
        if ((DataFlags & TangentsFlag) != 0)
        {
            throw new NotSupportedException(
                "NIF geometry data carries tangents/bitangents; C4a does not decode them.");
        }

        var hasNormals = reader.ReadByte("HasNormals");
        if (hasNormals == 0)
        {
            throw new NotSupportedException(
                $"NIF geometry data has no normals; C1 requires one normal per vertex.");
        }
        var normals = new Vector3[NumVertices];
        for (var i = 0; i < NumVertices; i++)
        {
            normals[i] = reader.ReadVector3($"Normals[{i}]");
        }
        Normals = normals;

        BoundCenter = reader.ReadVector3("BoundCenter");
        BoundRadius = reader.ReadSingle("BoundRadius");

        var hasColors = reader.ReadByte("HasVertexColors");
        if (hasColors != 0)
        {
            throw new NotSupportedException(
                "NIF geometry data carries vertex colors; C4a-01 proved them absent in both " +
                "targets and C1 has no vertex-color field, so they are rejected.");
        }

        var uvSets = new Vector2[NumUvSets][];
        for (var set = 0; set < NumUvSets; set++)
        {
            var coords = new Vector2[NumVertices];
            for (var i = 0; i < NumVertices; i++)
            {
                var u = reader.ReadSingle($"UvSets[{set}][{i}].U");
                var v = reader.ReadSingle($"UvSets[{set}][{i}].V");
                coords[i] = new Vector2(u, v);
            }
            uvSets[set] = coords;
        }
        UvSets = uvSets;

        ConsistencyFlags = reader.ReadUInt16("ConsistencyFlags");
    }
}
