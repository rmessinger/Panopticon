using System;
using System.IO;
using System.Linq;
using Panopticon.FileFormats.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifLodTests
{
    private static byte[] Build(
        int[] children,
        (float Near, float Far)[] ranges,
        int? lodDataOverride = null,
        int? wrongTypeLodData = null)
    {
        var builder = new NifTestDataBuilder();
        var lodType = builder.AddBlockType("NiLODNode");
        var rangeType = builder.AddBlockType("NiRangeLODData");
        var shapeType = builder.AddBlockType("NiTriShape");
        var nodeType = builder.AddBlockType("NiNode");

        var nonNull = children.Count(c => c >= 0);
        var shapeBodies = Enumerable.Range(0, nonNull)
            .Select(_ => NifTestGeometry.Shape(name: "lod_mesh", data: -1))
            .ToList();
        var rangeBody = NifTestLod.RangeLodData(ranges);

        builder.AddBlock(lodType, NifTestLod.LodNode(children, lodDataOverride ?? 1 + shapeBodies.Count));
        for (var i = 0; i < shapeBodies.Count; i++)
        {
            builder.AddBlock(shapeType, shapeBodies[i]);
        }

        if (wrongTypeLodData is { } wrong)
        {
            builder.AddBlock(nodeType, NifTestBlocks.Node(name: "notlod"));
        }

        else
        {
            builder.AddBlock(rangeType, rangeBody);
        }

        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    [Fact]
    public void BomberShapeSelectsFirstChildByMinimumNear()
    {
        var file = Read(Build([1, 2], [(0.0f, 3.0f), (3.0f, 100000.0f)]));
        var lod = (NifLodNode)file.Blocks[0];
        Assert.Equal(2, ((NifRangeLodData)file.Blocks[lod.LodDataReference]).Ranges.Count);
        Assert.Equal(1, file.GetHighestDetailLodChild(lod));
    }

    [Fact]
    public void SelectionUsesNearDistanceNotBlockOrder()
    {
        var file = Read(Build([1, 2], [(3.0f, 100000.0f), (0.0f, 3.0f)]));
        Assert.Equal(2, file.GetHighestDetailLodChild((NifLodNode)file.Blocks[0]));
    }

    [Fact]
    public void NullChildrenAreSkippedForSelection()
    {
        var file = Read(Build([-1, 2], [(0.0f, 3.0f)]));
        var lod = (NifLodNode)file.Blocks[0];
        Assert.Equal([2], lod.ChildReferences.Where(c => c >= 0).ToArray());
        Assert.Equal(2, file.GetHighestDetailLodChild(lod));
    }

    [Fact]
    public void ChildCountMismatchFails()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => Read(Build([1, 2], [(0.0f, 3.0f)])));
        Assert.Contains("children", ex.Message);
    }

    [Fact]
    public void LodDataReferenceToWrongTypeFails()
    {
        var builder = new NifTestDataBuilder();
        var lodType = builder.AddBlockType("NiLODNode");
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(lodType, NifTestLod.LodNode([1], 1));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "notlod"));
        builder.AddTrailing(NifTestBlocks.Footer());
        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("LOD data", ex.Message);
    }

    [Fact]
    public void InvertedRangeFails()
    {
        Assert.Throws<InvalidDataException>(
            () => Read(Build([1], [(5.0f, 1.0f)])));
    }

    [Fact]
    public void NonFiniteRangeFails()
    {
        Assert.Throws<InvalidDataException>(
            () => Read(Build([1], [(float.NegativeInfinity, 1.0f)])));
    }

    [Fact]
    public void AmbiguousMinimumNearFailsOnSelection()
    {
        var file = Read(Build([1, 2], [(0.0f, 3.0f), (0.0f, 5.0f)]));
        var ex = Assert.Throws<InvalidDataException>(
            () => file.GetHighestDetailLodChild((NifLodNode)file.Blocks[0]));
        Assert.Contains("ambiguous", ex.Message);
    }

    [Fact]
    public void SelectionWithoutRangeDataFails()
    {
        var builder = new NifTestDataBuilder();
        var lodType = builder.AddBlockType("NiLODNode");
        builder.AddBlock(lodType, NifTestLod.LodNode([-1], -1));
        builder.AddTrailing(NifTestBlocks.Footer());
        var file = Read(builder.Build());
        Assert.Throws<InvalidDataException>(
            () => file.GetHighestDetailLodChild((NifLodNode)file.Blocks[0]));
    }
}
