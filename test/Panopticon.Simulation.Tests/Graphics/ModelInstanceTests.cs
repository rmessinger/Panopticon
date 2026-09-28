using System;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Graphics;
using Panopticon.Mathematics;
using Panopticon.Tests.Content.Importing;
using Panopticon.Utilities;
using Xunit;

namespace Panopticon.Tests.Graphics;

public sealed class ModelInstanceTests
{
    [Fact]
    public void ScaledParentBone_ExpandsMeshBoundsThroughAbsoluteBoneTransforms()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var model = CreateScaledBoneModel();
        var instance = new ModelInstance(model, context.Context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

        // ModelMesh uses AbsoluteBoneTransforms[parentBone.Index] as the rigid mesh world
        // matrix and expands render bounds through AxisAlignedBoundingBox.Transform.
        var parentIndex = Array.FindIndex(model.BoneHierarchy.Bones, bone => bone.Name == "Parent");
        var world = instance.AbsoluteBoneTransforms[parentIndex];

        Assert.Equal(
            2.5f,
            Vector3.TransformNormal(Vector3.UnitX, world).Length(),
            5);

        var localBounds = new AxisAlignedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        var worldBounds = AxisAlignedBoundingBox.Transform(localBounds, world);

        Assert.Equal(new Vector3(-1.5f, -2.5f, -2.5f), worldBounds.Min);
        Assert.Equal(new Vector3(3.5f, 2.5f, 2.5f), worldBounds.Max);
        Assert.Equal(5.0f, worldBounds.Max.X - worldBounds.Min.X, 5);
    }

    [Fact]
    public void UniformScale_PreservesNormalDirectionAfterNormalization()
    {
        // CPU mirror of the shader contract: TransformNormal returns direction unchanged
        // once renormalized, for any uniform bone scale.
        var normal = Vector3.Normalize(new Vector3(0.3f, -0.5f, 0.8f));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
        var rest = new Transform(new Vector3(3, 4, 5), rotation, 2.5f);

        var transformed = Vector3.TransformNormal(normal, rest.Matrix);
        var normalized = Vector3.Normalize(transformed);
        var expected = Vector3.Transform(normal, rotation);

        Assert.Equal(expected.X, normalized.X, 5);
        Assert.Equal(expected.Y, normalized.Y, 5);
        Assert.Equal(expected.Z, normalized.Z, 5);
    }

    private static Model CreateScaledBoneModel()
    {
        var hierarchy = new ModelBoneHierarchy(
            new ImportedSkeleton(
                "TestSkeleton",
                new[]
                {
                    new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity),
                    new ImportedBone("Parent", 0, new Vector3(1, 0, 0), Quaternion.Identity, bindScale: 2.5f),
                }));

        return new Model("TestModel", hierarchy, []);
    }
}
