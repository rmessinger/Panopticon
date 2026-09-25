#nullable enable

using System;

using System.Numerics;

namespace Panopticon.Content.Importing;

/// <summary>
/// One mesh vertex: primary geometry, optional secondary geometry (null when the source has
/// none), and four-bone skinning slots. The four-slot limit is the current runtime constraint;
/// importers must not silently truncate beyond it.
/// </summary>
public sealed class ImportedVertex
{
    public Vector3 Position { get; }

    public Vector3 Normal { get; }

    public Vector3? SecondaryPosition { get; }

    public Vector3? SecondaryNormal { get; }

    public Vector3? Tangent { get; }

    public Vector3? Binormal { get; }

    public uint BoneIndex0 { get; }

    public uint BoneIndex1 { get; }

    public uint BoneIndex2 { get; }

    public uint BoneIndex3 { get; }

    public float BoneWeight0 { get; }

    public float BoneWeight1 { get; }

    public float BoneWeight2 { get; }

    public float BoneWeight3 { get; }

    public ImportedVertex(
        in Vector3 position,
        in Vector3 normal,
        Vector3? secondaryPosition = null,
        Vector3? secondaryNormal = null,
        Vector3? tangent = null,
        Vector3? binormal = null,
        uint boneIndex0 = 0,
        uint boneIndex1 = 0,
        uint boneIndex2 = 0,
        uint boneIndex3 = 0,
        float boneWeight0 = 0.0f,
        float boneWeight1 = 0.0f,
        float boneWeight2 = 0.0f,
        float boneWeight3 = 0.0f)
    {
        if (boneWeight0 < 0.0f || boneWeight0 > 1.0f)
        {
            throw new ArgumentException("Bone weight 0 must be in [0, 1].", nameof(boneWeight0));
        }

        if (boneWeight1 < 0.0f || boneWeight1 > 1.0f)
        {
            throw new ArgumentException("Bone weight 1 must be in [0, 1].", nameof(boneWeight1));
        }

        if (boneWeight2 < 0.0f || boneWeight2 > 1.0f)
        {
            throw new ArgumentException("Bone weight 2 must be in [0, 1].", nameof(boneWeight2));
        }

        if (boneWeight3 < 0.0f || boneWeight3 > 1.0f)
        {
            throw new ArgumentException("Bone weight 3 must be in [0, 1].", nameof(boneWeight3));
        }

        Position = position;
        Normal = normal;
        SecondaryPosition = secondaryPosition;
        SecondaryNormal = secondaryNormal;
        Tangent = tangent;
        Binormal = binormal;
        BoneIndex0 = boneIndex0;
        BoneIndex1 = boneIndex1;
        BoneIndex2 = boneIndex2;
        BoneIndex3 = boneIndex3;
        BoneWeight0 = boneWeight0;
        BoneWeight1 = boneWeight1;
        BoneWeight2 = boneWeight2;
        BoneWeight3 = boneWeight3;
    }
}
