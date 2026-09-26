using System.IO;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.M3;
using Panopticon.Tests.Content;
using Panopticon.Tests.Data.M3;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class M3ModelImporterTests
{
    private const string ModelPath = "mods/liberty.sc2mod/base.sc2assets/assets/units/test/test.m3";

    [Fact]
    public void MissingEntryReturnsNull()
    {
        var importer = new M3ModelImporter();

        Assert.Null(importer.ImportModel(ModelPath, new AssetImportContext(new MemoryFileSystem(), null)));
    }

    [Fact]
    public void ImportsBatchGeometryInEngineFrame()
    {
        var model = Import(CreateTriangleModel());

        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal(0, subObject.ParentBoneIndex);
        var mesh = Assert.IsType<ImportedMesh>(subObject.RenderObject);
        Assert.False(mesh.IsSkinned);
        Assert.Equal(ImportedMaterial.PbrMetallicRoughnessFamily, Assert.Single(mesh.Materials).ShaderFamily);
        Assert.Equal(new ushort[] { 0, 1, 2 }, mesh.Indices);

        AssertEqual(new Vector3(10, 0, 0), mesh.Vertices[0].Position);
        AssertEqual(new Vector3(10, 10, 0), mesh.Vertices[1].Position);
        AssertEqual(new Vector3(20, 0, 0), mesh.Vertices[2].Position);
        AssertEqual(Vector3.UnitZ, mesh.Vertices[0].Normal, tolerance: 1e-2f);
        Assert.NotNull(mesh.Vertices[0].Tangent);
        Assert.NotNull(mesh.Vertices[0].Binormal);

        var part = Assert.Single(mesh.MeshParts);
        Assert.Equal(new Vector2(0, 0), part.TextureCoordinates0![0]);
        Assert.Equal(new Vector2(1, 0), part.TextureCoordinates0[1]);
        Assert.Equal(new Vector2(0, 1), part.TextureCoordinates0[2]);
        Assert.Equal(new Vector2(1, 0), part.TextureCoordinates1![0]);
        Assert.Equal(new Vector2(1, 1), part.TextureCoordinates1[1]);
        Assert.Equal(new Vector2(0, 1), part.TextureCoordinates1[2]);
    }

    [Fact]
    public void ImportsOneSubObjectPerBatch()
    {
        var bytes = new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
            BatchCount = 2,
        }.Build();

        var model = Import(bytes);

        Assert.Equal(2, model.SubObjects.Count);
    }

    [Fact]
    public void FaceIndexOutsideRegionFails()
    {
        var bytes = new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
            Faces = [0, 1, 3],
        }.Build();

        var ex = Assert.Throws<InvalidDataException>(() => Import(bytes));

        Assert.Contains("face index 3", ex.Message);
        Assert.Contains("vertex count 3", ex.Message);
    }

    [Fact]
    public void ModelKeyIsCaseInsensitive()
    {
        var importer = new M3ModelImporter();
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(ModelPath, CreateTriangleModel());
        fileSystem.AddFile(
            "mods/liberty.sc2mod/base.sc2assets/Assets/Textures/Synthetic_Diffuse.dds",
            CreateDxt1Dds());

        var model = importer.ImportModel(ModelPath.ToUpperInvariant(), new AssetImportContext(fileSystem, null));

        Assert.NotNull(model);
    }

    private static ImportedModel Import(byte[] bytes)
    {
        var importer = new M3ModelImporter();
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(ModelPath, bytes);
        fileSystem.AddFile(
            "mods/liberty.sc2mod/base.sc2assets/Assets/Textures/Synthetic_Diffuse.dds",
            CreateDxt1Dds());
        return importer.ImportModel(ModelPath, new AssetImportContext(fileSystem, null))!;
    }

    private static byte[] CreateTriangleModel()
    {
        return new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
        }.Build();
    }

    private static M3TestVertex[] TriangleVertices()
    {
        return
        [
            new(new Vector3(0, -1, 0), Vector3.UnitZ, new Vector2(0, 0), new Vector2(1, 0)),
            new(new Vector3(1, -1, 0), Vector3.UnitZ, new Vector2(1, 0), new Vector2(1, 1)),
            new(new Vector3(0, -2, 0), Vector3.UnitZ, new Vector2(0, 1), new Vector2(0, 1)),
        ];
    }

    private static void AssertEqual(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"Expected {expected}, got {actual}.");
    }

    private static byte[] CreateDxt1Dds()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("DDS "));
        writer.Write((uint)124);
        writer.Write((uint)0x0002100F);
        writer.Write((uint)4);
        writer.Write((uint)4);
        writer.Write((uint)8);
        writer.Write((uint)0);
        writer.Write((uint)1);
        writer.Write(new byte[44]);
        writer.Write((uint)32);
        writer.Write((uint)0x4);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("DXT1"));
        writer.Write(new byte[20]);
        writer.Write((uint)0x1000);
        writer.Write(new byte[16]);
        writer.Write(new byte[] { 0x00, 0xF8, 0xE0, 0x07, 0, 0, 0, 0 });
        return stream.ToArray();
    }
}
