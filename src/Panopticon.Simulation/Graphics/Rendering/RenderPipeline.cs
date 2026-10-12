using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Data.Map;
using Panopticon.Graphics.Rendering.Instancing;
using Panopticon.Graphics.Rendering.Shadows;
using Panopticon.Graphics.Rendering.Water;
using Panopticon.Graphics.Shaders;
using Panopticon.Gui;
using Panopticon.Mathematics;
using Veldrid;

namespace Panopticon.Graphics.Rendering;

internal sealed class RenderPipeline : DisposableBase
{
    public event EventHandler<Rendering2DEventArgs> Rendering2D;
    public event EventHandler<BuildingRenderListEventArgs> BuildingRenderList;

    /// <summary>
    /// Render-path spans per frame (B5): buildRenderList, shadowPass, forwardPass, and the
    /// render total, in that order. Mirrors LogicStepper.SystemSpan for the presentation side.
    /// </summary>
    public event Action<string, TimeSpan>? RenderSpan;

    private const int ParallelCullingBatchSize = 128;

    /// <summary>Per-frame render counters (B5); valid after Execute returns.</summary>
    public RenderStatistics Statistics { get; } = new();

    /// <summary>
    /// Frame instance stream (B5-2): reset at Execute start, appended by instanced batches
    /// in every pass, uploaded per batch immediately before its draw.
    /// </summary>
    private readonly InstancePool _instancePool;

    /// <summary>Render-target clear color; overridable for diagnostic captures.</summary>
    public RgbaFloat ClearColor { get; set; } = new RgbaFloat(105 / 255f, 105 / 255f, 105 / 255f, 1f);

    public static readonly OutputDescription GameOutputDescription = CreateGameOutputDescription(GraphicsSettings.Enhanced());

    /// <summary>
    /// Builds the output description pipelines render into. The colour format follows the
    /// colour space (G6): linear shading renders into a float intermediate and the single
    /// sRGB transfer happens in the output pass; Legacy keeps the UNorm swapchain format.
    /// The sample count follows the settings.
    /// </summary>
    public static OutputDescription CreateGameOutputDescription(GraphicsSettings settings) =>
        CreateGameOutputDescription(settings, GraphicsSettingsController.ToTextureSampleCount(settings.SampleCount));

    /// <summary>
    /// Builds the output description with an explicit sample count. Used by passes that share
    /// the settings formats but must stay single-sample regardless of the 3D scene pass (2D/UI).
    /// </summary>
    public static OutputDescription CreateGameOutputDescription(GraphicsSettings settings, TextureSampleCount sampleCount) => new(
        new OutputAttachmentDescription(PixelFormat.D32_Float_S8_UInt),
        new[] { new OutputAttachmentDescription(settings.ColourSpaceMode == ColourSpaceMode.Linear
            ? PixelFormat.R16_G16_B16_A16_Float
            : PixelFormat.B8_G8_R8_A8_UNorm) },
        sampleCount);

    private readonly RenderList _renderList;

    private readonly CommandList _commandList;

    private readonly GraphicsLoadContext _loadContext;
    private readonly GlobalShaderResources _globalShaderResources;
    private readonly GlobalShaderResourceData _globalShaderResourceData;
    private readonly MeshDepthShaderResources _meshDepthShaderResources;

    internal readonly DrawingContext2D DrawingContext;

    private readonly ShadowMapRenderer _shadowMapRenderer;
    private readonly WaterMapRenderer _waterMapRenderer;

    private Texture _intermediateDepthBuffer;
    private Texture _intermediateTexture;
    private Framebuffer _intermediateFramebuffer;

    /// <summary>
    /// Single-sample colour target the multisampled 3D pass resolves into before 2D/UI draws.
    /// Null at sample count 1, where the intermediate texture is used directly.
    /// </summary>
    private Texture _compositionTexture;
    private Texture _compositionDepthBuffer;
    private Framebuffer _compositionFramebuffer;

    private readonly TextureCopier? _textureCopier;
    private readonly OutputColourPass? _outputColourPass;

    public Texture ShadowMap => _shadowMapRenderer.ShadowMap;
    public Texture ReflectionMap => _waterMapRenderer.ReflectionMap;
    public Texture RefractionMap => _waterMapRenderer.RefractionMap;

