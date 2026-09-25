using System;
using System.IO;
using Panopticon.FileFormats.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifSceneGraphTests
{
    private static NifTestDataBuilder NestedGraphBuilder()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var extraType = builder.AddBlockType("NiStringExtraData");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root.nif", children: [1]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "hull",
            extraData: [3],
            flags: 0x10,
            translation: (1.5f, -2.25f, 3.0f),
            scale: 2.0f,
            children: [2, -1, -1]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "mast"));
        builder.AddBlock(extraType, NifTestBlocks.StringExtraData("UserPropBuffer", "NiOptimizeKeep"));
        return builder;
    }

    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    [Fact]
    public void NestedGraphRoundTripsNamesTransformsAndReferences()
    {
        var builder = NestedGraphBuilder();
        builder.AddTrailing(NifTestBlocks.Footer());
        var file = Read(builder.Build());

        Assert.Equal(4, file.Blocks.Count);
        Assert.Equal([0], file.Roots);

        var root = (NifNode)file.Blocks[0];
        Assert.Equal("root.nif", root.Name);
        Assert.Equal([1], root.ChildReferences);
        Assert.Empty(root.EffectReferences);
        Assert.Empty(root.ExtraDataReferences);
        Assert.Equal(-1, root.ControllerReference);
        Assert.Equal(-1, root.CollisionObjectReference);

        var hull = (NifNode)file.Blocks[1];
        Assert.Equal("hull", hull.Name);
        Assert.Equal(0x10, hull.Flags);
        Assert.Equal(new System.Numerics.Vector3(1.5f, -2.25f, 3.0f), hull.Translation);
        Assert.Equal(2.0f, hull.Scale);
        Assert.True(hull.Rotation.IsIdentity(1e-6f));
        Assert.Equal([3], hull.ExtraDataReferences);
        // Null child references are preserved verbatim.
        Assert.Equal([2, -1, -1], hull.ChildReferences);

        var extra = (NifStringExtraData)file.Blocks[3];
        Assert.Equal("UserPropBuffer", extra.Name);
        Assert.Equal("NiOptimizeKeep", extra.Data);
    }

    [Fact]
    public void FooterIsRejectedWhenWrong()
    {
        var builder = NestedGraphBuilder();
        builder.AddTrailing([2, 0, 0, 0, 0, 0, 0, 0]);
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("footer", ex.Message);
    }

    [Fact]
    public void TrailingBytesAfterFooterAreRejected()
    {
        var builder = NestedGraphBuilder();
        builder.AddTrailing(NifTestBlocks.Footer());
        builder.AddTrailing([1, 2, 3, 4]);
        Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
    }

    [Fact]
    public void NonZeroLeadingWordIsRejected()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, [1, 0, 0, 0]);
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("block 0", ex.Message);
    }

    [Fact]
    public void OutOfRangeChildReferenceFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [9]));
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("Child", ex.Message);
    }

    [Fact]
    public void OutOfRangePropertyReferenceFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(properties: [4]));
        builder.AddBlock(nodeType, NifTestBlocks.Node());
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("Property", ex.Message);
    }

    [Fact]
    public void ExtraDataReferenceToNonExtraBlockFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(extraData: [1]));
        builder.AddBlock(nodeType, NifTestBlocks.Node());
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("extra data", ex.Message);
    }

    [Fact]
    public void DuplicateParentFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [2]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [2]));
        builder.AddBlock(nodeType, NifTestBlocks.Node());
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("two parents", ex.Message);
    }

    [Fact]
    public void SelfChildReferenceFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [0]));
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("itself", ex.Message);
    }

    [Fact]
    public void ChildCycleFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        // 0 -> 1 -> 2 -> 0: no node is parentless, so the duplicate-parent
        // check cannot fire and the cycle detection must.
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [1]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [2]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [0]));
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("cycle", ex.Message);
    }

    [Fact]
    public void DisconnectedCycleFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root"));
        // 1 <-> 2 cycle with no path from the root.
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [2]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(children: [1]));
        builder.AddTrailing(NifTestBlocks.Footer());
        Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
    }

    [Fact]
    public void UnknownBlockTypeFailsNamingBlockAndIndex()
    {
        var builder = new NifTestDataBuilder();
        // NiMorphData is a real NIF block type with no C4c support.
        var morphType = builder.AddBlockType("NiMorphData");
        builder.AddBlock(morphType, [0, 0, 0, 0]);
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));
        Assert.Contains("block 0", ex.Message);
        Assert.Contains("NiMorphData", ex.Message);
    }

    [Fact]
    public void MultipleRootsFormAForest()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "a", children: [1]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "b"));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "c"));
        builder.AddTrailing(NifTestBlocks.Footer());
        var file = Read(builder.Build());
        Assert.Equal([0, 2], file.Roots);
    }
}
