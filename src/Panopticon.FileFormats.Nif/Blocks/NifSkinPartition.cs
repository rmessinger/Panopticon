using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiSkinPartition at 10.1.0.0: hardware-skinning partitions. Parsed and
/// validated for exact stream consumption, then ignored; NiSkinData
/// weights are authoritative for C4d.
/// </summary>
public sealed record NifSkinPartition : NifBlock
{
    // Minimum per partition: five u16 counts.
    private const int BytesPerPartition = 10;

    public IReadOnlyList<NifSkinPartitionData> Partitions { get; }

    private NifSkinPartition(NifBinaryReader reader)
    {
        var count = reader.ReadElementCount(BytesPerPartition, "Partitions.Count");
        var partitions = new NifSkinPartitionData[count];
        for (var i = 0; i < count; i++)
        {
            partitions[i] = NifSkinPartitionData.Read(reader, $"Partitions[{i}]");
        }

        Partitions = partitions;
    }

    internal static NifSkinPartition Read(NifBinaryReader reader)
    {
        return new NifSkinPartition(reader);
    }
}

/// <summary>
/// One partition: bone lists, vertex map and weights, and strip or
/// triangle faces, each gated by its has flag as stored.
/// </summary>
public sealed record NifSkinPartitionData(
    int VertexCount,
    int TriangleCount,
    IReadOnlyList<ushort> Bones,
    IReadOnlyList<uint> StripLengths,
    bool HasVertexMap,
    IReadOnlyList<ushort> VertexMap,
    bool HasVertexWeights,
    IReadOnlyList<float[]> VertexWeights,
    bool HasFaces,
    IReadOnlyList<IReadOnlyList<ushort>> Faces,
    bool HasBoneIndices,
    IReadOnlyList<byte[]> BoneIndices)
{
    public static NifSkinPartitionData Read(NifBinaryReader reader, string fieldName)
    {
        var vertexCount = reader.ReadUInt16(fieldName + ".VertexCount");
        var triangleCount = reader.ReadUInt16(fieldName + ".TriangleCount");
        var boneCount = reader.ReadUInt16(fieldName + ".BoneCount");
        var stripCount = reader.ReadUInt16(fieldName + ".StripCount");
        var weightsPerVertex = reader.ReadUInt16(fieldName + ".WeightsPerVertex");

        var needed = (long)boneCount * 2 + (long)stripCount * 4 + 4;
        if (needed > reader.RemainingBytes)
        {
            throw new InvalidDataException(
                $"NIF NiSkinPartition {fieldName} declares {boneCount} bones and {stripCount} " +
                $"strips but only {reader.RemainingBytes} bytes remain.");
        }

        var bones = new ushort[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            bones[i] = reader.ReadUInt16(fieldName + $".Bones[{i}]");
        }

        var hasVertexMap = reader.ReadByte(fieldName + ".HasVertexMap") != 0;
        var vertexMap = ReadU16Array(
            reader, hasVertexMap ? vertexCount : 0, fieldName + ".VertexMap");

        var hasVertexWeights = reader.ReadByte(fieldName + ".HasVertexWeights") != 0;
        float[][] weights = [];
        if (hasVertexWeights)
        {
            if ((long)vertexCount * weightsPerVertex * 4 > reader.RemainingBytes)
            {
                throw new InvalidDataException(
                    $"NIF NiSkinPartition {fieldName} declares {vertexCount} vertices with " +
                    $"{weightsPerVertex} weights each but only {reader.RemainingBytes} bytes remain.");
            }

            weights = new float[vertexCount][];
            for (var i = 0; i < vertexCount; i++)
            {
                var row = new float[weightsPerVertex];
                for (var j = 0; j < weightsPerVertex; j++)
                {
                    row[j] = reader.ReadSingle(fieldName + $".VertexWeights[{i}][{j}]");
                }

                weights[i] = row;
            }
        }

        var stripLengths = new uint[stripCount];
        if ((long)stripCount * 4 > reader.RemainingBytes)
        {
            throw new InvalidDataException(
                $"NIF NiSkinPartition {fieldName} declares {stripCount} strips but only " +
                $"{reader.RemainingBytes} bytes remain.");
        }

        for (var i = 0; i < stripCount; i++)
        {
            stripLengths[i] = reader.ReadUInt32(fieldName + $".StripLengths[{i}]");
        }

        var hasFaces = reader.ReadByte(fieldName + ".HasFaces") != 0;
        IReadOnlyList<ushort>[] faces;
        if (stripCount > 0)
        {
            faces = new IReadOnlyList<ushort>[stripCount];
            if (hasFaces)
            {
                for (var i = 0; i < stripCount; i++)
                {
                    faces[i] = ReadU16Array(
                        reader, (int)stripLengths[i], fieldName + $".Strips[{i}]");
                }
            }
        }
        else
        {
            faces = hasFaces
                ? [ReadU16Array(reader, triangleCount * 3, fieldName + ".Triangles")]
                : [];
        }

        var hasBoneIndices = reader.ReadByte(fieldName + ".HasBoneIndices") != 0;
        byte[][] boneIndices = [];
        if (hasBoneIndices)
        {
            if ((long)vertexCount * weightsPerVertex > reader.RemainingBytes)
            {
                throw new InvalidDataException(
                    $"NIF NiSkinPartition {fieldName} declares {vertexCount} vertices with " +
                    $"{weightsPerVertex} bone indices each but only {reader.RemainingBytes} bytes remain.");
            }

            boneIndices = new byte[vertexCount][];
            for (var i = 0; i < vertexCount; i++)
            {
                var row = new byte[weightsPerVertex];
                for (var j = 0; j < weightsPerVertex; j++)
                {
                    row[j] = reader.ReadByte(fieldName + $".BoneIndices[{i}][{j}]");
                }

                boneIndices[i] = row;
            }
        }

        return new NifSkinPartitionData(
            vertexCount,
            triangleCount,
            bones,
            stripLengths,
            hasVertexMap,
            vertexMap,
            hasVertexWeights,
            weights,
            hasFaces,
            faces,
            hasBoneIndices,
            boneIndices);
    }

    private static ushort[] ReadU16Array(NifBinaryReader reader, int count, string fieldName)
    {
        if ((long)count * 2 > reader.RemainingBytes)
        {
            throw new InvalidDataException(
                $"NIF NiSkinPartition {fieldName} declares {count} u16 entries but only " +
                $"{reader.RemainingBytes} bytes remain.");
        }

        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = reader.ReadUInt16(fieldName);
        }

        return values;
    }
}
