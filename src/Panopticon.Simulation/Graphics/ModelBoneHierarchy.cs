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
    {
        SetNameAndInstanceId("W3DHierarchy", importedSkeleton.Name);

        Bones = new ModelBone[importedSkeleton.Bones.Count];

        for (var i = 0; i < Bones.Length; i++)
        {
            var importedBone = importedSkeleton.Bones[i];

            var parent = importedBone.ParentIndex == -1
                ? null
                : Bones[importedBone.ParentIndex];

            Bones[i] = new ModelBone(
                i,
                importedBone.Name,
                parent,
                importedBone.BindTranslation,
                importedBone.BindRotation,
                importedBone.InverseBindMatrix);
        }
    }
}
