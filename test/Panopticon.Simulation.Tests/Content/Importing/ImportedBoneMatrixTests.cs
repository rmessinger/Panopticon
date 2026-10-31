using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Graphics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class ImportedBoneMatrixTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void ExactBindPreservesNonuniformScaleAndShearThroughRootScaling(float rootScale)
    {
        var rootMatrix = Matrix4x4.CreateScale(2f, 3f, 4f);
        rootMatrix.M12 = 0.25f;
        rootMatrix.Translation = new Vector3(5f, 6f, 7f);
        var childMatrix = Matrix4x4.CreateScale(0.5f, 1.5f, 2.5f);
        childMatrix.Translation = new Vector3(1f, 2f, 3f);
        Matrix4x4.Invert(rootMatrix, out var rootInverse);
        Matrix4x4.Invert(childMatrix * rootMatrix, out var childInverse);
        var root = new ImportedBone("Root", -1, rootMatrix.Translation, Quaternion.Identity,
            rootInverse, 1f, rootMatrix);
        var child = new ImportedBone("Child", 0, childMatrix.Translation, Quaternion.Identity,
            childInverse, 1f, childMatrix);

        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton("Exact binds", [root, child]), rootScale);

        Assert.Equal(rootMatrix, root.BindLocalMatrix);
        Assert.Equal(rootMatrix * Matrix4x4.CreateScale(rootScale), hierarchy.Bones[0].Transform.Matrix);
        Assert.Equal(childMatrix, hierarchy.Bones[1].Transform.Matrix);
        Assert.Equal(childInverse, hierarchy.Bones[1].InverseBindMatrix);
    }
}
