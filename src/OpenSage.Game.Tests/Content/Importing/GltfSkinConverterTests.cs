#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.Tests.Content;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public class GltfSkinConverterTests
{
    private static readonly GltfImportOptions Options = GltfImportOptions.Default;

    private static ImportedMaterial PlaceholderMaterial() => ImportedMaterial.CreatePbrMetallicRoughness(
        new ImportedPbrMetallicRoughness());

    private static (GltfScenePlan Plan, SharpGLTF.Schema2.ModelRoot Root) Load(string json)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, Options);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, Options, resolver);
        Assert.NotNull(document);
        var plan = GltfSceneConverter.CreatePlan("model.gltf", document!.Root, Options, document.HasDeclaredDefaultScene);
        return (plan, document.Root);
    }

    private static string B64(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static int B64Bytes(string base64)
    {
        var padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith("=", StringComparison.Ordinal) ? 1 : 0;
        return base64.Length / 4 * 3 - padding;
    }

    private static string B64U16(params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private const string TrianglePositions = "0 0 0 1 0 0 0 1 0";
    private const string TriangleNormals = "0 0 1 0 0 1 0 0 1";

    private static string BuildSkinJson(
        string jointsData,
        string weightsData,
        string? inverseBindData = null,
        string? jointsDeclaration = null,
        int jointCount = 2)
    {
        var ibmBuffer = inverseBindData != null
            ? $", {{ \"uri\": \"data:application/octet-stream;base64,{inverseBindData}\", \"byteLength\": {B64Bytes(inverseBindData)} }}"
            : "";
        var ibmView = inverseBindData != null
            ? ", { \"buffer\": 5, \"byteOffset\": 0, \"byteLength\": " + B64Bytes(inverseBindData) + " }"
            : "";
        var ibmAccessor = inverseBindData != null
            ? ", { \"bufferView\": 5, \"componentType\": 5126, \"count\": " + jointCount + ", \"type\": \"MAT4\" }"
            : "";
        var ibmSkinField = inverseBindData != null
            ? ", \"inverseBindMatrices\": 5"
            : "";

        return "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"buffers\": [" +
            "{ \"uri\": \"data:application/octet-stream;base64," + B64(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1) + "\", \"byteLength\": 36 }," +
            "{ \"uri\": \"data:application/octet-stream;base64," + B64(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0) + "\", \"byteLength\": 36 }," +
            "{ \"uri\": \"data:application/octet-stream;base64," + B64(0, 1, 2) + "\", \"byteLength\": 6 }," +
            "{ \"uri\": \"data:application/octet-stream;base64," + jointsData + "\", \"byteLength\": " + jointsData.Length * 3 / 4 + " }," +
            "{ \"uri\": \"data:application/octet-stream;base64," + weightsData + "\", \"byteLength\": " + weightsData.Length * 3 / 4 + " }" +
            ibmBuffer +
            "]," +
            "\"bufferViews\": [" +
            "{ \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": 36 }," +
            "{ \"buffer\": 1, \"byteOffset\": 0, \"byteLength\": 36 }," +
            "{ \"buffer\": 2, \"byteOffset\": 0, \"byteLength\": 6 }," +
            "{ \"buffer\": 3, \"byteOffset\": 0, \"byteLength\": " + jointsData.Length * 3 / 4 + " }," +
            "{ \"buffer\": 4, \"byteOffset\": 0, \"byteLength\": " + weightsData.Length * 3 / 4 + " }" +
            ibmView +
            "]," +
            "\"accessors\": [" +
            "{ \"bufferView\": 0, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\", \"min\": [0, 0, 0], \"max\": [1, 1, 1] }," +
            "{ \"bufferView\": 1, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\" }," +
            "{ \"bufferView\": 2, \"componentType\": 5123, \"count\": 3, \"type\": \"SCALAR\" }," +
            "{ \"bufferView\": 3, \"componentType\": 5123, \"count\": 3, \"type\": \"VEC4\" }," +
            "{ \"bufferView\": 4, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC4\" }" +
            ibmAccessor +
            "]," +
            "\"nodes\": [" +
            "{ \"name\": \"jointA\", \"children\": [1] }," +
            "{ \"name\": \"jointB\", \"translation\": [0, 1, 0] }," +
            "{ \"mesh\": 0, \"skin\": 0 }" +
            "]," +
            "\"skins\": [ { \"joints\": " + (jointsDeclaration ?? "[1, 0]") + ", \"skeleton\": 0" + ibmSkinField + " } ]," +
            "\"meshes\": [ { \"primitives\": [ { \"attributes\": { \"POSITION\": 0, \"NORMAL\": 1, \"JOINTS_0\": 3, \"WEIGHTS_0\": 4 }, \"indices\": 2 } ] } ]," +
            "\"scenes\": [ { \"nodes\": [0, 2] } ]," +
            "\"scene\": 0" +
            "}";
    }

    private static string IdentityIbm(int count)
    {
        var values = new float[count * 16];
        for (var i = 0; i < count; i++)
        {
            values[i * 16 + 0] = 1;
            values[i * 16 + 5] = 1;
            values[i * 16 + 10] = 1;
            values[i * 16 + 15] = 1;
        }

        return B64(values);
    }

    [Fact]
    public void IdentityInverseBinds_RemapJointsAndShareGeometry()
    {
        // Skin joint order (jointA, jointB) differs from scene node order (jointB first).
        var (plan, root) = Load(BuildSkinJson(
            jointsData: B64U16(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            weightsData: B64(0.75f, 0.25f, 0, 0, 0.75f, 0.25f, 0, 0, 0.75f, 0.25f, 0, 0)));

        var skin = GltfSkinConverter.ConvertSkin(root, plan, Options);

        // The scene has two roots (jointA tree, mesh node), so the skeleton inserts a
        // synthetic root as bone 0: jointA = 1, jointB = 2, mesh node = 3. Skin joint order
        // is (jointB, jointA), so the remap is [2, 1].
        Assert.Equal(new[] { 2, 1 }, skin.JointToBoneIndex);

        var primitive = root.LogicalMeshes[0].Primitives[0];
        var converted = GltfSkinConverter.ConvertSkinnedPrimitive(primitive, sourceNodeIndex: 2, skin, Options, _ => PlaceholderMaterial());

        var mesh = Assert.Single(converted.Meshes);
        Assert.True(mesh.IsSkinned);

        // Two influences: remapped bones (1, 0), weights sorted descending, primary equals
        // secondary geometry.
        var vertex = mesh.Vertices[0];
        Assert.Equal(1u, vertex.BoneIndex0);
        Assert.Equal(2u, vertex.BoneIndex1);
        Assert.Equal(0.75f, vertex.BoneWeight0, 4);
        Assert.Equal(0.25f, vertex.BoneWeight1, 4);
        Assert.Equal(vertex.Position, vertex.SecondaryPosition);
        Assert.Equal(vertex.Normal, vertex.SecondaryNormal);

        // Converted inverse binds are identity for a skin without the accessor.
        Assert.All(skin.Skeleton.Bones, bone => Assert.Equal(Matrix4x4.Identity, bone.InverseBindMatrix));
    }

    [Fact]
    public void NonIdentityInverseBinds_ConvertAndPassCpuDeformation()
    {
        // IBM for the first skin joint (jointB) is the inverse of its bind translation
        // (0, 1, 0) meters, so the rest-pose skinning matrix is exactly identity.
        var ibm = new float[32];
        ibm[0] = 1;
        ibm[5] = 1;
        ibm[10] = 1;
        ibm[13] = -1;
        ibm[15] = 1;
        ibm[16 + 0] = 1;
        ibm[16 + 5] = 1;
        ibm[16 + 10] = 1;
        ibm[16 + 15] = 1;

        var (plan, root) = Load(BuildSkinJson(
            jointsData: B64U16(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            weightsData: B64(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
            inverseBindData: B64(ibm)));

        var skin = GltfSkinConverter.ConvertSkin(root, plan, Options);

        // Converted IBM keeps unit scale and carries the translation in converted axes:
        // glTF (0, 0, -1) becomes OpenSAGE (-s, 0, 0).
        var s = 1f / 0.3048f;
        var jointB = skin.Skeleton.Bones[skin.JointToBoneIndex[0]];

        // Converted IBM keeps unit scale and carries glTF (0, -1, 0) to OpenSAGE (0, 0, -s).
        Assert.Equal(new Vector3(0, 0, -s), jointB.InverseBindMatrix.Translation);

        // CPU deformation reference: at the rest pose, skinning matrices are
        // IBM * relativeBind; jointB's relative bind is its bind translation.
        var primitive = root.LogicalMeshes[0].Primitives[0];
        var converted = GltfSkinConverter.ConvertSkinnedPrimitive(primitive, 2, skin, Options, _ => PlaceholderMaterial());
        var mesh = Assert.Single(converted.Meshes);

        // Row-vector composition of the synthetic root (identity), jointA (identity), and
        // jointB's bind translation (glTF +Y becomes OpenSAGE +Z, in world units).
        var jointBRelativeBind = Matrix4x4.CreateTranslation(0, 0, s);
        var skinning = jointB.InverseBindMatrix * jointBRelativeBind;

        foreach (var vertex in mesh.Vertices)
        {
            var expected = Vector3.Transform(vertex.Position, skinning);
            Assert.Equal(expected, vertex.Position);
        }
    }

    [Fact]
    public void Weights_PassThroughWithSortedInfluences()
    {
        var (plan, root) = Load(BuildSkinJson(
            jointsData: B64U16(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
            weightsData: B64(0.4f, 0.6f, 0, 0, 0.4f, 0.6f, 0, 0, 0.4f, 0.6f, 0, 0)));

        var skin = GltfSkinConverter.ConvertSkin(root, plan, Options);
        var primitive = root.LogicalMeshes[0].Primitives[0];
        var converted = GltfSkinConverter.ConvertSkinnedPrimitive(primitive, 2, skin, Options, _ => PlaceholderMaterial());

        // Slot order is swapped so weights sort descending; influences keep their joints.
        var vertex = Assert.Single(converted.Meshes).Vertices[0];
        Assert.Equal(0.6f, vertex.BoneWeight0, 5);
        Assert.Equal(0.4f, vertex.BoneWeight1, 5);
        Assert.Equal(2u, vertex.BoneIndex0);
        Assert.Equal(1u, vertex.BoneIndex1);
    }

    [Fact]
    public void WeightSumOutsideTolerance_FailsAtStrictParse()
    {
        // SharpGLTF strict validation rejects bad weight sums before import.
        var json = BuildSkinJson(
            jointsData: B64U16(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
            weightsData: B64(0.3f, 0.3f, 0, 0, 0.3f, 0.3f, 0, 0, 0.3f, 0.3f, 0, 0));
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, Options);

        Assert.Throws<InvalidDataException>(() =>
            GltfDocumentLoader.TryLoadDocument("model.gltf", context, Options, resolver));
    }

    [Fact]
    public void MeaningfulThirdInfluence_Throws()
    {
        var (plan, root) = Load(BuildSkinJson(
            jointsData: B64U16(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
            weightsData: B64(0.5f, 0.25f, 0.25f, 0, 0.5f, 0.25f, 0.25f, 0, 0.5f, 0.25f, 0.25f, 0)));

        var skin = GltfSkinConverter.ConvertSkin(root, plan, Options);

        Assert.Throws<NotSupportedException>(() =>
        {
            var primitive = root.LogicalMeshes[0].Primitives[0];
            GltfSkinConverter.ConvertSkinnedPrimitive(primitive, 2, skin, Options, _ => PlaceholderMaterial());
        });
    }

    [Fact]
    public void OutOfRangeJoint_FailsAtStrictParse()
    {
        // SharpGLTF strict validation rejects joint indices outside the skin before import.
        var json = BuildSkinJson(
            jointsData: B64U16(5, 0, 0, 0, 5, 0, 0, 0, 5, 0, 0, 0),
            weightsData: B64(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0));
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, Options);

        Assert.Throws<InvalidDataException>(() =>
            GltfDocumentLoader.TryLoadDocument("model.gltf", context, Options, resolver));
    }

    [Fact]
    public void UnreachableJoint_Throws()
    {
        // SharpGLTF validates joint-root consistency at parse, so scene-level
        // unreachability is exercised through a hand-built plan that omits a joint.
        var (plan, root) = Load(BuildSkinJson(
            jointsData: B64U16(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
            weightsData: B64(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)));

        var reducedPlan = new GltfScenePlan(
            plan.Skeleton,
            plan.Nodes.Where(n => n.SourceNodeIndex != 1).ToList(),
            plan.Attachments,
            plan.SkinnedNodes);

        Assert.Throws<InvalidDataException>(() => GltfSkinConverter.ConvertSkin(root, reducedPlan, Options));
    }
}
