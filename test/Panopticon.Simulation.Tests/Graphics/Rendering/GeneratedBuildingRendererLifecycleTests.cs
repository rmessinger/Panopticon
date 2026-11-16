using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing.Maps;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;
using Panopticon.Tests.Content.Importing;
using Veldrid;
using Xunit;
using Rectangle = Panopticon.Mathematics.Rectangle;
using RenderItem = Panopticon.Graphics.Rendering.RenderItem;
namespace Panopticon.Tests.Graphics.Rendering;

// Lifecycle coverage for GeneratedBuildingRenderer: constructor failure disposal, normal
// dispose idempotence, and the real FilterVisible culling path. Everything here runs on the
// headless TestGraphicsContext device (no window, no X11, no game installation).
// Border deferral: GeneratedMapPackageConverter.BuildLayout always writes border 0, but
// PackageReader forwards manifest.Layout.Border unvalidated and RuntimeBridge rejects only
// negative values, so a hand-edited package with border != 0 plus a buildings layer would
// reach the renderer. No test covers border != 0.
[Collection("GraphicsTests")]
public sealed class GeneratedBuildingRendererLifecycleTests
{
    [GraphicsFact]
    public void ConstructorFailureAtSegmentN_DisposesAllCreatedHandlesAndNeverPublishes()
    {
        using var graphics = new TestGraphicsContext();
        var game = new TestGraphicsGame(graphics);
        game.Panel.EnsureFrame(new Rectangle(0, 0, 160, 120));

        var layer = Layer(
            Chunk(0, 0, Segment(-8, -8, -10, -4, -4, -5)),
            Chunk(0, 1, Segment(-2, -8, -10, 2, -4, -5)),
            Chunk(1, 0, Segment(4, -8, -10, 8, -4, -5)));

        var createdHandles = new List<IDisposable>();
        var segmentsCreated = 0;

        GeneratedBuildingRenderer.TestSegmentHandlesCreated = handles =>
        {
            createdHandles.AddRange(handles);

            if (++segmentsCreated == 3)
            {
                throw new InjectedSegmentFailureException();
            }
        };

        try
        {
            Assert.Throws<InjectedSegmentFailureException>(() =>
                _ = new GeneratedBuildingRenderer(LoadContext(graphics), layer));
        }
        finally
        {
            GeneratedBuildingRenderer.TestSegmentHandlesCreated = null;
        }

        // Real disposal evidence: every owned handle captured at creation (3 segments x 6
        // handles) reports destroyed through its owning type, proving disposal at least once;
        // exactly-once rests on DisposableBase's single-shot dispose guard plus the one
        // AddDisposable registration per handle audited in GeneratedBuildingRenderer source.
        Assert.Equal(18, createdHandles.Count);
        Assert.All(createdHandles, handle => Assert.True(
            IsHandleDisposed(handle),
            $"{handle.GetType().Name} created before the injected failure was not disposed."));

        // The renderer was never attached, so raising the real RenderPipeline delegate
        // through a real Execute publishes zero items.
        using var pipeline = new RenderPipeline(game);
        var frame = RenderRealFrame(pipeline, game);
        Assert.Empty(frame.Opaque);
        Assert.Empty(frame.Shadow);
    }

    [GraphicsFact]
    public void DisposeUnsubscribesDisposesEverySegmentHandleOnceAndIsIdempotent()
    {
        using var graphics = new TestGraphicsContext();
        var game = new TestGraphicsGame(graphics);
        game.Panel.EnsureFrame(new Rectangle(0, 0, 160, 120));
        using var pipeline = new RenderPipeline(game);

        var layer = Layer(
            Chunk(0, 0, Segment(-8, -8, -10, -4, -4, -5)),
            Chunk(1, 1, Segment(4, 4, -10, 8, 8, -5)));

        var createdHandles = new List<IDisposable>();
        GeneratedBuildingRenderer.TestSegmentHandlesCreated = handles => createdHandles.AddRange(handles);

        GeneratedBuildingRenderer renderer;
        try
        {
            renderer = new GeneratedBuildingRenderer(LoadContext(graphics), layer);
        }
        finally
        {
            GeneratedBuildingRenderer.TestSegmentHandlesCreated = null;
        }

        Assert.Equal(12, createdHandles.Count);

        renderer.Attach(pipeline);

        var frame = RenderRealFrame(pipeline, game);
        Assert.Equal(2, frame.Opaque.Count);
        Assert.Equal(2, frame.Shadow.Count);
        Assert.All(frame.Opaque, item => Assert.False(item.IndexBuffer.IsDisposed));

        renderer.Dispose();

        var frameAfterDispose = RenderRealFrame(pipeline, game);
        Assert.Empty(frameAfterDispose.Opaque);
        Assert.Empty(frameAfterDispose.Shadow);

        renderer.Dispose();

        var frameAfterRepeat = RenderRealFrame(pipeline, game);
        Assert.Empty(frameAfterRepeat.Opaque);

        Assert.All(createdHandles, handle => Assert.True(
            IsHandleDisposed(handle),
            $"{handle.GetType().Name} owned by the renderer was not disposed."));
        Assert.All(frame.Opaque, item => Assert.True(
            item.IndexBuffer.IsDisposed,
            "Index buffer published before dispose was not disposed."));
    }

