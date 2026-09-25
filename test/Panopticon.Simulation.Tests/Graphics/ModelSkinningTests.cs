using System;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.Graphics;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class ModelSkinningTests
{
    private static Matrix4x4 CreateNonIdentityMatrix(float tx, float ty, float tz)
    {
        return Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f)) *
               Matrix4x4.CreateTranslation(tx, ty, tz);
    }

    [Fact]
    public void ImportedBone_RetainsExactInverseBindMatrix()
    {
        var inverseBind = CreateNonIdentityMatrix(3, 4, 5);

        var bone = new ImportedBone("bone", 0, Vector3.Zero, Quaternion.Identity, inverseBind);

        Assert.Equal(inverseBind, bone.InverseBindMatrix);
    }

    [Fact]
    public void ImportedBone_CompatibilityConstructorDefaultsToIdentity()
    {
        var bone = new ImportedBone("bone", 0, Vector3.Zero, Quaternion.Identity);

        Assert.Equal(Matrix4x4.Identity, bone.InverseBindMatrix);
    }

    [Fact]
    public void ImportedSkeletonToRuntimeHierarchy_CopiesInverseBindMatrix()
    {
        var inverseBind = CreateNonIdentityMatrix(1, 2, 3);
        var skeleton = new ImportedSkeleton(
            "skeleton",
            new[]
            {
                new ImportedBone("root", -1, Vector3.Zero, Quaternion.Identity),
                new ImportedBone("child", 0, new Vector3(0, 0, 5), Quaternion.Identity, inverseBind),
            });

        var hierarchy = new ModelBoneHierarchy(skeleton);

        Assert.Equal(Matrix4x4.Identity, hierarchy.Bones[0].InverseBindMatrix);
        Assert.Equal(inverseBind, hierarchy.Bones[1].InverseBindMatrix);
    }

    [Fact]
    public void DefaultHierarchyBoneHasIdentityInverseBind()
    {
        var hierarchy = ModelBoneHierarchy.CreateDefault();

        Assert.Equal(Matrix4x4.Identity, hierarchy.Bones[0].InverseBindMatrix);
    }

    [Fact]
    public void SkinningMatrix_MultipliesInverseBindBeforeRelativeTransform()
    {
        // Rotation and translation do not commute, so this pins the multiplication order.
        var inverseBind = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f)) *
                          Matrix4x4.CreateTranslation(10, 0, 0);
        var relative = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.1f)) *
                       Matrix4x4.CreateTranslation(0, 20, 0);
        var bone = new ModelBone(0, "bone", null, Vector3.Zero, Quaternion.Identity, inverseBind);

        var result = ModelInstance.ComputeSkinningMatrix(bone, relative);

        Assert.Equal(inverseBind * relative, result);
        Assert.NotEqual(relative * inverseBind, result);
    }

    [Fact]
    public void SkinningMatrix_IdentityInverseBindReturnsRelativeTransform()
    {
        var relative = CreateNonIdentityMatrix(7, 8, 9);
        var bone = new ModelBone(0, "bone", null, Vector3.Zero, Quaternion.Identity, Matrix4x4.Identity);

        Assert.Equal(relative, ModelInstance.ComputeSkinningMatrix(bone, relative));
    }

    [Fact]
    public void W3dBones_GetIdentityInverseBind()
    {
        // W3D has no inverse-bind concept; conversion must produce identity so the GPU
        // upload stays equal to the bare relative transform.
        var hierarchyDef = new FileFormats.W3d.W3dHierarchyDef(
            new FileFormats.W3d.W3dHierarchy(268, "hierarchy", 1, Vector3.Zero),
            new FileFormats.W3d.W3dPivots(
            [
                new FileFormats.W3d.W3dPivot("pivot", -1, new Vector3(1, 2, 3), Vector3.Zero, Quaternion.Identity),
            ]),
            null);

        var skeleton = W3dModelImporter.ConvertSkeleton(hierarchyDef);

        Assert.All(skeleton.Bones, bone => Assert.Equal(Matrix4x4.Identity, bone.InverseBindMatrix));
    }
}
