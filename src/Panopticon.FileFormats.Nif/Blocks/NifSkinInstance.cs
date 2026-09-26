using System;
using System.Collections.Generic;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiSkinInstance at 10.1.0.0: links a skinned shape to its skin data,
/// its skeleton-root node, and its bone nodes. At this version the block
/// has no partition field; NiSkinData carries the partition reference.
/// </summary>
public sealed record NifSkinInstance : NifBlock
{
    public int DataReference { get; }
    public int SkeletonRootReference { get; }
    public IReadOnlyList<int> BoneReferences { get; }

    private NifSkinInstance(NifBinaryReader reader)
    {
        DataReference = reader.ReadBlockReference("Data");
        SkeletonRootReference = reader.ReadBlockReference("SkeletonRoot");
        var count = reader.ReadElementCount(4, "Bones.Count");
        var bones = new int[count];
        for (var i = 0; i < count; i++)
        {
            bones[i] = reader.ReadBlockReference($"Bones[{i}]");
        }

        BoneReferences = bones;
    }

    internal static NifSkinInstance Read(NifBinaryReader reader)
    {
        return new NifSkinInstance(reader);
    }
}