    public int RenderedObjectsOpaque { get; private set; }
    public int RenderedObjectsTransparent { get; private set; }

    private readonly IGame _game;

    public RenderPipeline(IGame game)
    {
        _game = game;

        _renderList = new RenderList();

        var graphicsDevice = game.GraphicsDevice;

        _loadContext = game.GraphicsLoadContext;

        _globalShaderResources = game.GraphicsLoadContext.ShaderResources.Global;
        _globalShaderResourceData = AddDisposable(new GlobalShaderResourceData(game.GraphicsDevice, _globalShaderResources, game.GraphicsLoadContext.StandardGraphicsResources));
        _meshDepthShaderResources = game.GraphicsLoadContext.ShaderResources.MeshDepth;

        _commandList = AddDisposable(graphicsDevice.ResourceFactory.CreateCommandList());

        DrawingContext = AddDisposable(new DrawingContext2D(
            game.ContentManager,
            game.GraphicsLoadContext,
            BlendStateDescription.SingleAlphaBlend,
            CreateGameOutputDescription(game.GraphicsSettings, TextureSampleCount.Count1),
            decodeLinearTints: game.GraphicsSettings.ColourSpaceMode == ColourSpaceMode.Linear));

        _shadowMapRenderer = AddDisposable(new ShadowMapRenderer(game.GraphicsDevice));
        _waterMapRenderer = AddDisposable(new WaterMapRenderer(game.AssetStore, _loadContext, game.GraphicsDevice, game.GraphicsLoadContext.ShaderResources.Global));
        _instancePool = AddDisposable(new InstancePool(game.GraphicsDevice));

        if (game.GraphicsSettings.ColourSpaceMode == ColourSpaceMode.Linear)
        {
            // Linear mode defers the sRGB transfer to a dedicated output pass; Legacy keeps
            // the straight copier blit.
            _outputColourPass = AddDisposable(new OutputColourPass(
                game,
                game.Panel.OutputDescription));
        }
        else
        {
            _textureCopier = AddDisposable(new TextureCopier(
                game,
                game.Panel.OutputDescription));
        }
    }

    private void EnsureIntermediateFramebuffer(GraphicsDevice graphicsDevice, Framebuffer target)
    {
        // The shader-set store description is authoritative: pipelines are built once with it,
        // so the intermediate attachments must always match it, even if the settings instance
        // is swapped later in the session (a count change requires a new session). The colour
        // format follows the store too: the linear colour space (G6) renders 3D into a float
        // intermediate and defers the sRGB transfer to the output pass.
        var sampleCount = _loadContext.ShaderSetStore.OutputDescription.SampleCount;
        var colourFormat = _loadContext.ShaderSetStore.OutputDescription.ColorAttachments[0].Format;

        if (_intermediateDepthBuffer != null
            && _intermediateDepthBuffer.Width == target.Width
            && _intermediateDepthBuffer.Height == target.Height
            && _intermediateDepthBuffer.SampleCount == sampleCount)
        {
            return;
        }

        RemoveAndDispose(ref _intermediateDepthBuffer);
        RemoveAndDispose(ref _intermediateTexture);
        RemoveAndDispose(ref _intermediateFramebuffer);
        RemoveAndDispose(ref _compositionTexture);
        RemoveAndDispose(ref _compositionDepthBuffer);
        RemoveAndDispose(ref _compositionFramebuffer);

        _intermediateDepthBuffer = AddDisposable(graphicsDevice.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(target.Width, target.Height, 1, 1, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil, sampleCount)));

