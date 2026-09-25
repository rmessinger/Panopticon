using System;
using System.IO;
using Panopticon.FileFormats.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifSkinTests
{
    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    private static byte[] SkinnedModelBytes(
        byte[]? skinData = null,
        byte[]? skinInstance = null,
        byte[]? skinPartition = null)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var skinDataType = builder.AddBlockType("NiSkinData");
        var skinInstanceType = builder.AddBlockType("NiSkinInstance");
        var skinPartitionType = builder.AddBlockType("NiSkinPartition");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1, 2]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "arm"));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "body", data: 3, skin: 5));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(numVertices: 4));
        builder.AddBlock(skinDataType, skinData ?? NifTestBlocks.SkinData(
            boneWeights:
            [
                [(0, 0.25f), (2, 0.75f)],
                [(1, 1.0f)],
            ],
            boundCenters: [(2, 3, 4), (0, 0, 0)],
            boundRadii: [5.0f, 2.0f]));
        builder.AddBlock(skinInstanceType, skinInstance ?? NifTestBlocks.SkinInstance(
            data: 4, skeletonRoot: 0, bones: [0, 1]));
        if (skinPartition is not null)
        {
            builder.AddBlock(skinPartitionType, skinPartition);
        }

        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    [Fact]
    public void SkinInstanceAndDataRoundTripExactly()
    {
        var file = Read(SkinnedModelBytes());

        var instance = (NifSkinInstance)file.Blocks[5];
        Assert.Equal(4, instance.DataReference);
        Assert.Equal(0, instance.SkeletonRootReference);
        Assert.Equal([0, 1], instance.BoneReferences);

        var data = (NifSkinData)file.Blocks[4];
        Assert.Equal(1.0f, data.SkinTransform.Scale);
        Assert.True(data.SkinTransform.Rotation.IsIdentity(0.0f));
        Assert.Equal(2, data.Bones.Count);
        Assert.Equal(-1, data.PartitionReference);
        Assert.True(data.HasVertexWeights);

        var arm = data.Bones[0];
        Assert.Equal(new System.Numerics.Vector3(2, 3, 4), arm.BoundCenter);
        Assert.Equal(5.0f, arm.BoundRadius);
        Assert.Equal(2, arm.VertexWeights.Count);
        Assert.Equal(0, arm.VertexWeights[0].VertexIndex);
        Assert.Equal(0.25f, arm.VertexWeights[0].Weight);
        Assert.Equal(2, arm.VertexWeights[1].VertexIndex);
        Assert.Equal(0.75f, arm.VertexWeights[1].Weight);

        var torso = data.Bones[1];
        Assert.Equal(1, torso.VertexWeights.Count);
        Assert.Equal(1, torso.VertexWeights[0].VertexIndex);
        Assert.Equal(1.0f, torso.VertexWeights[0].Weight);
    }

    [Fact]
    public void SkinPartitionRoundTripsAllFields()
    {
        var partition = NifTestBlocks.SkinPartition(
            bones: [5, 2],
            vertexCount: 2,
            triangleCount: 1,
            stripCount: 1,
            weightsPerVertex: 2,
            hasVertexMap: true,
            vertexMap: [1, 0],
            hasVertexWeights: true,
            vertexWeights: [0.25f, 0.75f, 1.0f, 0.0f],
            stripLengths: [4],
            hasFaces: true,
            faces: [0, 1, 2, 3],
            hasBoneIndices: true,
            boneIndices: [7, 8, 9, 10]);

        var file = Read(SkinnedModelBytes(skinPartition: partition));

        var skinPartition = (NifSkinPartition)file.Blocks[6];
        var part = Assert.Single(skinPartition.Partitions);
        Assert.Equal(2, part.VertexCount);
        Assert.Equal(1, part.TriangleCount);
        Assert.Equal([5, 2], part.Bones);
        Assert.Equal(1, part.StripLengths.Count);
        Assert.Equal(4u, part.StripLengths[0]);
        Assert.True(part.HasVertexMap);
        Assert.Equal([1, 0], part.VertexMap);
        Assert.True(part.HasVertexWeights);
        Assert.Equal(0.75f, part.VertexWeights[0][1]);
        Assert.Equal(0.0f, part.VertexWeights[1][1]);
        Assert.True(part.HasFaces);
        var strip = Assert.Single(part.Faces);
        Assert.Equal([0, 1, 2, 3], strip);
        Assert.True(part.HasBoneIndices);
        Assert.Equal(7, part.BoneIndices[0][0]);
        Assert.Equal(10, part.BoneIndices[1][1]);
    }

    [Fact]
    public void SkinPartitionTriangleModeReadsFlatTriangles()
    {
        var partition = NifTestBlocks.SkinPartition(
            bones: [0],
            vertexCount: 3,
            triangleCount: 1,
            hasFaces: true,
            faces: [0, 1, 2]);

        var file = Read(SkinnedModelBytes(skinPartition: partition));

        var skinPartition = (NifSkinPartition)file.Blocks[6];
        var part = Assert.Single(skinPartition.Partitions);
        var triangles = Assert.Single(part.Faces);
        Assert.Equal([0, 1, 2], triangles);
        Assert.Empty(part.StripLengths);
    }

    [Fact]
    public void TruncatedSkinDataThrowsWithContext()
    {
        var bytes = SkinnedModelBytes();
        // Cut through the footer, skin instance, and into the last bone of
        // the skin data block; the read must fail naming the skin field.
        var truncated = new byte[bytes.Length - 40];
        Array.Copy(bytes, 0, truncated, 0, truncated.Length);

        var ex = Assert.Throws<InvalidDataException>(() => Read(truncated));
        Assert.Contains("Bones[1]", ex.Message);
    }

    [Fact]
    public void SkinInstanceDataRefMustTargetSkinData()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => Read(SkinnedModelBytes(skinInstance: NifTestBlocks.SkinInstance(
                data: 0, skeletonRoot: 0, bones: [0, 1]))));

        Assert.Contains("as skin data", ex.Message);
    }

    [Fact]
    public void SkeletonRootMustTargetNode()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => Read(SkinnedModelBytes(skinInstance: NifTestBlocks.SkinInstance(
                data: 4, skeletonRoot: 3, bones: [0, 1]))));

        Assert.Contains("skeleton root", ex.Message);
    }

    [Fact]
    public void BoneRefMustTargetNode()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => Read(SkinnedModelBytes(skinInstance: NifTestBlocks.SkinInstance(
                data: 4, skeletonRoot: 0, bones: [0, 4]))));

        Assert.Contains("as bone 1", ex.Message);
    }

    [Fact]
    public void InstanceAndDataBoneCountsMustMatch()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => Read(SkinnedModelBytes(skinInstance: NifTestBlocks.SkinInstance(
                data: 4, skeletonRoot: 0, bones: [0]))));

        Assert.Contains("lists 1", ex.Message);
        Assert.Contains("declares 2", ex.Message);
    }

    [Fact]
    public void PartitionRefMustTargetSkinPartition()
    {
        var data = NifTestBlocks.SkinData(
            boneWeights: [[(0, 1.0f)], [(1, 1.0f)]],
            partition: 0);

        var ex = Assert.Throws<InvalidDataException>(() => Read(SkinnedModelBytes(skinData: data)));

        Assert.Contains("as a skin partition", ex.Message);
    }

    [Fact]
    public void GeometrySkinRefMustTargetSkinInstance()
    {
        var bytes = SkinnedModelBytes();
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "body", data: 2, skin: 0));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(numVertices: 4));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));

        Assert.Contains("as a skin instance", ex.Message);
    }
}
