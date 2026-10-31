using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Mathematics;
using Panopticon.Tools.TestHarness;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// GPU-free tests for the pure bounds and camera-fit calculations extracted from
/// StandaloneModelCapture. Full captures are exercised by the C3-11 feature-matrix suite.
/// </summary>
public class StandaloneModelCaptureTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(8.117f)]
    public void GroundTransform_AlignsFeetAndDepthWithoutChangingScale(float scale)
    {
        var source = new AxisAlignedBoundingBox(new Vector3(3, -7, -4), new Vector3(5, -1, 10));
        var matrix = StandaloneModelCapture.GroundTransform(source, scale, 20f);
        var grounded = AxisAlignedBoundingBox.Transform(source, matrix);

        Assert.Equal(0f, grounded.Min.Z, 4);
        Assert.Equal(0f, grounded.GetCenter().Y, 4);
        Assert.Equal(20f, grounded.GetCenter().X, 4);
        Assert.Equal(14f * scale, grounded.Max.Z - grounded.Min.Z, 4);
    }

    [Fact]
    public void PerspectiveComparison_ProjectsCommonGroundAndEqualHeightsAtMatchedDepth()
    {
        var camera = new Panopticon.Graphics.Cameras.Camera(() => new Veldrid.Viewport(0, 0, 512, 512, 0, 1))
        {
            NearPlaneDistance = 0.1f,
        };
        camera.SetLookAt(new Vector3(0, -100, 60), new Vector3(0, 0, 10), Vector3.UnitZ);
        var leftFoot = camera.WorldToScreenPoint(new Vector3(-10, 0, 0));
        var rightFoot = camera.WorldToScreenPoint(new Vector3(10, 0, 0));
        var leftHead = camera.WorldToScreenPoint(new Vector3(-10, 0, 15));
        var rightHead = camera.WorldToScreenPoint(new Vector3(10, 0, 15));

        Assert.Equal(leftFoot.Y, rightFoot.Y, 4);
        Assert.Equal(leftFoot.Y - leftHead.Y, rightFoot.Y - rightHead.Y, 4);
    }

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
