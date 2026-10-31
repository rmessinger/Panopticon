using System.Numerics;
using Panopticon.Content.Importing;

namespace Panopticon.Graphics;

public sealed class ModelBoneHierarchy : BaseAsset
{
    public static ModelBoneHierarchy CreateDefault()
    {
        var bones = new ModelBone[1];
        bones[0] = new ModelBone(0, null, null, Vector3.Zero, Quaternion.Identity, Matrix4x4.Identity);
        return new ModelBoneHierarchy("[Default]", bones);
    }

    public ModelBone[] Bones { get; }

    private ModelBoneHierarchy(string name, ModelBone[] bones)
    {
        SetNameAndInstanceId("W3DHierarchy", name);
        Bones = bones;
    }

    internal ModelBoneHierarchy(ImportedSkeleton importedSkeleton)
        : this(importedSkeleton, rootScale: 1f)
    {
    }

    /// <summary>
    /// Card WI-M3-01: materialization at a uniform model scale. Scaling only the root binds
    /// (translation and scale) appends the scale to every bone's absolute chain, which is
    /// identical to premultiplying the world matrix with CreateScale (matrix multiplication is
    /// associative), so rigid meshes, skinned meshes through the unchanged inverse binds, shadows,
    /// and attachments all follow. Rest-relative animation offsets need no scaling: they reproduce
    /// the scaled rest exactly.
    /// </summary>
    internal ModelBoneHierarchy(ImportedSkeleton importedSkeleton, float rootScale)
    {
        SetNameAndInstanceId("W3DHierarchy", importedSkeleton.Name);

        Bones = new ModelBone[importedSkeleton.Bones.Count];

        for (var i = 0; i < Bones.Length; i++)
        {
            var importedBone = importedSkeleton.Bones[i];

            var isRoot = importedBone.ParentIndex == -1;
            var parent = isRoot
                ? null
                : Bones[importedBone.ParentIndex];

            Bones[i] = new ModelBone(
                i,
                importedBone.Name,
                parent,
                isRoot ? importedBone.BindTranslation * rootScale : importedBone.BindTranslation,
                importedBone.BindRotation,
                importedBone.InverseBindMatrix,
                isRoot ? importedBone.BindScale * rootScale : importedBone.BindScale);
            if (importedBone.HasExactBindLocalMatrix)
            {
                Bones[i].Transform.Matrix = isRoot
                    ? importedBone.BindLocalMatrix * Matrix4x4.CreateScale(rootScale)
                    : importedBone.BindLocalMatrix;
            }
        }
    }
}
