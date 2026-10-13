using System;
using System.Numerics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering.Instancing;
using Panopticon.Mathematics;

namespace Panopticon.Graphics.Rendering;

/// <summary>
/// Distance LOD gate (B5-3): a mesh whose bounding sphere projects below a small screen
/// radius is skipped entirely, in every bucket. This is the LOD floor; impostor
/// replacement and multi-LOD asset selection are recorded follow-ups in the B5 deep dive.
/// </summary>
internal static class ModelLod
{
    /// <summary>True when the mesh is negligible at this camera and should not render at all.</summary>
    public static bool IsNegligible(Camera camera, in BoundingSphere sphere, in Matrix4x4 world)
    {
        // A degenerate viewport (the headless panel's 1x1 fallback before EnsureFrame)
        // carries no screen-size information; the gate is meaningless there.
        if (camera.ScreenSize.X < 2f || camera.ScreenSize.Y < 2f)
        {
            return false;
        }

        return RenderScaleOptions.SmallFeatureCulling
            && ProjectedRadiusPx(camera, sphere, world) < RenderScaleOptions.SmallFeatureCullingPx;
    }

    /// <summary>
    /// Screen-space radius in pixels of a world-space sphere under a world transform.
    /// Uniform or non-uniform scale in the transform scales the radius by its largest axis.
    /// </summary>
    public static float ProjectedRadiusPx(Camera camera, in BoundingSphere sphere, in Matrix4x4 world)
    {
        var center = Vector3.Transform(sphere.Center, world);
        var scaleX = new Vector3(world.M11, world.M12, world.M13).Length();
        var scaleY = new Vector3(world.M21, world.M22, world.M23).Length();
        var scaleZ = new Vector3(world.M31, world.M32, world.M33).Length();
        var radius = sphere.Radius * MathF.Max(scaleX, MathF.Max(scaleY, scaleZ));

        var distance = Vector3.Distance(camera.Position, center);
        if (distance <= radius)
        {
            // The camera is inside the sphere; it cannot be negligible.
            return float.MaxValue;
        }

        var verticalFov = camera.VerticalFieldOfView;
        var projectedRadius = radius / (distance * MathF.Tan(verticalFov / 2.0f));
        return projectedRadius * (camera.ScreenSize.Y / 2.0f);
    }
}