        _intermediateTexture = AddDisposable(graphicsDevice.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(target.Width, target.Height, 1, 1, colourFormat, TextureUsage.RenderTarget | TextureUsage.Sampled, sampleCount)));

        _intermediateFramebuffer = AddDisposable(graphicsDevice.ResourceFactory.CreateFramebuffer(
            new FramebufferDescription(_intermediateDepthBuffer, _intermediateTexture)));

        if (sampleCount != TextureSampleCount.Count1)
        {
            // 2D/UI composites on a single-sample target; the multisampled 3D colour is
            // resolved into it before any 2D drawing each frame. The depth attachment is
            // never written by 2D pipelines but must exist for framebuffer compatibility.
            _compositionDepthBuffer = AddDisposable(graphicsDevice.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(target.Width, target.Height, 1, 1, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil)));

            _compositionTexture = AddDisposable(graphicsDevice.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(target.Width, target.Height, 1, 1, colourFormat, TextureUsage.RenderTarget | TextureUsage.Sampled)));

            _compositionFramebuffer = AddDisposable(graphicsDevice.ResourceFactory.CreateFramebuffer(
                new FramebufferDescription(_compositionDepthBuffer, _compositionTexture)));
        }
    }

    /// <summary>
    /// Applies effective anisotropy and mip bias without a restart: recreates the world
    /// sampler, rebuilds every set that binds it, then retires the old sampler once no
    /// live set references it (WI-G7-02).
    /// </summary>
    internal void ApplySamplerSettings(GraphicsSettings effective)
    {
        var standardGraphicsResources = _loadContext.StandardGraphicsResources;

        if (!standardGraphicsResources.WorldSamplerNeedsRebuild(effective))
        {
            return;
        }

        var worldSampler = standardGraphicsResources.RecreateWorldSampler(effective);

        _loadContext.ShaderResources.RebuildWorldSamplerSets(worldSampler);
        _globalShaderResourceData.InvalidateForwardPassResourceSet();
        _waterMapRenderer.ApplyWorldSampler(worldSampler);
        _game.Scene3D?.Terrain?.ApplyWorldSampler(worldSampler);

        standardGraphicsResources.DisposeRetiredWorldSampler();
    }

    public void Execute(RenderContext context)
    {
        var renderStartTimestamp = Stopwatch.GetTimestamp();

        RenderedObjectsOpaque = 0;
        RenderedObjectsTransparent = 0;
        Statistics.Reset();
        _instancePool.Reset();

        EnsureIntermediateFramebuffer(context.GraphicsDevice, context.RenderTarget);

        _renderList.Clear();
        _globalShaderResourceData.BeginFrame();

        var buildListStartTimestamp = Stopwatch.GetTimestamp();

        context.Scene3D?.BuildRenderList(
            _renderList,
            context.Scene3D.Camera,
            context.GameTime);

        BuildingRenderList?.Invoke(this, new BuildingRenderListEventArgs(
            _renderList,
            context.Scene3D?.Camera,
            context.GameTime));

        Statistics.ItemsOpaque = _renderList.Opaque.RenderItems.Length;
        Statistics.ItemsTransparent = _renderList.Transparent.RenderItems.Length;
        Statistics.ItemsShadow = _renderList.Shadow.RenderItems.Length;
        Statistics.ItemsWater = _renderList.Water.RenderItems.Length;

        RaiseSpan(RenderSpanNames.BuildRenderList, buildListStartTimestamp);

        _commandList.Begin();

        if (context.Scene3D != null)
        {
            _commandList.PushDebugGroup("3D Scene");
            Render3DScene(_commandList, context.Scene3D, context);
            _commandList.PopDebugGroup();

            if (_compositionFramebuffer != null)
            {
                // Multisampled frame: resolve the 3D colour before 2D draws, which composite
                // on the single-sample target and would otherwise sample an unresolved texture.
                _commandList.PushDebugGroup("MSAA Resolve");
                _commandList.ResolveTexture(_intermediateTexture, _compositionTexture);
                _commandList.SetFramebuffer(_compositionFramebuffer);
                _commandList.PopDebugGroup();
            }
        }
        else
        {
            if (_compositionFramebuffer != null)
            {
                // Nothing 3D is drawn; clear the composition target directly.
                _commandList.SetFramebuffer(_compositionFramebuffer);
            }
            else
            {
                _commandList.SetFramebuffer(_intermediateFramebuffer);
            }

            _commandList.ClearColorTarget(0, ClearColor);
        }

        // GUI and camera-dependent 2D elements
        {
            _commandList.PushDebugGroup("2D Scene");

            DrawingContext.Begin(
                _commandList,
                _loadContext.StandardGraphicsResources.LinearClampSampler,
                new SizeF(context.RenderTarget.Width, context.RenderTarget.Height),
                context.GameTime);

            context.Scene3D?.Render(DrawingContext);
            context.Scene2D?.Render(DrawingContext);

            _shadowMapRenderer.DrawDebugOverlay(
                context.Scene3D,
                DrawingContext);

            Rendering2D?.Invoke(this, new Rendering2DEventArgs(DrawingContext));

            DrawingContext.End();

            _commandList.PopDebugGroup();
        }

        _commandList.End();

        context.GraphicsDevice.SubmitCommands(_commandList);

        if (_outputColourPass != null)
        {
            _outputColourPass.Execute(
                _compositionTexture ?? _intermediateTexture,
                context.RenderTarget);
        }
        else
        {
            _textureCopier!.Execute(
                _compositionTexture ?? _intermediateTexture,
                context.RenderTarget);
        }

        RaiseSpan(RenderSpanNames.Render, renderStartTimestamp);
    }

    private static class RenderSpanNames
    {
        public const string BuildRenderList = "buildRenderList";
        public const string ShadowPass = "shadowPass";
        public const string ForwardPass = "forwardPass";
        public const string Render = "render";
    }

    private void RaiseSpan(string name, long startTimestamp)
    {
        if (RenderSpan is { } sink)
        {
            sink(name, TimeSpan.FromTicks((Stopwatch.GetTimestamp() - startTimestamp)
                * TimeSpan.TicksPerSecond / Stopwatch.Frequency));
        }
    }

    private void Render3DScene(
        CommandList commandList,
        IScene3D scene,
        RenderContext context)
    {
        Texture cloudTexture;
        if (scene.Lighting.TimeOfDay != TimeOfDay.Night
            && scene.Lighting.EnableCloudShadows
            && scene.Terrain != null)
        {
            cloudTexture = scene.Terrain.CloudTexture;
        }
        else
        {
            cloudTexture = _loadContext.StandardGraphicsResources.SolidWhiteTexture;
        }

        // Shadow map passes.

        commandList.PushDebugGroup("Shadow pass");

        var shadowStartTimestamp = Stopwatch.GetTimestamp();

        _shadowMapRenderer.RenderShadowMap(
            scene,
            context.GraphicsDevice,
            commandList,
            (framebuffer, lightBoundingFrustum) =>
            {
                commandList.SetFramebuffer(framebuffer);

                commandList.ClearDepthStencil(1);

                commandList.SetFullViewports();

                var shadowViewProjection = lightBoundingFrustum.Matrix;
                _globalShaderResourceData.UpdateGlobalConstantBuffers(commandList, context, shadowViewProjection, null, null);

                DoRenderPass(context, commandList, _renderList.Shadow, lightBoundingFrustum, null);
            });

        commandList.PopDebugGroup();

        RaiseSpan(RenderSpanNames.ShadowPass, shadowStartTimestamp);

        // Standard pass.

        commandList.PushDebugGroup("Forward pass");

        var forwardStartTimestamp = Stopwatch.GetTimestamp();

        var forwardPassResourceSet = _globalShaderResourceData.GetForwardPassResourceSet(
            cloudTexture,
            _shadowMapRenderer.ShadowConstantsPSBuffer,
            _shadowMapRenderer.ShadowMap);

        commandList.SetFramebuffer(_intermediateFramebuffer);

        _globalShaderResourceData.UpdateGlobalConstantBuffers(commandList, context, scene.Camera.ViewProjection, null, null);

        commandList.ClearColorTarget(0, ClearColor);
        commandList.ClearDepthStencil(1);

        commandList.SetFullViewports();

        var standardPassCameraFrustum = scene.Camera.BoundingFrustum;

        commandList.PushDebugGroup("Opaque");
        RenderedObjectsOpaque += DoRenderPass(context, commandList, _renderList.Opaque, standardPassCameraFrustum, forwardPassResourceSet);
        commandList.PopDebugGroup();

        commandList.PushDebugGroup("Transparent");
        RenderedObjectsTransparent = DoRenderPass(context, commandList, _renderList.Transparent, standardPassCameraFrustum, forwardPassResourceSet);
        commandList.PopDebugGroup();

        scene.RenderScene.Render(commandList, _globalShaderResourceData.GlobalConstantsResourceSet, forwardPassResourceSet);

        commandList.PushDebugGroup("Water");
        DoRenderPass(context, commandList, _renderList.Water, standardPassCameraFrustum, forwardPassResourceSet);
        commandList.PopDebugGroup();

        commandList.PopDebugGroup();

        scene.Game.Scripting.CameraFadeOverlay.Render(
            commandList,
            new SizeF(context.RenderTarget.Width, context.RenderTarget.Height));

        RaiseSpan(RenderSpanNames.ForwardPass, forwardStartTimestamp);
    }

    private void CalculateWaterShaderMap(IScene3D scene, RenderContext context, CommandList commandList, RenderItem renderItem, ResourceSet forwardPassResourceSet)
    {
        _waterMapRenderer.RenderWaterShaders(
            scene,
            context.GraphicsDevice,
            commandList,
            (reflectionFramebuffer, refractionFramebuffer) =>
            {
                var camera = scene.Camera;
                var clippingOffset = scene.Waters.ClippingOffset;
                var originalFarPlaneDistance = camera.FarPlaneDistance;
                var pivot = renderItem.World.Translation.Y;

                if (refractionFramebuffer != null)
                {
                    commandList.PushDebugGroup("Refraction");
                    camera.FarPlaneDistance = scene.Waters.RefractionRenderDistance;

                    var clippingPlaneTop = new Plane(-Vector3.UnitZ, pivot + clippingOffset);

                    var transparentWaterDepth = scene.AssetLoadContext.AssetStore.WaterTransparency.Current.TransparentWaterDepth;
                    var clippingPlaneBottom = new Plane(Vector3.UnitZ, -pivot + transparentWaterDepth);

                    // Render normal scene for water refraction shader
                    _globalShaderResourceData.UpdateGlobalConstantBuffers(commandList, context, camera.ViewProjection, clippingPlaneTop.AsVector4(), clippingPlaneBottom.AsVector4());

                    commandList.SetFramebuffer(refractionFramebuffer);

                    commandList.ClearColorTarget(0, ClearColor);
                    commandList.ClearDepthStencil(1);

                    commandList.SetFullViewports();

                    RenderedObjectsOpaque += DoRenderPass(context, commandList, _renderList.Opaque, camera.BoundingFrustum, forwardPassResourceSet, clippingPlaneTop, clippingPlaneBottom, useSingleSamplePipelines: true);
                    commandList.PopDebugGroup();
                }

                if (reflectionFramebuffer != null)
                {
                    commandList.PushDebugGroup("Reflection");
                    camera.FarPlaneDistance = scene.Waters.ReflectionRenderDistance;
                    var clippingPlane = new Plane(Vector3.UnitZ, -pivot - clippingOffset);

                    // TODO: Improve rendering speed somehow?
                    // ------------------- Used for creating stencil mask -------------------
                    _globalShaderResourceData.UpdateGlobalConstantBuffers(commandList, context, camera.ViewProjection, clippingPlane.AsVector4(), null);

                    commandList.SetFramebuffer(reflectionFramebuffer);
                    commandList.ClearColorTarget(0, ClearColor);
                    commandList.ClearDepthStencil(1);

                    // -----------------------------------------------------------------------

                    // Render inverted scene for water reflection shader
                    camera.SetMirrorX(pivot);
                    _globalShaderResourceData.UpdateGlobalConstantBuffers(commandList, context, camera.ViewProjection, clippingPlane.AsVector4(), null);

                    //commandList.SetFramebuffer(reflectionFramebuffer);
                    commandList.ClearColorTarget(0, ClearColor);
                    //commandList.ClearDepthStencil(1);

                    commandList.SetFullViewports();

                    RenderedObjectsOpaque += DoRenderPass(context, commandList, _renderList.Opaque, camera.BoundingFrustum, forwardPassResourceSet, clippingPlane, useSingleSamplePipelines: true);

                    camera.SetMirrorX(pivot);
                    commandList.PopDebugGroup();
                }

                if (reflectionFramebuffer != null || refractionFramebuffer != null)
                {
                    camera.FarPlaneDistance = originalFarPlaneDistance;
                    _globalShaderResourceData.UpdateGlobalConstantBuffers(commandList, context, camera.ViewProjection, null, null);

                    // Reset the render item pipeline
                    commandList.SetFramebuffer(_intermediateFramebuffer);
                    commandList.InsertDebugMarker("Setting pipeline");
                    commandList.SetPipeline(renderItem.Material.Pipeline);
                }
            });
    }

    private int DoRenderPass(
        RenderContext context,
        CommandList commandList,
        RenderBucket bucket,
        BoundingFrustum cameraFrustum,
        ResourceSet forwardPassResourceSet,
        in Plane? clippingPlane1 = null,
        in Plane? clippingPlane2 = null,
        bool useSingleSamplePipelines = false)
    {
        // TODO: Make culling batch size configurable at runtime
        bucket.RenderItems.CullAndSort(cameraFrustum, clippingPlane1, clippingPlane2, ParallelCullingBatchSize);

        if (bucket.RenderItems.CulledItemIndices.Count == 0)
        {
            return 0;
        }

        switch (bucket.RenderItemName)
        {
            case "Shadow":
                Statistics.VisibleShadow += bucket.RenderItems.CulledItemIndices.Count;
                break;
            case "Opaque":
                // Water-map re-passes (single-sample) re-cull this bucket with extra clipping
                // planes and short far planes; the main pass is the meaningful visible count.
                if (!useSingleSamplePipelines)
                {
                    Statistics.VisibleOpaque = bucket.RenderItems.CulledItemIndices.Count;
                }

                break;
            case "Transparent":
                Statistics.VisibleTransparent = bucket.RenderItems.CulledItemIndices.Count;
                break;
        }

        int? lastRenderItemIndex = null;
        var indices = bucket.RenderItems.CulledItemIndices;

        var itemIndex = 0;
        while (itemIndex < indices.Count)
        {
            ref var renderItem = ref bucket.RenderItems[indices[itemIndex]];

            // B5-2: water-map re-passes render into single-sample targets and keep the
            // per-item path (instanced pipelines target the multisampled store outputs).
            if (RenderScaleOptions.Instancing
                && !useSingleSamplePipelines
                && renderItem.InstancingEligible
                && renderItem.Material.InstancedPipeline is not null)
            {
                itemIndex = DrawInstancedRun(
                    commandList,
                    bucket,
                    indices,
                    itemIndex,
                    forwardPassResourceSet,
                    ref lastRenderItemIndex);
                continue;
            }

            commandList.PushDebugGroup($"Render item: {renderItem.DebugName}");

            var passResourceSet = (bucket.RenderItemName == "Shadow")
                ? null
                : forwardPassResourceSet;

            // Water reflection/refraction targets stay single-sample even under MSAA, so those
            // passes swap in the per-material single-sample pipeline variant.
            var pipeline = renderItem.Material.Pipeline;

            if (useSingleSamplePipelines && renderItem.Material.SingleSamplePipeline != null)
            {
                pipeline = renderItem.Material.SingleSamplePipeline;
            }

            var newMaterial = true;
            if (lastRenderItemIndex != null)
            {
                var lastMaterial = bucket.RenderItems[lastRenderItemIndex.Value].Material;

                var lastPipeline = useSingleSamplePipelines && lastMaterial.SingleSamplePipeline != null
                    ? lastMaterial.SingleSamplePipeline
                    : lastMaterial.Pipeline;

                newMaterial = lastPipeline != pipeline;
            }

            if (newMaterial)
            {
                commandList.InsertDebugMarker("Setting pipeline");
                commandList.SetPipeline(pipeline);
                SetGlobalResources(commandList, passResourceSet);
            }

            if (bucket.RenderItemName == "Water")
            {
                CalculateWaterShaderMap(context.Scene3D, context, commandList, renderItem, forwardPassResourceSet);

                SetGlobalResources(commandList, passResourceSet);
                commandList.SetGraphicsResourceSet(2, _waterMapRenderer.ResourceSetForRendering);
            }

            renderItem.BeforeRenderCallback.Invoke(commandList, renderItem);

            if (renderItem.Material.MaterialResourceSet != null)
            {
                commandList.SetGraphicsResourceSet(2, renderItem.Material.MaterialResourceSet);
            }

            commandList.SetIndexBuffer(renderItem.IndexBuffer, IndexFormat.UInt16);
            Statistics.DrawCalls++;
            commandList.DrawIndexed(
                renderItem.IndexCount,
                1,
                renderItem.StartIndex,
                0,
                0);

            lastRenderItemIndex = indices[itemIndex];
            itemIndex++;

            commandList.PopDebugGroup();
        }

        return bucket.RenderItems.CulledItemIndices.Count;
    }

    /// <summary>
    /// B5-2: draws one maximal run of instancing-eligible items that share pipeline, index
    /// buffer, and index range, appending their payloads to the frame instance stream and
    /// issuing a single instanced draw. Returns the item index after the run.
    /// </summary>
    private int DrawInstancedRun(
        CommandList commandList,
        RenderBucket bucket,
        IReadOnlyList<int> indices,
        int itemIndex,
        ResourceSet forwardPassResourceSet,
        ref int? lastRenderItemIndex)
    {
        ref var first = ref bucket.RenderItems[indices[itemIndex]];

        var startInstance = _instancePool.Append(first.InstancePayload);
        var runInstances = 1;
        var scan = itemIndex + 1;
        while (scan < indices.Count)
        {
            ref var next = ref bucket.RenderItems[indices[scan]];
            if (!next.InstancingEligible
                || next.Material.InstancedPipeline is null
                || !first.JoinsInstancedRunWith(next))
            {
                break;
            }

            _instancePool.Append(next.InstancePayload);
            runInstances++;
            scan++;
        }

        var passResourceSet = (bucket.RenderItemName == "Shadow")
            ? null
            : forwardPassResourceSet;

        var pipeline = first.Material.InstancedPipeline!;

        commandList.PushDebugGroup($"Instanced run: {first.DebugName} x{runInstances}");
        commandList.InsertDebugMarker("Setting pipeline");
        commandList.SetPipeline(pipeline);
        SetGlobalResources(commandList, passResourceSet);

        if (first.Material.MaterialResourceSet != null)
        {
            commandList.SetGraphicsResourceSet(2, first.Material.MaterialResourceSet);
        }

        _instancePool.UploadRange(commandList, startInstance, runInstances);
        _instancePool.BindItemSet(commandList, first.Material.InstancedItemLayout!, first.MeshConstantsBuffer!, (uint)startInstance);

        // The per-item path binds these inside BeforeRenderCallback (ModelMeshPart.BeforeRender);
        // a run skips callbacks, so it binds its own (all instances share the part's buffers).
        commandList.SetVertexBuffer(0, first.VertexBuffer!);
        if (first.TexCoordVertexBuffer != null)
        {
            commandList.SetVertexBuffer(1, first.TexCoordVertexBuffer);
        }

        commandList.SetIndexBuffer(first.IndexBuffer, IndexFormat.UInt16);
        Statistics.DrawCalls++;
        Statistics.InstancedDrawCalls++;
        Statistics.InstancedBatches++;
        Statistics.InstancesDrawn += runInstances;
        commandList.DrawIndexed(
            first.IndexCount,
            (uint)runInstances,
            first.StartIndex,
            0,
            0);

        // The next per-item draw must rebind its pipeline and set 3.
        lastRenderItemIndex = null;
        commandList.PopDebugGroup();
        return scan;
    }

    private void SetGlobalResources(CommandList commandList, ResourceSet passResourceSet)
    {
        commandList.SetGraphicsResourceSet(0, _globalShaderResourceData.GlobalConstantsResourceSet);

        if (passResourceSet != null)
        {
            commandList.SetGraphicsResourceSet(1, passResourceSet);
        }
        else
        {
            // Shadow/depth pass: bind empty resource sets for any unused pipeline slots
            // to satisfy Metal's requirement that all declared slots are bound.
            var emptyResourceSets = _meshDepthShaderResources.EmptyResourceSets;
            for (var i = 1; i < emptyResourceSets.Length; i++)
            {
                if (emptyResourceSets[i] != null)
                {
                    commandList.SetGraphicsResourceSet((uint)i, emptyResourceSets[i]);
                }
            }
        }
    }
}
