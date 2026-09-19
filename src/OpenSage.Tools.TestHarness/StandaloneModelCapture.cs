using System;
using System.IO;
using System.Numerics;
using OpenSage.Content;
using OpenSage.Content.Loaders;
using OpenSage.Graphics;
using OpenSage.Graphics.Cameras;
using OpenSage.Graphics.Rendering;
using OpenSage.Graphics.Shaders;
using OpenSage.Input;
using OpenSage.Mathematics;
using OpenSage.Settings;
using Veldrid;

namespace OpenSage.Tools.TestHarness;

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
        in Size viewportSize)
    {
        if (game is null)
        {
            throw new ArgumentNullException(nameof(game));
        }

        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            throw new ArgumentException("Output path must not be null or empty.", nameof(outputPath));
        }

        if (model.SubObjects.Length == 0)
        {
            throw new InvalidDataException(
                $"Model '{model.Name}' has no sub-objects; there is nothing to capture.");
        }

        var enclosingBounds = ComputeEnclosingBoundingBox(model);
        var (cameraTarget, cameraDistance) = FitCamera(enclosingBounds);

        using var modelInstance = model.CreateInstance(loadContext);
        modelInstance.Update(TimeInterval.Zero);

        var inputMessageBuffer = new InputMessageBuffer();
        var width = (float)viewportSize.Width;
        var height = (float)viewportSize.Height;
        var cameraController = new ArcballCameraController(cameraTarget, cameraDistance);
        using var scene = new Scene3D(
            game,
            inputMessageBuffer,
            () => new Viewport(0, 0, width, height, 0, 1),
            cameraController,
            WorldLighting.CreateDefault(),
            isDiagnosticScene: true);
        using var renderPipeline = new RenderPipeline(game);
        using var renderTarget = new RenderTarget(game.GraphicsDevice);
        renderTarget.EnsureSize(viewportSize);

        void OnBuildingRenderList(object sender, BuildingRenderListEventArgs e)
        {
            modelInstance.SetWorldMatrix(Matrix4x4.Identity);
            modelInstance.BuildRenderList(
                e.RenderList,
                e.Camera,
                true,
                new MeshShaderResources.RenderItemConstantsPS
                {
                    HouseColor = Vector3.One,
                    Opacity = 1.0f,
                });
        }

        renderPipeline.BuildingRenderList += OnBuildingRenderList;
        try
        {
            // Diagnostic scenes have no tactical view; apply the arcball camera directly.
            ((IEditorCameraController)cameraController).UpdateCamera(
                scene.Camera,
                default,
                TimeInterval.Zero);

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
        }
    }

    /// <summary>
    /// Minimal data-only load context for captures that need no asset-store resolution.
    /// </summary>
    internal static AssetLoadContext CreateDataOnlyLoadContext()
    {
        var fileSystem = new OpenSage.IO.DiskFileSystem(System.IO.Path.GetTempPath());
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
    /// Merged sub-object bounds over rest-pose bone transforms; mirrors ModelView's
    /// enclosing-bounds computation for a freshly updated instance.
    /// </summary>
    internal static AxisAlignedBoundingBox ComputeEnclosingBoundingBox(Model model)
    {
        var boundingBox = default(AxisAlignedBoundingBox);

        var first = true;
        foreach (var subObject in model.SubObjects)
        {
            var transformedBoundingBox = AxisAlignedBoundingBox.Transform(
                subObject.RenderObject.BoundingBox,
                subObject.Bone.Transform.Matrix);

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
