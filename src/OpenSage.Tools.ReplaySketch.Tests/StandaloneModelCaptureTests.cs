using System;
using System.IO;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Loaders;
using OpenSage.Graphics;
using OpenSage.Mathematics;
using OpenSage.Tools.TestHarness;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// GPU-free tests for the pure bounds and camera-fit calculations extracted from
/// StandaloneModelCapture. Full captures are exercised by the C3-11 feature-matrix suite.
/// </summary>
public class StandaloneModelCaptureTests
{
    [Fact]
    public void EmptyModel_FailsExplicitly()
    {
        var model = CreateModel(0);
        Assert.Throws<InvalidDataException>(() =>
            StandaloneModelCapture.ComputeEnclosingBoundingBox(model));
    }

    [Fact]
    public void CenteredBounds_TargetOrigin_WithDiagonalDistance()
    {
        var model = CreateModel(1, new AxisAlignedBoundingBox(
            new Vector3(-1, -1, -1),
            new Vector3(1, 1, 1)));

        var bounds = StandaloneModelCapture.ComputeEnclosingBoundingBox(model);
        Assert.Equal(new Vector3(-1, -1, -1), bounds.Min);
        Assert.Equal(new Vector3(1, 1, 1), bounds.Max);

        var (target, distance) = StandaloneModelCapture.FitCamera(bounds);
        Assert.Equal(Vector3.Zero, target);
        Assert.Equal(Vector3.Distance(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)), distance, 5);
    }

    [Fact]
    public void OffsetBounds_TargetCenter()
    {
        var model = CreateModel(2,
            new AxisAlignedBoundingBox(new Vector3(10, 20, 30), new Vector3(12, 22, 34)),
            new AxisAlignedBoundingBox(new Vector3(-5, -5, -5), new Vector3(0, 0, 0)));

        var bounds = StandaloneModelCapture.ComputeEnclosingBoundingBox(model);
        Assert.Equal(new Vector3(-5, -5, -5), bounds.Min);
        Assert.Equal(new Vector3(12, 22, 34), bounds.Max);

        var (target, _) = StandaloneModelCapture.FitCamera(bounds);
        Assert.Equal(new Vector3(3.5f, 8.5f, 14.5f), target);
    }

    private static Model CreateModel(int meshCount, params AxisAlignedBoundingBox[] meshBounds)
    {
        var skeleton = new ImportedSkeleton(
            "CaptureSkeleton",
            new[] { new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity) });
        var hierarchy = new ModelBoneHierarchy(skeleton);

        var subObjects = new ModelSubObject[meshCount];
        for (var i = 0; i < meshCount; i++)
        {
            var box = new ImportedBox(
                (meshBounds[i].Min + meshBounds[i].Max) / 2,
                (meshBounds[i].Max - meshBounds[i].Min) / 2);
            subObjects[i] = new ModelSubObject(
                $"Container.Mesh{i}",
                $"Mesh{i}",
                hierarchy.Bones[0],
                new ModelBox(box));
        }

        return new Model("CaptureModel", hierarchy, subObjects);
    }
}
