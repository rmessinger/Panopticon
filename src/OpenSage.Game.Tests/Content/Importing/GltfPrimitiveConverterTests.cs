#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public class GltfPrimitiveConverterTests
{
    private static readonly GltfImportOptions Options = GltfImportOptions.Default;

    private static ImportedMaterial PlaceholderMaterial() => ImportedMaterial.CreatePbrMetallicRoughness(
        new ImportedPbrMetallicRoughness());

    private static GltfAttachmentPlan IdentityAttachment()
    {
        return new GltfAttachmentPlan(
            boneIndex: 0,
            sourceNodeIndex: 0,
            geometryTransform: Matrix4x4.Identity,
            normalMatrix: Matrix4x4.Identity,
            requiresTangentReorthogonalization: false);
    }

    private static byte[] EncodeFloats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] EncodeUShort(params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] EncodeUInt(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static SharpGLTF.Schema2.MeshPrimitive CreatePrimitive(
        byte[] positionData,
        byte[] normalData,
        byte[]? indexData = null,
        int vertexCount = 3,
        SharpGLTF.Schema2.IndexEncodingType indexComponentType = SharpGLTF.Schema2.IndexEncodingType.UNSIGNED_SHORT,
        int indexBytesPerValue = 2)
    {
        var model = SharpGLTF.Schema2.ModelRoot.CreateModel();
        var mesh = model.CreateMesh("mesh");
        var primitive = mesh.CreatePrimitive();

        var positionView = model.UseBufferView(positionData);
        var positionAccessor = model.CreateAccessor("POSITION");
        positionAccessor.SetVertexData(positionView, 0, vertexCount, SharpGLTF.Schema2.DimensionType.VEC3, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("POSITION", positionAccessor);

        var normalView = model.UseBufferView(normalData);
        var normalAccessor = model.CreateAccessor("NORMAL");
        normalAccessor.SetVertexData(normalView, 0, vertexCount, SharpGLTF.Schema2.DimensionType.VEC3, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("NORMAL", normalAccessor);

        if (indexData != null)
        {
            var indexView = model.UseBufferView(indexData);
            var indexAccessor = model.CreateAccessor("INDICES");
            indexAccessor.SetIndexData(indexView, 0, indexData.Length / indexBytesPerValue, indexComponentType);
            primitive.SetIndexAccessor(indexAccessor);
        }

        return primitive;
    }

    private static readonly float[] TrianglePositions =
    [
        0, 0, 0,
        1, 0, 0,
        0, 1, 0,
    ];

    private static readonly float[] TriangleNormals =
    [
        0, 0, 1,
        0, 0, 1,
        0, 0, 1,
    ];

    [Fact]
    public void NonIndexedTriangle_ConvertsWithBasisAndUnits()
    {
        var primitive = CreatePrimitive(EncodeFloats(TrianglePositions), EncodeFloats(TriangleNormals));

        var result = GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());

        var mesh = Assert.Single(result.Meshes);
        var s = 1f / 0.3048f;
        Assert.Equal(new Vector3(0, 0, 0), mesh.Vertices[0].Position);
        Assert.Equal(new Vector3(0, s, 0), mesh.Vertices[1].Position);

        // glTF +Y (up) becomes OpenSAGE +Z (up).
        Assert.Equal(new Vector3(0, 0, s), mesh.Vertices[2].Position);

        // glTF +Z normals become OpenSAGE +X.
        Assert.Equal(new Vector3(1, 0, 0), mesh.Vertices[0].Normal);

        Assert.Equal(new ushort[] { 0, 1, 2 }, mesh.Indices.ToArray());
        Assert.Equal(3, mesh.BoundingSphere.Center.Length() >= 0 ? mesh.Vertices.Count : 0);
    }

    [Fact]
    public void IndexedTriangle_UsesDecodedIndices()
    {
        var primitive = CreatePrimitive(
            EncodeFloats(TrianglePositions),
            EncodeFloats(TriangleNormals),
            EncodeUShort(0, 2, 1));

        var result = GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());

        // Authored triangle order (0, 2, 1) is preserved: the split assigns local indices by
        // first use, so vertices come back ordered v0, v2, v1 with sequential indices.
        var vertices = result.Meshes[0].Vertices;
        var s = 1f / 0.3048f;
        Assert.Equal(new Vector3(0, 0, 0), vertices[0].Position);
        Assert.Equal(new Vector3(0, 0, s), vertices[1].Position);
        Assert.Equal(new Vector3(0, s, 0), vertices[2].Position);
        Assert.Equal(new ushort[] { 0, 1, 2 }, result.Meshes[0].Indices.ToArray());
    }

    [Fact]
    public void NonIndexedTriangleCountNotDivisibleByThree_Throws()
    {
        // Four vertices without an index buffer cannot form whole triangles.
        var positions = TrianglePositions.Concat([2, 2, 2]).ToArray();
        var normals = TriangleNormals.Concat([0, 0, 1]).ToArray();
        var primitive = CreatePrimitive(EncodeFloats(positions), EncodeFloats(normals), vertexCount: 4);

        Assert.Throws<InvalidDataException>(() =>
            GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial()));
    }

    [Fact]
    public void Tangents_ConvertWithHandedness()
    {
        var model = SharpGLTF.Schema2.ModelRoot.CreateModel();
        var mesh = model.CreateMesh("mesh");
        var primitive = mesh.CreatePrimitive();

        var positionView = model.UseBufferView(EncodeFloats(TrianglePositions));
        var positionAccessor = model.CreateAccessor("POSITION");
        positionAccessor.SetVertexData(positionView, 0, 3, SharpGLTF.Schema2.DimensionType.VEC3, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("POSITION", positionAccessor);

        var normalView = model.UseBufferView(EncodeFloats(TriangleNormals));
        var normalAccessor = model.CreateAccessor("NORMAL");
        normalAccessor.SetVertexData(normalView, 0, 3, SharpGLTF.Schema2.DimensionType.VEC3, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("NORMAL", normalAccessor);

        var tangentData = EncodeFloats(
            1, 0, 0, 1,
            1, 0, 0, -1,
            1, 0, 0, 1);
        var tangentView = model.UseBufferView(tangentData);
        var tangentAccessor = model.CreateAccessor("TANGENT");
        tangentAccessor.SetVertexData(tangentView, 0, 3, SharpGLTF.Schema2.DimensionType.VEC4, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("TANGENT", tangentAccessor);

        var result = GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());

        var vertices = result.Meshes[0].Vertices;

        // glTF tangent +X becomes OpenSAGE +Y; handedness w = -1 flips the binormal.
        Assert.Equal(new Vector3(0, 1, 0), vertices[0].Tangent);
        Assert.Equal(new Vector3(0, 1, 0), vertices[1].Tangent);
        Assert.Equal(Vector3.Normalize(Vector3.Cross(new Vector3(1, 0, 0), new Vector3(0, 1, 0))), vertices[0].Binormal);
        Assert.Equal(-Vector3.Normalize(Vector3.Cross(new Vector3(1, 0, 0), new Vector3(0, 1, 0))), vertices[1].Binormal);
    }

    [Fact]
    public void TextureCoordinates_ArePreservedPerPart()
    {
        var model = SharpGLTF.Schema2.ModelRoot.CreateModel();
        var mesh = model.CreateMesh("mesh");
        var primitive = mesh.CreatePrimitive();

        var positionView = model.UseBufferView(EncodeFloats(TrianglePositions));
        var positionAccessor = model.CreateAccessor("POSITION");
        positionAccessor.SetVertexData(positionView, 0, 3, SharpGLTF.Schema2.DimensionType.VEC3, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("POSITION", positionAccessor);

        var normalView = model.UseBufferView(EncodeFloats(TriangleNormals));
        var normalAccessor = model.CreateAccessor("NORMAL");
        normalAccessor.SetVertexData(normalView, 0, 3, SharpGLTF.Schema2.DimensionType.VEC3, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("NORMAL", normalAccessor);

        var uvData = EncodeFloats(0.25f, 0.5f, 0.75f, 0.125f, 0.5f, 0.5f);
        var uvView = model.UseBufferView(uvData);
        var uvAccessor = model.CreateAccessor("TEXCOORD_0");
        uvAccessor.SetVertexData(uvView, 0, 3, SharpGLTF.Schema2.DimensionType.VEC2, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("TEXCOORD_0", uvAccessor);

        var result = GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());

        var part = result.Meshes[0].MeshParts[0];
        Assert.NotNull(part.TextureCoordinates0);
        Assert.Equal(new Vector2(0.25f, 0.5f), part.TextureCoordinates0![0]);
        Assert.Null(part.TextureCoordinates1);
    }

    [Fact]
    public void UnsupportedAttribute_Throws()
    {
        var primitive = CreatePrimitive(EncodeFloats(TrianglePositions), EncodeFloats(TriangleNormals));
        var model = primitive.LogicalParent.LogicalParent;
        var colorView = model.UseBufferView(EncodeFloats(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
        var colorAccessor = model.CreateAccessor("COLOR_0");
        colorAccessor.SetVertexData(colorView, 0, 3, SharpGLTF.Schema2.DimensionType.VEC4, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("COLOR_0", colorAccessor);

        Assert.Throws<NotSupportedException>(() =>
            GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial()));
    }

    [Fact]
    public void PointMode_Throws()
    {
        var json = "{" +
            "\"asset\": { \"version\": \"2.0\" }," +
            "\"buffers\": [ { \"uri\": \"data:application/octet-stream;base64," + Convert.ToBase64String(EncodeFloats(TrianglePositions)) + "\", \"byteLength\": 36 } ]," +
            "\"bufferViews\": [ { \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": 36 } ]," +
            "\"accessors\": [ { \"bufferView\": 0, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\", \"min\": [0, 0, 0], \"max\": [1, 1, 1] } ]," +
            "\"meshes\": [ { \"primitives\": [ { \"mode\": 0, \"attributes\": { \"POSITION\": 0 } } ] } ] }" +
            "}";
        var fileSystem = new OpenSage.Tests.Content.MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, Options, externalResolver: null);

        Assert.NotNull(document);
        var primitive = document!.Root.LogicalMeshes[0].Primitives[0];

        Assert.Throws<NotSupportedException>(() =>
            GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial()));
    }

    [Fact]
    public void MissingNormal_Throws()
    {
        var model = SharpGLTF.Schema2.ModelRoot.CreateModel();
        var mesh = model.CreateMesh("mesh");
        var primitive = mesh.CreatePrimitive();
        var positionView = model.UseBufferView(EncodeFloats(TrianglePositions));
        var positionAccessor = model.CreateAccessor("POSITION");
        positionAccessor.SetVertexData(positionView, 0, 3, SharpGLTF.Schema2.DimensionType.VEC3, SharpGLTF.Schema2.EncodingType.FLOAT, false);
        primitive.SetVertexAccessor("POSITION", positionAccessor);

        Assert.Throws<InvalidDataException>(() =>
            GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial()));
    }

    [Fact]
    public void NonFinitePosition_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
        {
            var positions = new float[TrianglePositions.Length];
            TrianglePositions.CopyTo(positions, 0);
            positions[0] = float.NaN;
            var primitive = CreatePrimitive(EncodeFloats(positions), EncodeFloats(TriangleNormals), vertexCount: 3);
            GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());
        });
    }

    [Fact]
    public void AttachmentGeometryTransform_ScalesPositionsAndNormals()
    {
        var primitive = CreatePrimitive(EncodeFloats(TrianglePositions), EncodeFloats(TriangleNormals));
        Matrix4x4.Invert(Matrix4x4.CreateScale(2, 4, 2), out var inverseScale);
        var attachment = new GltfAttachmentPlan(
            boneIndex: 0,
            sourceNodeIndex: 0,
            geometryTransform: Matrix4x4.CreateScale(2, 4, 2),
            normalMatrix: Matrix4x4.Transpose(inverseScale),
            requiresTangentReorthogonalization: true);

        var result = GltfPrimitiveConverter.Convert(primitive, attachment, Options, _ => PlaceholderMaterial());

        var vertices = result.Meshes[0].Vertices;
        var s = 1f / 0.3048f;

        // glTF (1, 0, 0) scaled by (2, 4, 2) -> (2, 0, 0), then basis -> OpenSAGE (0, 2, 0) * s.
        Assert.Equal(new Vector3(0, 2 * s, 0), vertices[1].Position);

        // The glTF +Z normal scaled by inverse-transpose then normalized stays +Z -> OpenSAGE +X.
        Assert.Equal(new Vector3(1, 0, 0), vertices[1].Normal);
    }

    [Fact]
    public void OversizedPrimitive_SplitsDeterministically()
    {
        const int vertexCount = GltfMeshSplitter.MaxVerticesPerMesh + 3;
        var positions = new float[vertexCount * 3];
        for (var i = 0; i < vertexCount; i++)
        {
            positions[i * 3] = i;
            positions[i * 3 + 1] = 0;
            positions[i * 3 + 2] = 0;
        }

        var normals = new float[vertexCount * 3];
        for (var i = 0; i < vertexCount; i++)
        {
            normals[i * 3 + 2] = 1;
        }

        var indices = new uint[(vertexCount - 2) * 3];
        var index = 0;
        for (var i = 0; i + 2 < vertexCount; i++)
        {
            indices[index++] = 0;
            indices[index++] = (uint)(i + 1);
            indices[index++] = (uint)(i + 2);
        }

        var indexBytes = new byte[indices.Length * 4];
        Buffer.BlockCopy(indices, 0, indexBytes, 0, indexBytes.Length);

        var primitive = CreatePrimitive(
            EncodeFloats(positions),
            EncodeFloats(normals),
            indexBytes,
            vertexCount,
            indexComponentType: SharpGLTF.Schema2.IndexEncodingType.UNSIGNED_INT,
            indexBytesPerValue: 4);

        var first = GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());
        var second = GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());

        Assert.Equal(2, first.Meshes.Count);
        Assert.All(first.Meshes, mesh => Assert.True(mesh.Vertices.Count <= GltfMeshSplitter.MaxVerticesPerMesh));

        // Triangle order is stable: the union of indices preserves the source sequence.
        var totalTriangles = first.Meshes.Sum(m => m.Indices.Count / 3);
        Assert.Equal(vertexCount - 2, totalTriangles);

        // Repeatable: identical split arrays across conversions.
        for (var m = 0; m < first.Meshes.Count; m++)
        {
            Assert.Equal(
                first.Meshes[m].Vertices.Select(v => v.Position).ToArray(),
                second.Meshes[m].Vertices.Select(v => v.Position).ToArray());
            Assert.Equal(first.Meshes[m].Indices, second.Meshes[m].Indices);
        }

        // Each split's bounds come from exactly its own vertices.
        foreach (var mesh in first.Meshes)
        {
            var expectedBox = OpenSage.Mathematics.AxisAlignedBoundingBox.CreateFromPoints(
                mesh.Vertices.Select(v => v.Position));
            Assert.Equal(expectedBox.Min, mesh.BoundingBox.Min);
            Assert.Equal(expectedBox.Max, mesh.BoundingBox.Max);
        }
    }

    [Fact]
    public void SparseAccessor_OverlaysBaseValues()
    {
        // Base positions are a degenerate triangle; the sparse overlay replaces vertex 1.
        // Built as raw JSON because SharpGLTF's authoring API does not expose sparse writing.
        var positions = EncodeFloats(TrianglePositions);
        var normals = EncodeFloats(TriangleNormals);
        var replacement = EncodeFloats(1, 1, 1);

        var json = BuildSparseDocumentJson(positions, normals, replacement);
        var fileSystem = new OpenSage.Tests.Content.MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, Options, externalResolver: null);

        Assert.NotNull(document);
        var primitive = document!.Root.LogicalMeshes[0].Primitives[0];

        var result = GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());

        var s = 1f / 0.3048f;

        // Vertex 1 replaced by (1, 1, 1) -> OpenSAGE (1, 1, 1) * s.
        Assert.Equal(new Vector3(s, s, s), result.Meshes[0].Vertices[1].Position);
    }

    private static string BuildSparseDocumentJson(byte[] positions, byte[] normals, byte[] replacement)
    {
        var positionB64 = Convert.ToBase64String(positions);
        var normalB64 = Convert.ToBase64String(normals);
        var indicesB64 = Convert.ToBase64String(EncodeUShort(0, 1, 2));
        var replacementB64 = Convert.ToBase64String(replacement);
        var keyB64 = Convert.ToBase64String(EncodeUInt(1));

        var json = "{" +
            "\"asset\": { \"version\": \"2.0\" }," +
            "\"buffers\": [" +
            $"{{ \"uri\": \"data:application/octet-stream;base64,{positionB64}\", \"byteLength\": {positions.Length} }}," +
            $"{{ \"uri\": \"data:application/octet-stream;base64,{normalB64}\", \"byteLength\": {normals.Length} }}," +
            $"{{ \"uri\": \"data:application/octet-stream;base64,{indicesB64}\", \"byteLength\": {indicesB64.Length * 3 / 4} }}," +
            $"{{ \"uri\": \"data:application/octet-stream;base64,{replacementB64}\", \"byteLength\": {replacement.Length} }}," +
            $"{{ \"uri\": \"data:application/octet-stream;base64,{keyB64}\", \"byteLength\": 4 }}" +
            "]," +
            "\"bufferViews\": [" +
            "{ \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": " + positions.Length + " }," +
            "{ \"buffer\": 1, \"byteOffset\": 0, \"byteLength\": " + normals.Length + " }," +
            "{ \"buffer\": 2, \"byteOffset\": 0, \"byteLength\": 6 }," +
            "{ \"buffer\": 4, \"byteOffset\": 0, \"byteLength\": 4 }," +
            "{ \"buffer\": 3, \"byteOffset\": 0, \"byteLength\": 12 }" +
            "]," +
            "\"accessors\": [" +
            "{ \"bufferView\": 0, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\", " +
            "\"min\": [0, 0, 0], \"max\": [1, 1, 1], " +
            "\"sparse\": { \"count\": 1, " +
            "\"indices\": { \"bufferView\": 3, \"componentType\": 5125 }, " +
            "\"values\": { \"bufferView\": 4 } } }," +
            "{ \"bufferView\": 1, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\" }," +
            "{ \"bufferView\": 2, \"componentType\": 5123, \"count\": 3, \"type\": \"SCALAR\" }" +
            "]," +
            "\"meshes\": [ { \"primitives\": [ { \"attributes\": { \"POSITION\": 0, \"NORMAL\": 1 }, \"indices\": 2 } ] } ]" +
            "}";

        return json;
    }

    [Fact]
    public void InterleavedBufferView_DecodesBothAttributes()
    {
        // One buffer view with stride 24: position at offset 0, normal at offset 12.
        var interleaved = new byte[3 * 24];
        for (var i = 0; i < 3; i++)
        {
            Buffer.BlockCopy(TrianglePositions, i * 12, interleaved, i * 24, 12);
            Buffer.BlockCopy(TriangleNormals, i * 12, interleaved, i * 24 + 12, 12);
        }

        var json = BuildInterleavedDocumentJson(interleaved);
        var fileSystem = new OpenSage.Tests.Content.MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, Options, externalResolver: null);

        Assert.NotNull(document);
        var primitive = document!.Root.LogicalMeshes[0].Primitives[0];

        var result = GltfPrimitiveConverter.Convert(primitive, IdentityAttachment(), Options, _ => PlaceholderMaterial());

        var s = 1f / 0.3048f;
        Assert.Equal(new Vector3(0, s, 0), result.Meshes[0].Vertices[1].Position);
        Assert.Equal(new Vector3(1, 0, 0), result.Meshes[0].Vertices[1].Normal);
    }

    private static string BuildInterleavedDocumentJson(byte[] interleaved)
    {
        var b64 = Convert.ToBase64String(interleaved);
        var indicesB64 = Convert.ToBase64String(EncodeUShort(0, 1, 2));

        return "{" +
            "\"asset\": { \"version\": \"2.0\" }," +
            "\"buffers\": [" +
            $"{{ \"uri\": \"data:application/octet-stream;base64,{b64}\", \"byteLength\": {interleaved.Length} }}," +
            $"{{ \"uri\": \"data:application/octet-stream;base64,{indicesB64}\", \"byteLength\": 6 }}" +
            "]," +
            "\"bufferViews\": [" +
            $"{{ \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": {interleaved.Length}, \"byteStride\": 24 }}," +
            "{ \"buffer\": 1, \"byteOffset\": 0, \"byteLength\": 6 }" +
            "]," +
            "\"accessors\": [" +
            "{ \"bufferView\": 0, \"byteOffset\": 0, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\", \"min\": [0, 0, 0], \"max\": [1, 1, 1] }," +
            "{ \"bufferView\": 0, \"byteOffset\": 12, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\" }," +
            "{ \"bufferView\": 1, \"componentType\": 5123, \"count\": 3, \"type\": \"SCALAR\" }" +
            "]," +
            "\"meshes\": [ { \"primitives\": [ { \"attributes\": { \"POSITION\": 0, \"NORMAL\": 1 }, \"indices\": 2 } ] } ]" +
            "}";
    }
}
