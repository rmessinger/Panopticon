using System;
using System.Numerics;
using Panopticon.Graphics.Rendering;
using Panopticon.Mathematics;
using Panopticon.Tests.Content.Importing;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics.Rendering;

/// <summary>
/// B5-3: one sort per frame, order-preserving visible filtering per pass. The collection's
/// visible output must be the sorted-by-key order filtered by the frustum, and identical
/// across passes with the same frustum.
/// </summary>
[Collection("GraphicsTests")]
public sealed class RenderItemCollectionSortFilterTests
{
    [GraphicsFact]
    public void FilterVisible_ReturnsSortedOrder_NotInsertionOrder()
    {
        using var graphics = new TestGraphicsContext();
        var material = graphics.ShaderResources.MeshDepth.Material;
        var collection = new RenderItemCollection();

        // Insert with descending part identity so insertion order differs from key order.
        collection.Add(MakeItem(material, partId: 9, box: BigBox()));
        collection.Add(MakeItem(material, partId: 5, box: BigBox()));
        collection.Add(MakeItem(material, partId: 7, box: BigBox()));

        collection.SortAll();
        collection.FilterVisible(AllVisibleFrustum(), null, null, batchSize: -1);

        var visible = collection.CulledItemIndices;
        Assert.Equal(3, visible.Count);
        Assert.Equal(5, collection[visible[0]].Key & 0xFFFF);
        Assert.Equal(7, collection[visible[1]].Key & 0xFFFF);
        Assert.Equal(9, collection[visible[2]].Key & 0xFFFF);
    }

    [GraphicsFact]
    public void FilterVisible_SameFrustumTwice_ProducesIdenticalOutput()
    {
        using var graphics = new TestGraphicsContext();
        var material = graphics.ShaderResources.MeshDepth.Material;
        var collection = new RenderItemCollection();

        for (var i = 0; i < 32; i++)
        {
            collection.Add(MakeItem(material, partId: (i * 13) % 64, box: BigBox()));
        }

        collection.SortAll();

        collection.FilterVisible(AllVisibleFrustum(), null, null, batchSize: -1);
        var first = new System.Collections.Generic.List<int>(collection.CulledItemIndices);
        collection.FilterVisible(AllVisibleFrustum(), null, null, batchSize: 8);
        var second = new System.Collections.Generic.List<int>(collection.CulledItemIndices);

        Assert.Equal(first, second);
        Assert.Equal(32, second.Count);
    }

    [GraphicsFact]
    public void FilterVisible_FrustumExclusion_KeepsRelativeOrder()
    {
        using var graphics = new TestGraphicsContext();
        var material = graphics.ShaderResources.MeshDepth.Material;
        var collection = new RenderItemCollection();

        collection.Add(MakeItem(material, partId: 9, box: BigBox()));
        collection.Add(MakeItem(material, partId: 5, box: FarAwayBox()));
        collection.Add(MakeItem(material, partId: 7, box: BigBox()));

        collection.SortAll();
        collection.FilterVisible(AllVisibleFrustum(), null, null, batchSize: -1);

        var visible = collection.CulledItemIndices;
        Assert.Equal(2, visible.Count);
        Assert.Equal(7, collection[visible[0]].Key & 0xFFFF);
        Assert.Equal(9, collection[visible[1]].Key & 0xFFFF);
    }

    private static RenderItem MakeItem(Panopticon.Rendering.Material material, int partId, in AxisAlignedBoundingBox box) =>
        new(
            $"part{partId}",
            material,
            box,
            Matrix4x4.Identity,
            startIndex: 0,
            indexCount: 6,
            indexBuffer: null!,
            Stub,
            instancingEligible: true,
            default,
            meshConstantsBuffer: null,
            partId);

    private static void Stub(CommandList commandList, in RenderItem renderItem)
    {
    }

    private static AxisAlignedBoundingBox BigBox() => new(new Vector3(-10f, -10f, -10f), new Vector3(10f, 10f, 10f));

    private static AxisAlignedBoundingBox FarAwayBox() => new(new Vector3(900_000f, 900_000f, 0f), new Vector3(900_010f, 900_010f, 10f));

    private static BoundingFrustum AllVisibleFrustum() => new(Matrix4x4.CreatePerspectiveFieldOfView(
        MathF.PI / 2f,
        1f,
        0.1f,
        1_000_000f));
}
