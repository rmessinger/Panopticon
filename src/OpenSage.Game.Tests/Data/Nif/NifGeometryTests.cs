using System;
using System.IO;
using OpenSage.FileFormats.Nif;
using Xunit;
using NifTestBlocks = OpenSage.Tests.Data.Nif.NifTestBlocks;

namespace OpenSage.Tests.Data.Nif;

public sealed class NifGeometryTests
{
    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    private static NifTestDataBuilder ShapeBuilder(
        byte[]? geometryData = null,
        int? dataOverride = null,
        int skin = -1,
        byte hasShader = 0)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(data: dataOverride ?? 2, skin: skin, hasShader: hasShader));
        builder.AddBlock(dataType, geometryData ?? NifTestGeometry.GeometryData(
            numVertices: 4,
            triangles: [(0, 1, 2), (2, 1, 3)],
            matchGroupIndices: [0, 1]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder;
    }

    [Fact]
    public void GeometryRoundTripsExactly()
    {
        var file = Read(ShapeBuilder().Build());

        var shape = (NifTriShape)file.Blocks[1];
        Assert.Equal(2, shape.DataReference);
        var data = (NifTriShapeData)file.Blocks[2];

        Assert.Equal(4, data.NumVertices);
        Assert.Equal(new System.Numerics.Vector3(0, 0, 0), data.Vertices[0]);
        Assert.Equal(new System.Numerics.Vector3(3, 6, 9), data.Vertices[3]);
        Assert.NotNull(data.Normals);
        Assert.Equal(4, data.Normals.Count);
        Assert.Equal(1, data.NumUvSets);
        Assert.Equal(new System.Numerics.Vector2(0.2f, 0.8f), data.UvSets[0][2]);
        Assert.Equal(new System.Numerics.Vector3(0, 0, 0), data.BoundCenter);
        Assert.Equal(1.0f, data.BoundRadius);
        Assert.Equal(0x4000, data.ConsistencyFlags);
        Assert.Equal(2, data.NumTriangles);
        Assert.Equal(6, data.NumTrianglePoints);
        Assert.Equal((ushort)2, data.Triangles[1].V1);
        Assert.Single(data.MatchGroups);
        Assert.Equal(2, data.MatchGroups[0].VertexIndices.Length);
    }

    [Fact]
    public void ZeroUvSetsAreDecoded()
    {
        var geometry = NifTestGeometry.GeometryData(numVertices: 3, dataFlags: 0);
        var file = Read(ShapeBuilder(geometryData: geometry).Build());
        var data = (NifTriShapeData)file.Blocks[2];
        Assert.Equal(0, data.NumUvSets);
        Assert.Empty(data.UvSets);
    }

    [Fact]
    public void TriangleIndexOutOfRangeFails()
    {
        var geometry = NifTestGeometry.GeometryData(numVertices: 3, triangles: [(0, 1, 9)]);
        var ex = Assert.Throws<InvalidDataException>(() => Read(ShapeBuilder(geometry).Build()));
        Assert.Contains("triangle 0", ex.Message);
    }

    [Fact]
    public void TrianglePointCountMismatchFails()
    {
        var geometry = NifTestGeometry.GeometryData(
            numVertices: 3, triangles: [(0, 1, 2)], numTrianglePoints: 7);
        Assert.Throws<InvalidDataException>(() => Read(ShapeBuilder(geometry).Build()));
    }

    [Fact]
    public void HasTrianglesZeroFails()
    {
        var geometry = NifTestGeometry.GeometryData(numVertices: 3, hasTriangles: 0);
        Assert.Throws<InvalidDataException>(() => Read(ShapeBuilder(geometry).Build()));
    }

    [Fact]
    public void VertexColorsEnabledFailsWithNotSupported()
    {
        var geometry = NifTestGeometry.GeometryData(numVertices: 3, hasColors: true);
        Assert.Throws<NotSupportedException>(() => Read(ShapeBuilder(geometry).Build()));
    }

    [Fact]
    public void MissingNormalsFailsWithNotSupported()
    {
        var geometry = NifTestGeometry.GeometryData(numVertices: 3, hasNormals: false);
        var ex = Assert.Throws<NotSupportedException>(() => Read(ShapeBuilder(geometry).Build()));
        Assert.Contains("normal", ex.Message);
    }

    [Fact]
    public void TangentsFlagFailsWithNotSupported()
    {
        var geometry = NifTestGeometry.GeometryData(numVertices: 3, dataFlags: (ushort)(1 | 0x1000));
        Assert.Throws<NotSupportedException>(() => Read(ShapeBuilder(geometry).Build()));
    }

    [Fact]
    public void DataReferenceToWrongBlockTypeFails()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => Read(ShapeBuilder(dataOverride: 0).Build()));
        Assert.Contains("as data", ex.Message);
    }

    [Fact]
    public void SkinnedShapeFailsWithNotSupported()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Read(ShapeBuilder(skin: 2).Build()));
        Assert.Contains("skinned", ex.Message);
    }

    [Fact]
    public void ShaderMaterialFailsWithNotSupported()
    {
        Assert.Throws<NotSupportedException>(
            () => Read(ShapeBuilder(hasShader: 1).Build()));
    }

    [Fact]
    public void MatchGroupIndicesBeyondVertexCountArePreserved()
    {
        // The EE2 cooker emits such indices (body_LOD1); they are diagnostics.
        var geometry = NifTestGeometry.GeometryData(numVertices: 3, matchGroupIndices: [77]);
        var file = Read(ShapeBuilder(geometry).Build());
        var data = (NifTriShapeData)file.Blocks[2];
        Assert.Equal(77, data.MatchGroups[0].VertexIndices[0]);
    }
}
