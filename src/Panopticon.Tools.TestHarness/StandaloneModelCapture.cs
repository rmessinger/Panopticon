using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Input;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Settings;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Explicit deterministic camera: spherical offset (orbit, elevation) around the fitted
/// bounds center at the given distance, with an explicit vertical field of view.
/// </summary>
internal sealed record StandaloneCaptureCamera(
    float OrbitRadians,
    float ElevationRadians,
    float Distance,
    float FieldOfViewRadians);

/// <summary>Optional overrides for a standalone capture; null members keep the defaults.</summary>
internal sealed record StandaloneCaptureOptions(
    StandaloneCaptureCamera? Camera = null,
    Settings.WorldLighting? Lighting = null,
    Mathematics.ColorRgbaF? Background = null);

/// <summary>
/// Renders one already-materialized runtime <see cref="Model"/> to a PNG without the ImGui-based
/// diagnostic ModelView. Creates a diagnostic scene with a fixed viewport, default world
/// lighting, and an arcball camera fitted to the merged sub-object bounds, then executes the
/// render pipeline into an offscreen target. No ImGui, window, or installed-asset dependency.
/// </summary>
internal static class StandaloneModelCapture
{
    public static void Capture(
        Game game,
        Model model,
        AssetLoadContext loadContext,
        string outputPath,
        in Size viewportSize,
        Action<ModelInstance>? configureInstance = null,
        Matrix4x4? worldMatrix = null,
        StandaloneCaptureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        CaptureCore(
            game,
            loadContext,
            outputPath,
            viewportSize,
            [new CaptureSubject(model, worldMatrix ?? Matrix4x4.Identity, configureInstance)],
            options);
    }

    /// <summary>
    /// Captures two models in one frame, for example an imported model beside a native
    /// reference model; the camera fits the union of both transformed bounds.
    /// </summary>
    public static void CapturePair(
        Game game,
        Model primaryModel,
        Matrix4x4 primaryWorldMatrix,
        Model referenceModel,
        Matrix4x4 referenceWorldMatrix,
        AssetLoadContext loadContext,
        string outputPath,
        in Size viewportSize,
        StandaloneCaptureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(primaryModel);

        ArgumentNullException.ThrowIfNull(referenceModel);

        CaptureCore(
            game,
            loadContext,
            outputPath,
            viewportSize,
            [
                new CaptureSubject(primaryModel, primaryWorldMatrix, null),
                new CaptureSubject(referenceModel, referenceWorldMatrix, null),
            ],
            options);
    }

    private static void CaptureCore(
        Game game,
        AssetLoadContext loadContext,
        string outputPath,
        in Size viewportSize,
        CaptureSubject[] subjects,
        StandaloneCaptureOptions? options)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (string.IsNullOrEmpty(outputPath))
        {
            throw new ArgumentException("Output path must not be null or empty.", nameof(outputPath));
        }

        foreach (var subject in subjects)
        {
            if (subject.Model.SubObjects.Length == 0)
            {
                throw new InvalidDataException(
                    $"Model '{subject.Model.Name}' has no sub-objects; there is nothing to capture.");
            }
        }

        // Fit the camera to the union of the transformed subject bounds, not raw model bounds.
        var points = new List<Vector3>();
        foreach (var subject in subjects)
        {
            AddTransformedCorners(points, ComputeEnclosingBoundingBox(subject.Model), subject.WorldMatrix);
        }

        var enclosingBounds = AxisAlignedBoundingBox.CreateFromPoints(points);

        var (cameraTarget, cameraDistance) = FitCamera(enclosingBounds);

        var inputMessageBuffer = new InputMessageBuffer();
        var width = (float)viewportSize.Width;
        var height = (float)viewportSize.Height;
        var cameraController = new ArcballCameraController(cameraTarget, cameraDistance);
        using var scene = new Scene3D(
            game,
            inputMessageBuffer,
            () => new Viewport(0, 0, width, height, 0, 1),
            cameraController,
            options?.Lighting ?? WorldLighting.CreateDefault(),
            isDiagnosticScene: true);
        using var renderPipeline = new RenderPipeline(game);
        if (options?.Background is { } background)
        {
            renderPipeline.ClearColor = new RgbaFloat(background.R, background.G, background.B, background.A);
        }

