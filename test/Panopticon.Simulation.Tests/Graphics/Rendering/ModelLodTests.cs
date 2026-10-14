using System.Numerics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Rendering.Instancing;
using Panopticon.Mathematics;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics.Rendering;

/// <summary>
/// B5-3: projected-radius LOD math. Pure CPU; the camera is real but no device is needed.
/// </summary>
public sealed class ModelLodTests
{
    private static Camera CreateCamera(int width = 1000, int height = 500)
    {
        var camera = new Camera(() => new Viewport(0, 0, width, height, 0f, 1f));
        camera.FieldOfView = MathUtility.ToRadians(50f);
        camera.SetLookAt(new Vector3(0, 0, -1000f), Vector3.Zero, Vector3.UnitZ);
        return camera;
    }

    [Fact]
    public void ProjectedRadius_MatchesHandComputedValue()
    {
        var camera = CreateCamera();
        var sphere = new BoundingSphere(Vector3.Zero, 10f);

        // Horizontal fov 50 degrees over a 2:1 viewport gives a 25-degree vertical fov;
        // tan(12.5 degrees) = 0.2217. Radius 10 at distance 1000:
        // 10 / (1000 * 0.2217) * 250 = 11.28 px.
        var px = ModelLod.ProjectedRadiusPx(camera, in sphere, Matrix4x4.Identity);

        Assert.InRange(px, 11.2f, 11.4f);
    }

    [Fact]
    public void WorldScale_ScalesProjectedRadius()
    {
        var camera = CreateCamera();
        var sphere = new BoundingSphere(Vector3.Zero, 10f);

        var unscaled = ModelLod.ProjectedRadiusPx(camera, in sphere, Matrix4x4.Identity);
        var scaled = ModelLod.ProjectedRadiusPx(
            camera,
            in sphere,
            Matrix4x4.CreateScale(2f) * Matrix4x4.CreateTranslation(0, 0, -200f));

        // Distance shrinks to 800 and radius doubles: (20/800) / (10/1000) = 2.5x unscaled.
        Assert.Equal(unscaled * 2.5f, scaled, 2);
    }

    [Fact]
    public void CameraInsideSphere_IsNeverNegligible()
    {
        var camera = CreateCamera();
        var sphere = new BoundingSphere(Vector3.Zero, 10_000f);

        Assert.Equal(float.MaxValue, ModelLod.ProjectedRadiusPx(camera, in sphere, Matrix4x4.Identity));
        Assert.False(ModelLod.IsNegligible(camera, in sphere, Matrix4x4.Identity));
    }

    [Fact]
    public void DegenerateViewport_DisablesGate()
    {
        var camera = new Camera(() => new Viewport(0, 0, 1, 1, 0f, 1f));
        camera.SetLookAt(new Vector3(0, 0, -1000f), Vector3.Zero, Vector3.UnitZ);
        var farSphere = new BoundingSphere(new Vector3(0, 0, 50_000f), 10f);

        // The headless panel's 1x1 fallback carries no screen-size information.
        Assert.False(ModelLod.IsNegligible(camera, in farSphere, Matrix4x4.Identity));
    }

    [Fact]
    public void Gate_ThresholdBoundary_Flips()
    {
        var camera = CreateCamera();

        // Radius 10 at distance 1000 projects to ~11.28 px (see the hand-computed test).
        var nearSphere = new BoundingSphere(Vector3.Zero, 10f);
        var farSphere = new BoundingSphere(new Vector3(0, 0, 50_000f), 10f);

        var original = RenderScaleOptions.SmallFeatureCullingPx;
        try
        {
            RenderScaleOptions.SmallFeatureCulling = true;
            RenderScaleOptions.SmallFeatureCullingPx = 1.0f;

            Assert.False(ModelLod.IsNegligible(camera, in nearSphere, Matrix4x4.Identity));
            Assert.True(ModelLod.IsNegligible(camera, in farSphere, Matrix4x4.Identity));
        }
        finally
        {
            RenderScaleOptions.SmallFeatureCullingPx = original;
        }
    }

    [Fact]
    public void Gate_Disabled_SkipsNothing()
    {
        var camera = CreateCamera();
        var farSphere = new BoundingSphere(new Vector3(0, 0, 50_000f), 10f);

        // The gate flag lives inside IsNegligible; this asserts the pure math helper.
        var gateWasEnabled = RenderScaleOptions.SmallFeatureCulling;
        RenderScaleOptions.SmallFeatureCulling = false;
        try
        {
            Assert.False(ModelLod.IsNegligible(camera, in farSphere, Matrix4x4.Identity));
        }
        finally
        {
            RenderScaleOptions.SmallFeatureCulling = gateWasEnabled;
        }
    }
}
