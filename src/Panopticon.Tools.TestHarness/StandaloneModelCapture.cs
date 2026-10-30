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
    Mathematics.ColorRgbaF? Background = null,
    Mathematics.AxisAlignedBoundingBox? CameraFitBounds = null,
    bool GroundComparison = false);

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

    /// <summary>Bind-pose comparison: common ground, matched foot depth, unchanged scale.</summary>
    public static void CaptureGroundedPair(
        Game game,
        Model primaryModel,
        float primaryScale,
        Model referenceModel,
        AssetLoadContext loadContext,
        string outputPath,
        in Size viewportSize,
        StandaloneCaptureOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primaryScale);
        var primaryBounds = ComputeGeometryBounds(game, primaryModel, out var primaryFootDepth);
        var referenceBounds = ComputeGeometryBounds(game, referenceModel, out var referenceFootDepth);
        var scaled = AxisAlignedBoundingBox.Transform(primaryBounds, Matrix4x4.CreateScale(primaryScale));
        var gap = MathF.Max(scaled.Max.Z - scaled.Min.Z, referenceBounds.Max.Z - referenceBounds.Min.Z) * 0.3f;
        var referenceWorld = GroundTransform(referenceBounds, 1f, -(referenceBounds.Max.X - referenceBounds.Min.X + gap) / 2f);
        var primaryWorld = GroundTransform(primaryBounds, primaryScale, (scaled.Max.X - scaled.Min.X + gap) / 2f);
        referenceWorld.Translation += new Vector3(0, referenceBounds.GetCenter().Y - referenceFootDepth, 0);
        primaryWorld.Translation += new Vector3(0, (primaryBounds.GetCenter().Y - primaryFootDepth) * primaryScale, 0);
        CapturePair(game, primaryModel, primaryWorld, referenceModel, referenceWorld,
            loadContext, outputPath, viewportSize,
            (options ?? new StandaloneCaptureOptions()) with
            {
                GroundComparison = true,
                CameraFitBounds = AxisAlignedBoundingBox.CreateMerged(
                    AxisAlignedBoundingBox.Transform(primaryBounds, primaryWorld),
                    AxisAlignedBoundingBox.Transform(referenceBounds, referenceWorld)),
            });
    }

    private static AxisAlignedBoundingBox ComputeGeometryBounds(Game game, Model model, out float footDepth)
    {
        var points = new List<Vector3>();
        var bones = model.BoneHierarchy.Bones;
        var worlds = new Matrix4x4[bones.Length];
        for (var i = 0; i < bones.Length; i++)
        {
            worlds[i] = bones[i].Transform.Matrix * (bones[i].Parent is { } parent ? worlds[parent.Index] : Matrix4x4.Identity);
        }

        foreach (var subObject in model.SubObjects)
        {
            if (subObject.RenderObject.Hidden)
            {
                continue;
            }

            if (subObject.RenderObject is not ModelMesh mesh)
            {
                continue;
            }

            // Read back only for diagnostic captures; authored culling bounds can extend below the feet.
            var device = game.GraphicsDevice;
            using var staging = device.ResourceFactory.CreateBuffer(new BufferDescription(mesh.VertexBuffer.SizeInBytes, BufferUsage.Staging));
            using var commands = device.ResourceFactory.CreateCommandList();
            commands.Begin();
            commands.CopyBuffer(mesh.VertexBuffer, 0, staging, 0, mesh.VertexBuffer.SizeInBytes);
            commands.End();
            device.SubmitCommands(commands);
            device.WaitForIdle();
            var vertices = device.Map<MeshShaderResources.MeshVertex.Basic>(staging, MapMode.Read);
            try
            {
                for (uint i = 0; i < vertices.Count; i++)
                {
                    var vertex = vertices[i];
                    var position = mesh.Skinned
                        ? Skin(vertex.Position0, vertex.BoneIndex0, vertex.BoneWeight0)
                            + Skin(vertex.Position1, vertex.BoneIndex1, vertex.BoneWeight1)
                            + Skin(vertex.Position0, vertex.BoneIndex2, vertex.BoneWeight2)
                            + Skin(vertex.Position0, vertex.BoneIndex3, vertex.BoneWeight3)
                        : Vector3.Transform(vertex.Position0, worlds[subObject.Bone.Index]);
                    points.Add(position);
                }
            }
            finally
            {
                device.Unmap(staging);
            }
        }

        if (points.Count == 0)
        {
            throw new InvalidDataException($"Model '{model.Name}' has no visible mesh geometry to ground.");
        }

        var bounds = AxisAlignedBoundingBox.CreateFromPoints(points);
        var footBand = (bounds.Max.Z - bounds.Min.Z) * 0.01f;
        footDepth = float.PositiveInfinity;
        foreach (var point in points)
        {
            if (point.Z <= bounds.Min.Z + footBand)
            {
                footDepth = MathF.Min(footDepth, point.Y);
            }
        }

        return bounds;

        Vector3 Skin(Vector3 position, uint index, float weight)
        {
            return weight == 0f ? Vector3.Zero : Vector3.Transform(position, bones[index].InverseBindMatrix * worlds[index]) * weight;
        }
    }

    internal static Matrix4x4 GroundTransform(in AxisAlignedBoundingBox bounds, float scale, float centerX)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var center = bounds.GetCenter() * scale;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(
            centerX - center.X, -center.Y, -bounds.Min.Z * scale);
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
        // An explicit CameraFitBounds replaces the rest-pose-derived bounds, for subjects whose
        // rendered pose (animation, morphs) departs from the rest pose the default fit sees.
        AxisAlignedBoundingBox enclosingBounds;
        if (options?.CameraFitBounds is { } fitBounds)
        {
            enclosingBounds = fitBounds;
        }
        else
        {
            var points = new List<Vector3>();
            foreach (var subject in subjects)
            {
                AddTransformedCorners(points, ComputeEnclosingBoundingBox(subject.Model), subject.WorldMatrix);
            }

            enclosingBounds = AxisAlignedBoundingBox.CreateFromPoints(points);
        }

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

            // Execute's output pass already applies the sRGB transfer into the capture
            // target (verified: a (0.2, 0.4, 0.9) clear colour captures as (124, 170, 243)),
            // so the PNG holds display-ready values.
            FrameCapturer.CaptureToPng(game.GraphicsDevice, renderTarget.ColorTarget, outputPath);
            if (options?.GroundComparison == true)
            {
                // Project the real world ground, rather than aligning silhouettes after rendering.
                var ground = scene.Camera.WorldToScreenPoint(Vector3.Zero);
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(outputPath);
                var y = Math.Clamp((int)MathF.Round(ground.Y), 0, image.Height - 1);
                for (var x = 0; x < image.Width; x++)
                {
                    image[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(230, 230, 230, 255);
                }

                using var output = File.Create(outputPath);
                image.Save(output, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            }
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

            // Mirror ModelMesh.BuildRenderList's world selection: skinned meshes render at
            // the model world matrix (bone transforms reach them through the skinning
            // buffer), rigid meshes at their attached bone's absolute transform.
            var world = model.SubObjects[i].RenderObject is ModelMesh { Skinned: true }
                ? modelInstance.GetWorldMatrix()
                : modelInstance.AbsoluteBoneTransforms[model.SubObjects[i].Bone.Index];
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
    /// Rest bounds follow rendering: skinned bounds are model-space; rigid bounds are bone-local.
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
                subObject.RenderObject is ModelMesh { Skinned: true }
                    ? Matrix4x4.Identity
                    : boneWorlds[subObject.Bone.Index]);

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