        using var renderTarget = new RenderTarget(game.GraphicsDevice);
        renderTarget.EnsureSize(viewportSize);

        var instances = CreateInstances(game, loadContext, subjects);

        void OnBuildingRenderList(object? sender, BuildingRenderListEventArgs e)
        {
            for (var i = 0; i < subjects.Length; i++)
            {
                instances[i].SetWorldMatrix(subjects[i].WorldMatrix);
                instances[i].BuildRenderList(
                    e.RenderList,
                    e.Camera,
                    true,
                    new MeshShaderResources.RenderItemConstantsPS
                    {
                        HouseColor = Vector3.One,
                        Opacity = 1.0f,
                        TintColor = Vector3.One,
                    });

                FlushMeshWorldConstants(game, instances[i]);
            }
        }

        renderPipeline.BuildingRenderList += OnBuildingRenderList;
        try
        {
            // Diagnostic scenes have no tactical view; apply the arcball camera directly.
            ((IEditorCameraController)cameraController).UpdateCamera(
                scene.Camera,
                default,
                TimeInterval.Zero);

            if (options?.Camera is { } explicitCamera)
            {
                var offset = new Vector3(
                    MathF.Cos(explicitCamera.ElevationRadians) * MathF.Cos(explicitCamera.OrbitRadians),
                    MathF.Cos(explicitCamera.ElevationRadians) * MathF.Sin(explicitCamera.OrbitRadians),
                    MathF.Sin(explicitCamera.ElevationRadians)) * explicitCamera.Distance;
                scene.Camera.SetLookAt(cameraTarget + offset, cameraTarget, Vector3.UnitZ);
                scene.Camera.FieldOfView = explicitCamera.FieldOfViewRadians;
            }

            // The default near plane (one terrain tile, 10 units) clips sub-10-unit models
            // such as unit-scale glTF fixtures; tighten it for diagnostic framing.
            scene.Camera.NearPlaneDistance = MathF.Max((enclosingBounds.Max - enclosingBounds.Min).Length() / 100f, 0.05f);

            renderPipeline.Execute(new RenderContext
            {
                ContentManager = game.ContentManager,

                // A fixed frame time keeps captures deterministic: time-driven effects like
                // cloud-shadow scrolling would otherwise advance between captures.
                GameTime = TimeInterval.Zero,
                GraphicsDevice = game.GraphicsDevice,
                RenderTarget = renderTarget.Framebuffer,
                Scene2D = null,
                Scene3D = scene,
            });

            FrameCapturer.CaptureToPng(game.GraphicsDevice, renderTarget.ColorTarget, outputPath);
        }
        finally
        {
            renderPipeline.BuildingRenderList -= OnBuildingRenderList;

            foreach (var instance in instances)
            {
                instance.Dispose();
            }
        }
    }

    private static List<ModelInstance> CreateInstances(
        Game game,
        AssetLoadContext loadContext,
        CaptureSubject[] subjects)
    {
        var instances = new List<ModelInstance>();

        try
        {
            foreach (var subject in subjects)
            {
                var instance = subject.Model.CreateInstance(loadContext);
                instances.Add(instance);
                MuzzleFlashBones.HideForStandaloneRender(instance, game.AssetStore.ObjectDefinitions);
                subject.ConfigureInstance?.Invoke(instance);
                instance.Update(TimeInterval.Zero);
            }

            return instances;
        }
        catch
        {
            foreach (var instance in instances)
            {
                instance.Dispose();
            }

            throw;
        }
    }

    private static void AddTransformedCorners(List<Vector3> points, in AxisAlignedBoundingBox bounds, in Matrix4x4 matrix)
    {
        points.Add(Vector3.Transform(new Vector3(bounds.Min.X, bounds.Min.Y, bounds.Min.Z), matrix));
        points.Add(Vector3.Transform(new Vector3(bounds.Max.X, bounds.Min.Y, bounds.Min.Z), matrix));
        points.Add(Vector3.Transform(new Vector3(bounds.Min.X, bounds.Max.Y, bounds.Min.Z), matrix));
        points.Add(Vector3.Transform(new Vector3(bounds.Max.X, bounds.Max.Y, bounds.Min.Z), matrix));
        points.Add(Vector3.Transform(new Vector3(bounds.Min.X, bounds.Min.Y, bounds.Max.Z), matrix));
        points.Add(Vector3.Transform(new Vector3(bounds.Max.X, bounds.Min.Y, bounds.Max.Z), matrix));
        points.Add(Vector3.Transform(new Vector3(bounds.Min.X, bounds.Max.Y, bounds.Max.Z), matrix));
        points.Add(Vector3.Transform(new Vector3(bounds.Max.X, bounds.Max.Y, bounds.Max.Z), matrix));
    }

    private readonly record struct CaptureSubject(
        Model Model,
        Matrix4x4 WorldMatrix,
        Action<ModelInstance>? ConfigureInstance);

    /// <summary>
    /// Uploads every mesh's world constants synchronously before the render passes execute,
    /// so the per-item update inside <see cref="ModelMeshInstance.OnBeforeRender"/> is skipped
    /// by its dirty check. Without this, hardware Vulkan drivers (RADV) can execute those
    /// inline command-list buffer updates after the draws that read them, rendering some
    /// meshes with stale world matrices; serializing drivers (llvmpipe) never expose the race.
    /// Captures serialize frames via WaitForIdle, so synchronous CPU writes are safe here.
    /// </summary>
    internal static void FlushMeshWorldConstants(Game game, ModelInstance modelInstance)
    {
        var model = modelInstance.Model;
        for (var i = 0; i < model.SubObjects.Length; i++)
        {
            var meshInstance = modelInstance.MeshInstances[i];
            if (meshInstance is null)
            {
                continue;
            }

            var world = modelInstance.AbsoluteBoneTransforms[model.SubObjects[i].Bone.Index];
            meshInstance.RenderItemConstantsBufferVS.Value.World = world;
            meshInstance.RenderItemConstantsBufferVS.Update(game.GraphicsDevice);
        }
    }

    /// <summary>
    /// Minimal data-only load context for captures that need no asset-store resolution.
    /// </summary>
    internal static AssetLoadContext CreateDataOnlyLoadContext()
    {
        var fileSystem = new Panopticon.IO.DiskFileSystem(System.IO.Path.GetTempPath());
        var assetStore = new AssetStore(
            SageGame.CncGenerals,
            fileSystem,
            "english",
            null,
            null,
            null,
            null,
            OnDemandAssetLoadStrategy.None);
        assetStore.PushScope();
        return new AssetLoadContext(fileSystem, "english", null, null, null, null, assetStore);
    }

    /// <summary>
    /// Merged sub-object bounds over accumulated rest-pose bone worlds; mirrors ModelView's
    /// enclosing-bounds computation for a freshly updated instance. Bones are ordered parent
    /// before child, so index order accumulates the full chain; sub-object bounds are
    /// bone-local.
    /// </summary>
    internal static AxisAlignedBoundingBox ComputeEnclosingBoundingBox(Model model)
    {
        var boneWorlds = new Matrix4x4[model.BoneHierarchy.Bones.Length];
        for (var i = 0; i < boneWorlds.Length; i++)
        {
            var bone = model.BoneHierarchy.Bones[i];
            boneWorlds[i] = bone.Parent is null
                ? bone.Transform.Matrix
                : bone.Transform.Matrix * boneWorlds[bone.Parent.Index];
        }

        var boundingBox = default(AxisAlignedBoundingBox);

        var first = true;
        foreach (var subObject in model.SubObjects)
        {
            var transformedBoundingBox = AxisAlignedBoundingBox.Transform(
                subObject.RenderObject.BoundingBox,
                boneWorlds[subObject.Bone.Index]);

            if (first)
            {
                boundingBox = transformedBoundingBox;
                first = false;
            }
            else
            {
                boundingBox = AxisAlignedBoundingBox.CreateMerged(boundingBox, transformedBoundingBox);
            }
        }

        if (first)
        {
            throw new InvalidDataException(
                $"Model '{model.Name}' has no sub-objects; there is nothing to capture.");
        }

        return boundingBox;
    }

    /// <summary>
    /// Camera fit used by the diagnostic ModelView: target the bounds center, distance the
    /// diagonal length.
    /// </summary>
    internal static (Vector3 Target, float Distance) FitCamera(in AxisAlignedBoundingBox bounds)
    {
        return (bounds.GetCenter(), Vector3.Distance(bounds.Min, bounds.Max));
    }
}
