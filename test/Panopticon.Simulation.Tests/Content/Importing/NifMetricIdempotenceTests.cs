using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Tests.Content;
using Panopticon.Tests.Data.Nif;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class NifMetricIdempotenceTests
{
    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.3048f)]
    [InlineData(3.28084f)]
    public void MetricSource_GeometryExtentsKeepTheirMeterValue(float meters)
    {
        var model = Import(TriangleBytes(meters, shapeTranslation: null));
        var mesh = (ImportedMesh)Assert.Single(model!.SubObjects).RenderObject;

        // NIF source (x, y, z) becomes engine (-y, x, z) with no scale.
        AssertPosition(new Vector3(0, 0, 0), mesh.Vertices[0].Position);
        AssertPosition(new Vector3(0, meters, 0), mesh.Vertices[1].Position);
        AssertPosition(new Vector3(-meters, 0, 0), mesh.Vertices[2].Position);
        AssertPosition(new Vector3(0, 0, meters), mesh.Vertices[3].Position);
        AssertPosition(new Vector3(-meters, 0, 0), mesh.BoundingBox.Min);
        AssertPosition(new Vector3(0, meters, meters), mesh.BoundingBox.Max);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.3048f)]
    [InlineData(3.28084f)]
    public void MetricSource_ShapeTranslationKeepsItsMeterValue(float meters)
    {
        var model = Import(TriangleBytes(meters, shapeTranslation: (0, -meters, 0)));
        var mesh = (ImportedMesh)Assert.Single(model!.SubObjects).RenderObject;

        AssertPosition(new Vector3(meters, 0, 0), mesh.Vertices[0].Position);
    }

    private static ImportedModel? Import(byte[] fileBytes)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("cache/nifcache/metric.nifcache", fileBytes);
        return new NifModelImporter().ImportModel("metric", new AssetImportContext(fileSystem, language: null));
    }

    private static byte[] TriangleBytes(float meters, (float X, float Y, float Z)? shapeTranslation)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 2, translation: shapeTranslation));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (meters, 0, 0), (0, meters, 0), (0, 0, meters)],
            hasNormals: false,
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static void AssertPosition(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.Z, actual.Z, 5);
    }
}
