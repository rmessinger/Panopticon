using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiSkinData at 10.1.0.0: the skin transform mapping shape space to the
/// skeleton root's parent space, one optional partition reference, and per
/// bone transforms with vertex weights. The has-vertex-weights flag is
/// verbatim; converters enforce the C4d contract on it.
/// </summary>
public sealed record NifSkinData : NifBlock
{
    // Minimum per bone: transform (52) + bound center (12) + radius (4) +
    // vertex count (2).
    private const int BytesPerBone = 70;

    public NifSkinTransform SkinTransform { get; }
    public IReadOnlyList<NifSkinBoneData> Bones { get; }
    public int PartitionReference { get; }
    public bool HasVertexWeights { get; }

    private NifSkinData(NifBinaryReader reader)
    {
        SkinTransform = NifSkinTransform.Read(reader, "SkinTransform");
        var boneCount = reader.ReadElementCount(BytesPerBone, "Bones.Count");
        PartitionReference = reader.ReadBlockReference("Partition");
        HasVertexWeights = reader.ReadByte("HasVertexWeights") != 0;
        var bones = new NifSkinBoneData[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            var transform = NifSkinTransform.Read(reader, $"Bones[{i}].Transform");
            var boundCenter = reader.ReadVector3($"Bones[{i}].BoundCenter");
            var boundRadius = reader.ReadSingle($"Bones[{i}].BoundRadius");
            var weightCount = reader.ReadUInt16($"Bones[{i}].VertexWeights.Count");
            if ((long)weightCount * 6 > reader.RemainingBytes)
            {
                throw NewTruncatedWeights(i, weightCount, reader.RemainingBytes);
            }

            var weights = new NifSkinVertexWeight[weightCount];
            for (var j = 0; j < weightCount; j++)
            {
                weights[j] = new NifSkinVertexWeight(
                    reader.ReadUInt16($"Bones[{i}].VertexWeights[{j}].VertexIndex"),
                    reader.ReadSingle($"Bones[{i}].VertexWeights[{j}].Weight"));
            }

            bones[i] = new NifSkinBoneData(transform, boundCenter, boundRadius, weights);
        }

        Bones = bones;
    }

    private static InvalidDataException NewTruncatedWeights(
        int boneIndex, int weightCount, long remainingBytes)
    {
        return new InvalidDataException(
            $"NIF NiSkinData bone {boneIndex} declares {weightCount} vertex weights " +
            $"(6 bytes each) but only {remainingBytes} bytes remain.");
    }

    internal static NifSkinData Read(NifBinaryReader reader)
    {
        return new NifSkinData(reader);
    }
}

/// <summary>
/// One skin-space transform: a stored column-vector rotation, a
/// translation, and a uniform scale.
/// </summary>
public readonly record struct NifSkinTransform(NifMatrix33 Rotation, Vector3 Translation, float Scale)
{
    public static NifSkinTransform Read(NifBinaryReader reader, string fieldName)
    {
        var rotation = reader.ReadMatrix33(fieldName + ".Rotation");
        var translation = reader.ReadVector3(fieldName + ".Translation");
        var scale = reader.ReadSingle(fieldName + ".Scale");
        return new NifSkinTransform(rotation, translation, scale);
    }
}

/// <summary>
/// Per-bone skin data: the bone transform (skin space to bone space),
/// bound sphere, and weighted vertices.
/// </summary>
public sealed record NifSkinBoneData(
    NifSkinTransform Transform,
    Vector3 BoundCenter,
    float BoundRadius,
    IReadOnlyList<NifSkinVertexWeight> VertexWeights);

/// <summary>
/// One vertex influence: the vertex index in the shape's vertex array and
/// its weight for the owning bone.
/// </summary>
public readonly record struct NifSkinVertexWeight(ushort VertexIndex, float Weight);
