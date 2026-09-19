#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.Tests.Content;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public class GltfImporterTests
{
    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static string B64(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static string StaticModelBuffer()
    {
        var stream = new MemoryStream();
        var floatBytes = new byte[4];
        foreach (var value in new float[]
                 {
                     0, 0, 0, 1, 0, 0, 0, 1, 0,
                     0, 0, 1, 0, 0, 1, 0, 0, 1,
                     0.25f, 0.5f, 0.75f, 0.25f, 0.5f, 0.75f,
                 })
        {
            Buffer.BlockCopy(new[] { value }, 0, floatBytes, 0, 4);
            stream.Write(floatBytes, 0, 4);
        }

        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(1);
        stream.WriteByte(0);
        stream.WriteByte(2);
        stream.WriteByte(0);
        return Convert.ToBase64String(stream.ToArray());
    }

    /// <summary>Static single-triangle model with material, UVs, and a data-URI texture.</summary>
    private static string BuildStaticModelJson()
    {
        return "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"scenes\": [ { \"nodes\": [0] } ], \"scene\": 0," +
            "\"nodes\": [ { \"name\": \"hull\", \"mesh\": 0 } ]," +
            "\"meshes\": [ { \"name\": \"hullMesh\", \"primitives\": [ { " +
            "\"attributes\": { \"POSITION\": 0, \"NORMAL\": 1, \"TEXCOORD_0\": 2 }, \"indices\": 3, \"material\": 0 } ] } ]," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorFactor\": [1, 0, 0, 1], \"metallicFactor\": 0.1, \"roughnessFactor\": 0.9, \"baseColorTexture\": { \"index\": 0 } } } ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ { \"uri\": \"data:image/png;base64," + Convert.ToBase64String(MinimalPng) + "\", \"name\": \"skin\" } ]," +
            "\"accessors\": [" +
            "{ \"bufferView\": 0, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\", \"min\": [0, 0, 0], \"max\": [1, 1, 1] }," +
            "{ \"bufferView\": 1, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\" }," +
            "{ \"bufferView\": 2, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC2\" }," +
            "{ \"bufferView\": 3, \"componentType\": 5123, \"count\": 3, \"type\": \"SCALAR\" }" +
            "]," +
            "\"bufferViews\": [" +
            "{ \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": 36 }," +
            "{ \"buffer\": 0, \"byteOffset\": 36, \"byteLength\": 36 }," +
            "{ \"buffer\": 0, \"byteOffset\": 72, \"byteLength\": 24 }," +
            "{ \"buffer\": 0, \"byteOffset\": 96, \"byteLength\": 6 }" +
            "]," +
            "\"buffers\": [ { \"uri\": \"data:application/octet-stream;base64," + StaticModelBuffer() +
            "\", \"byteLength\": 102 } ]" +
            "}";
    }

    private static AssetImportContext CreateContext(string json)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("models/tank.gltf", json);
        return new AssetImportContext(fileSystem, null);
    }

    [Fact]
    public void InvalidKey_ReturnsNull()
    {
        var importer = new GltfModelImporter(GltfImportOptions.Default);
        var context = CreateContext(BuildStaticModelJson());

        Assert.Null(importer.ImportModel("models/tank.w3d", context));
        Assert.Null(importer.ImportModel("models/tank.gltf?x=1", context));
    }

    [Fact]
    public void MissingRoot_ReturnsNull()
    {
        var importer = new GltfModelImporter(GltfImportOptions.Default);
        var fileSystem = new MemoryFileSystem();
        var context = new AssetImportContext(fileSystem, null);

        Assert.Null(importer.ImportModel("models/tank.gltf", context));
    }

    [Fact]
    public void StaticModel_ImportsCompleteGraph()
    {
        var importer = new GltfModelImporter(GltfImportOptions.Default);
        var context = CreateContext(BuildStaticModelJson());

        var model = importer.ImportModel("models/tank.gltf", context);

        Assert.NotNull(model);
        Assert.Equal("tank", model!.Name);

        var bone = Assert.Single(model.Skeleton.Bones);
        Assert.Equal("hull", bone.Name);

        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal(0, subObject.ParentBoneIndex);
        Assert.IsType<ImportedMesh>(subObject.RenderObject);

        var mesh = (ImportedMesh)subObject.RenderObject;
        Assert.False(mesh.IsSkinned);
        Assert.Equal(3, mesh.Vertices.Count);

        var material = Assert.Single(mesh.Materials);
        Assert.Equal(ImportedMaterial.PbrMetallicRoughnessFamily, material.ShaderFamily);
        var payload = material.PbrMetallicRoughness!;
        Assert.Equal(new Vector4(1, 0, 0, 1), payload.BaseColorFactor);
        Assert.Equal(0.1f, payload.MetallicFactor, 5);
        Assert.Equal(0.9f, payload.RoughnessFactor, 5);
        Assert.NotNull(payload.BaseColorTexture);
        Assert.Equal(ImportedImageSourceKind.EmbeddedBytes, payload.BaseColorTexture!.ImageSource.Kind);

        // Basis-converted bounds: positions (0..1) meters map to world units at 3.28084.
        var s = 1f / 0.3048f;

        // Converted axes: box max maps glTF (maxZ, maxX, maxY) = (0, 1, 1) meters.
        Assert.Equal(new Vector3(0, s, s), mesh.BoundingBox.Max);
    }

    [Fact]
    public void VertexLimit_ThrowsWithPathContext()
    {
        var options = new GltfImportOptions(maxVertexCount: 2);
        var importer = new GltfModelImporter(options);
        var context = CreateContext(BuildStaticModelJson());

        var exception = Assert.Throws<InvalidDataException>(() => importer.ImportModel("models/tank.gltf", context));

        Assert.Contains("models/tank.gltf", exception.Message);
        Assert.Contains("hull", exception.Message);
    }

    [Fact]
    public void NodeLimit_Throws()
    {
        var json = BuildStaticModelJson().Replace(
            "\"nodes\": [ { \"name\": \"hull\", \"mesh\": 0 } ],",
            "\"nodes\": [ { \"name\": \"hull\", \"mesh\": 0 }, { \"name\": \"extra\" } ],",
            StringComparison.Ordinal).Replace(
            "\"scenes\": [ { \"nodes\": [0] } ],",
            "\"scenes\": [ { \"nodes\": [0, 1] } ],",
            StringComparison.Ordinal);
        var options = new GltfImportOptions(maxNodeCount: 1);
        var importer = new GltfModelImporter(options);
        var context = CreateContext(json);

        Assert.Throws<InvalidDataException>(() => importer.ImportModel("models/tank.gltf", context));
    }

    [Fact]
    public void PrimitiveLimit_Throws()
    {
        var json = BuildStaticModelJson().Replace(
            "\"attributes\": { \"POSITION\": 0, \"NORMAL\": 1, \"TEXCOORD_0\": 2 }, \"indices\": 3, \"material\": 0 }",
            "\"attributes\": { \"POSITION\": 0, \"NORMAL\": 1, \"TEXCOORD_0\": 2 }, \"indices\": 3, \"material\": 0 }, { \"attributes\": { \"POSITION\": 0, \"NORMAL\": 1 }, \"indices\": 3 }",
            StringComparison.Ordinal);
        var options = new GltfImportOptions(maxPrimitiveCount: 1);
        var importer = new GltfModelImporter(options);
        var context = CreateContext(json);

        Assert.Throws<InvalidDataException>(() => importer.ImportModel("models/tank.gltf", context));
    }

    [Fact]
    public void MultipleSkins_Throws()
    {
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"scenes\": [ { \"nodes\": [0] } ], \"scene\": 0," +
            "\"nodes\": [ { \"name\": \"joint\" } ]," +
            "\"skins\": [ { \"joints\": [0], \"skeleton\": 0 }, { \"joints\": [0], \"skeleton\": 0 } ]" +
            "}";
        var importer = new GltfModelImporter(GltfImportOptions.Default);
        var context = CreateContext(json);

        Assert.Throws<NotSupportedException>(() => importer.ImportModel("models/tank.gltf", context));
    }

    [Fact]
    public void RequiredExtension_NeverDisappearsSilently()
    {
        // SharpGLTF Core rejects unknown required extensions at strict parse; either way the
        // import must not succeed silently.
        var json = "{ \"asset\": { \"version\": \"2.0\" }, \"extensionsRequired\": [\"KHR_draco_mesh_compression\"], \"scenes\": [ { \"nodes\": [] } ], \"scene\": 0 }";
        var importer = new GltfModelImporter(GltfImportOptions.Default);
        var context = CreateContext(json);

        Assert.ThrowsAny<Exception>(() => importer.ImportModel("models/tank.gltf", context));
    }

    [Fact]
    public void MalformedMaterial_FailsWholeImport()
    {
        // The static model with a texture transform cannot complete; the importer must throw
        // rather than return a partial graph.
        var json = BuildStaticModelJson().Replace(
            "\"baseColorTexture\": { \"index\": 0 }",
            "\"baseColorTexture\": { \"index\": 0, \"texCoord\": 2 }",
            StringComparison.Ordinal);
        var importer = new GltfModelImporter(GltfImportOptions.Default);
        var context = CreateContext(json);

        Assert.ThrowsAny<Exception>(() => importer.ImportModel("models/tank.gltf", context));
    }

    [Fact]
    public void Diagnostics_DeduplicatesExtensionWarnings()
    {
        var diagnostics = new GltfImportDiagnostics();

        diagnostics.WarnOptionalExtensionIgnored("KHR_materials_sheen");
        diagnostics.WarnOptionalExtensionIgnored("KHR_materials_sheen");

        var warning = Assert.Single(diagnostics.Warnings);
        Assert.Contains("KHR_materials_sheen", warning);
    }

    [Fact]
    public void ExtensionPolicy_EmptyDocument_ProducesNoWarnings()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("models/empty.gltf", "{ \"asset\": { \"version\": \"2.0\" }, \"scenes\": [ { \"nodes\": [0] } ], \"scene\": 0, \"nodes\": [ { \"name\": \"only\" } ] }");
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("models/empty.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("models/empty.gltf", context, GltfImportOptions.Default, resolver);
        var diagnostics = new GltfImportDiagnostics();

        GltfModelImporter.ValidateExtensionPolicy(document!.Root, diagnostics);

        Assert.Empty(diagnostics.Warnings);
    }

    [Fact]
    public void SkeletonImport_ReturnsPlanSkeleton()
    {
        var importer = new GltfModelImporter(GltfImportOptions.Default);
        var context = CreateContext(BuildStaticModelJson());

        var skeleton = importer.ImportSkeleton("models/tank.gltf", context);

        Assert.NotNull(skeleton);
        Assert.Equal("hull", Assert.Single(skeleton!.Bones).Name);
    }
}