    [GraphicsFact]
    public void RealFilterVisibleKeepsCrossingSegmentWithCentroidOutsideAndCullsFullyOutside()
    {
        using var graphics = new TestGraphicsContext();
        var game = new TestGraphicsGame(graphics);
        game.Panel.EnsureFrame(new Rectangle(0, 0, 160, 120));
        using var pipeline = new RenderPipeline(game);

        // Segment 0 spans the frustum's east plane with its centroid outside; segment 1 is
        // fully outside. The AABBs are the real ones the renderer publishes in RenderItems.
        var layer = Layer(
            Chunk(0, 0, Segment(-1, -1, -10, 20, 1, -5)),
            Chunk(1, 1, Segment(30, -1, -10, 40, 1, -5)));

        using var renderer = new GeneratedBuildingRenderer(LoadContext(graphics), layer);
        renderer.Attach(pipeline);

        var frame = RenderRealFrame(pipeline, game);
        var crossing = frame.Opaque.Single(item => item.DebugName.Contains("chunk 0,0", StringComparison.Ordinal));
        var outside = frame.Opaque.Single(item => item.DebugName.Contains("chunk 1,1", StringComparison.Ordinal));

        var frustum = new BoundingFrustum(Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 2f,
            1f,
            0.1f,
            1_000_000f));

        // The conditions RenderItemCollection.Cull evaluates, verified on the real items:
        // a crossing box whose centroid is outside survives, a fully outside box does not.
        Assert.False(frustum.Contains(crossing.BoundingBox.GetCenter()));
        Assert.True(frustum.Intersects(crossing.BoundingBox));
        Assert.False(frustum.Intersects(outside.BoundingBox));

        // RenderPipeline.DoRenderPass invokes bucket.RenderItems.FilterVisible(frustum,
        // clippingPlane1, clippingPlane2, 128); batch size -1 runs the identical main-thread
        // cull loop so the result stays deterministic.
        var items = new RenderItemCollection();
        items.Add(crossing);
        items.Add(outside);
        items.SortAll();
        items.FilterVisible(frustum, null, null, -1);

        var visibleNames = items.CulledItemIndices
            .Select(index => items[index].DebugName)
            .ToArray();
        Assert.Contains(crossing.DebugName, visibleNames);
        Assert.DoesNotContain(outside.DebugName, visibleNames);

        // The only auxiliary condition on the path is the clipping-plane conjunction
        // (_culled[i] && BoundingBox.Intersects(plane) != Back); assert both sides of it.
        var keptByPlane = RebuildItems(crossing, outside);
        keptByPlane.FilterVisible(frustum, new Plane(Vector3.UnitZ, 12f), null, -1);
        Assert.Contains(
            crossing.DebugName,
            keptByPlane.CulledItemIndices.Select(index => keptByPlane[index].DebugName));

        var culledByPlane = RebuildItems(crossing, outside);
        culledByPlane.FilterVisible(frustum, new Plane(Vector3.UnitZ, 3f), null, -1);
        Assert.Empty(culledByPlane.CulledItemIndices);
    }

    private static RenderItemCollection RebuildItems(params RenderItem[] items)
    {
        var collection = new RenderItemCollection();
        foreach (var item in items)
        {
            collection.Add(item);
        }

        collection.SortAll();
        return collection;
    }

    private static (List<RenderItem> Opaque, List<RenderItem> Shadow) RenderRealFrame(
        RenderPipeline pipeline,
        TestGraphicsGame game)
    {
        var opaque = new List<RenderItem>();
        var shadow = new List<RenderItem>();

        void OnBuildingRenderList(object? sender, BuildingRenderListEventArgs args)
        {
            Collect(args.RenderList.Opaque.RenderItems, opaque);
            Collect(args.RenderList.Shadow.RenderItems, shadow);
        }

        pipeline.BuildingRenderList += OnBuildingRenderList;

        try
        {
            pipeline.Execute(new RenderContext
            {
                ContentManager = null,
                GraphicsDevice = game.GraphicsDevice,
                Scene3D = null,
                Scene2D = null,
                RenderTarget = game.Panel.Framebuffer,
                GameTime = TimeInterval.Zero,
            });
        }
        finally
        {
            pipeline.BuildingRenderList -= OnBuildingRenderList;
        }

        return (opaque, shadow);

        static void Collect(RenderItemCollection items, List<RenderItem> into)
        {
            for (var i = 0; i < items.Length; i++)
            {
                into.Add(items[i]);
            }
        }
    }

    private static bool IsHandleDisposed(IDisposable handle) => handle switch
    {
        DeviceBuffer buffer => buffer.IsDisposed,
        ResourceSet resourceSet => resourceSet.IsDisposed,
        ConstantBuffer<MeshShaderResources.RenderItemConstantsVS> vertexConstants => vertexConstants.Buffer.IsDisposed,
        ConstantBuffer<MeshShaderResources.RenderItemConstantsPS> pixelConstants => pixelConstants.Buffer.IsDisposed,
        _ => throw new InvalidOperationException($"Unhandled owned-handle type {handle.GetType().Name}."),
    };

    private static GraphicsLoadContext LoadContext(TestGraphicsContext graphics) =>
        new(
            graphics.Device,
            graphics.StandardResources,
            graphics.ShaderResources,
            graphics.ShaderSetStore);

    private static BuildingLayerSet Layer(params BuildingMeshChunk[] chunks) =>
        new([], chunks);

    private static BuildingMeshChunk Chunk(int chunkX, int chunkY, params BuildingMeshSegment[] segments) =>
        new(chunkX, chunkY, segments);

    private static BuildingMeshSegment Segment(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ) =>
        new(
            materialVariant: 0,
            vertices:
            [
                new BuildingVertex(0, 0, 0, 0, 0, -1),
                new BuildingVertex(1, 0, 0, 0, 0, -1),
                new BuildingVertex(0, 1, 0, 0, 0, -1),
            ],
            indices: [0u, 1u, 2u],
            minX,
            minY,
            minZ,
            maxX,
            maxY,
            maxZ);

    private sealed class InjectedSegmentFailureException : Exception
    {
    }

}
