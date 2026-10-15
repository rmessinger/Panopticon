using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace Panopticon.Tests.Content.Importing;

/// <summary>
/// Deterministic generator for the committed glTF fixture matrix: fixed geometry, ordering,
/// encoder settings, and metadata; repeated generation is byte-stable. Covers everything
/// SharpGLTF Core can author (static triangles in three resource forms, the animated rigid
/// scene, the PBR material matrix). Accessor-layout, skin, and exclusion fixtures are
/// hand-authored static files documented in the fixture README.
/// </summary>
internal static class GltfFixtureGenerator
{
    public const string GeneratorTag = "Panopticon glTF fixture generator 1";

    private static readonly Vector3[] TrianglePositions =
    [
        new(0, 0, 0),
        new(1, 0, 0),
        new(0, 1, 0),
    ];

    private static readonly Vector3[] TriangleNormals =
    [
        new(0, 0, 1),
        new(0, 0, 1),
        new(0, 0, 1),
    ];

    private static readonly Vector2[] TriangleUvs =
    [
        new(0.25f, 0.5f),
        new(0.75f, 0.25f),
        new(0.5f, 0.75f),
    ];

    public static void GenerateAll(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        WriteExternalTriangle(outputDirectory);
        WriteDataUriTriangle(outputDirectory);
        WriteGlbTriangle(outputDirectory);
        WriteRigidScene(outputDirectory);
        WritePbrMaterials(outputDirectory);
        WriteTwoInfluenceSkin(outputDirectory);
        WriteAnimatedSkin(outputDirectory);
    }

    private static ModelRoot CreateModel(string name)
    {
        var model = ModelRoot.CreateModel();
        model.Asset.Copyright = "Project-authored Panopticon test fixture";
        model.Asset.Generator = GeneratorTag;
        return model;
    }

    private static void AddTriangle(ModelRoot model, Node node, bool withUv1 = false)
    {
        var mesh = model.CreateMesh(node.Name + "Mesh");
        node.Mesh = mesh;
        var primitive = mesh.CreatePrimitive();
        AddTriangleGeometry(model, primitive, withUv1);
    }

    private static void AddTriangleGeometry(ModelRoot model, MeshPrimitive primitive, bool withUv1 = false)
    {
        primitive.SetVertexAccessor("POSITION", CreateVertexAccessor(model, "POSITION", TrianglePositions));
        primitive.SetVertexAccessor("NORMAL", CreateVertexAccessor(model, "NORMAL", TriangleNormals));
        primitive.SetVertexAccessor("TEXCOORD_0", CreateVertexAccessor(model, "TEXCOORD_0", TriangleUvs));
        if (withUv1)
        {
            primitive.SetVertexAccessor("TEXCOORD_1", CreateVertexAccessor(model, "TEXCOORD_1", TriangleUvs));
        }

        primitive.SetIndexAccessor(CreateIndexAccessor(model, [0, 1, 2]));
    }

    private static Accessor CreateVertexAccessor(ModelRoot model, string name, float[] floatData, int itemCount)
    {
        var bytes = new byte[floatData.Length * 4];
        System.Buffer.BlockCopy(floatData, 0, bytes, 0, bytes.Length);
        var bufferView = model.UseBufferView(bytes);
        var accessor = model.CreateAccessor(name);
        accessor.SetVertexData(
            bufferView,
            0,
            itemCount,
            new AttributeFormat(floatData.Length / itemCount == 3 ? DimensionType.VEC3 : DimensionType.VEC2, EncodingType.FLOAT));
        return accessor;
    }

    private static Accessor CreateVertexAccessor(ModelRoot model, string name, Vector3[] values)
    {
        var floatData = new float[values.Length * 3];
        for (var i = 0; i < values.Length; i++)
        {
            floatData[i * 3] = values[i].X;
            floatData[i * 3 + 1] = values[i].Y;
            floatData[i * 3 + 2] = values[i].Z;
        }

        return CreateVertexAccessor(model, name, floatData, values.Length);
    }

    private static Accessor CreateVertexAccessor(ModelRoot model, string name, Vector2[] values)
    {
        var floatData = new float[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            floatData[i * 2] = values[i].X;
            floatData[i * 2 + 1] = values[i].Y;
        }

        return CreateVertexAccessor(model, name, floatData, values.Length);
    }

    private static Accessor CreateIndexAccessor(ModelRoot model, int[] indices)
    {
        var shorts = new ushort[indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            shorts[i] = (ushort)indices[i];
        }

        var bytes = new byte[shorts.Length * 2];
        System.Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
        var bufferView = model.UseBufferView(bytes);
        var accessor = model.CreateAccessor("INDICES");
        accessor.SetIndexData(bufferView, 0, shorts.Length, IndexEncodingType.UNSIGNED_SHORT);
        return accessor;
    }

    private static void WriteExternalTriangle(string outputDirectory)
    {
        var model = CreateModel("external-triangle");
        var scene = model.UseScene(0);
        scene.Name = "triangle";
        var node = scene.CreateNode("hull");

        AddTriangle(model, node);
        model.Save(Path.Combine(outputDirectory, "external-triangle.gltf"));
    }

    private static void WriteDataUriTriangle(string outputDirectory)
    {
        var model = CreateModel("data-uri-triangle");
        var scene = model.UseScene(0);
        scene.Name = "triangle";
        var node = scene.CreateNode("hull");

        AddTriangle(model, node);
        model.MergeBuffers();
        model.Save(Path.Combine(outputDirectory, "data-uri-triangle.gltf"));
    }

    private static void WriteGlbTriangle(string outputDirectory)
    {
        var model = CreateModel("static-triangle");
        var scene = model.UseScene(0);
        scene.Name = "triangle";
        var node = scene.CreateNode("hull");

        AddTriangle(model, node);
        model.SaveGLB(Path.Combine(outputDirectory, "static-triangle.glb"));
    }

    private static void WriteRigidScene(string outputDirectory)
    {
        var model = CreateModel("rigid-scene");
        var scene = model.UseScene(0);
        scene.Name = "rigid";

        var root = scene.CreateNode("rig");
        root.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: Vector3.One,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 8),
            translation: new Vector3(0, 0, 1));

        var arm = root.CreateNode("arm");
        arm.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: Vector3.One,
            rotation: Quaternion.Identity,
            translation: new Vector3(0.5f, 0, 0));

        AddTriangle(model, arm);

        var animation = model.CreateAnimation("armWave");
        animation.CreateTranslationChannel(
            arm,
            new SortedDictionary<float, Vector3>
            {
                [0f] = new Vector3(0.5f, 0, 0),
                [1f] = new Vector3(0.5f, 1, 0),
                [2f] = new Vector3(0.5f, 0, 0),
            },
            linear: true);
        animation.CreateRotationChannel(
            arm,
            new SortedDictionary<float, Quaternion>
            {
                [0f] = Quaternion.Identity,
                [1f] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4),
                [2f] = Quaternion.Identity,
            },
            linear: false);

        model.Save(Path.Combine(outputDirectory, "rigid-scene.gltf"));
    }

    private static (ModelRoot Model, Node JointA, Node JointB) CreateSkinModel(string name)
    {
        var model = CreateModel(name);
        var scene = model.UseScene(0);

        // Skin joint order is deliberately reversed relative to scene order, and both joints
        // carry non-identity rest transforms.
        var jointA = scene.CreateNode("jointA");
        jointA.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: Vector3.One,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 6),
            translation: new Vector3(0.2f, 0, 0));
        var jointB = jointA.CreateNode("jointB");
        jointB.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: Vector3.One,
            rotation: Quaternion.Identity,
            translation: new Vector3(0, 0.4f, 0));

        return (model, jointA, jointB);
    }

    private static void AddSkinnedTriangle(ModelRoot model, Node meshNode, Skin skin)
    {
        var mesh = model.CreateMesh("skinnedMesh");
        meshNode.Mesh = mesh;
        meshNode.Skin = skin;
        var primitive = mesh.CreatePrimitive();
        AddTriangleGeometry(model, primitive);

        // Vertex 0 blends both joints; vertices 1 and 2 follow jointB only.
        primitive.SetVertexAccessor("JOINTS_0", CreateJointsAccessor(model, [(1, 0), (1, 0), (1, 0)]));
        primitive.SetVertexAccessor("WEIGHTS_0", CreateWeightsAccessor(model, [(0.5f, 0.5f), (1f, 0f), (1f, 0f)]));
    }

    private static Accessor CreateJointsAccessor(ModelRoot model, (int A, int B)[] joints)
    {
        var shorts = new ushort[joints.Length * 4];
        for (var i = 0; i < joints.Length; i++)
        {
            shorts[i * 4] = (ushort)joints[i].A;
            shorts[i * 4 + 1] = (ushort)joints[i].B;
        }

        var bytes = new byte[shorts.Length * 2];
        System.Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
        var bufferView = model.UseBufferView(bytes);
        var accessor = model.CreateAccessor("JOINTS_0");
        accessor.SetVertexData(bufferView, 0, joints.Length, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_SHORT));
        return accessor;
    }

    private static Accessor CreateWeightsAccessor(ModelRoot model, (float A, float B)[] weights)
    {
        var floats = new float[weights.Length * 4];
        for (var i = 0; i < weights.Length; i++)
        {
            floats[i * 4] = weights[i].A;
            floats[i * 4 + 1] = weights[i].B;
        }

        var bytes = new byte[floats.Length * 4];
        System.Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        var bufferView = model.UseBufferView(bytes);
        var accessor = model.CreateAccessor("WEIGHTS_0");
        accessor.SetVertexData(bufferView, 0, weights.Length, new AttributeFormat(DimensionType.VEC4, EncodingType.FLOAT));
        return accessor;
    }

    private static void WriteTwoInfluenceSkin(string outputDirectory)
    {
        var (model, jointA, jointB) = CreateSkinModel("two-influence-skin");
        var meshNode = model.LogicalScenes[0].CreateNode("skinned");

        // Bind order reversed: jointB is skin joint 0.
        var skin = model.CreateSkin("skin");
        skin.BindJoints(Matrix4x4.Identity, [jointB, jointA]);
        AddSkinnedTriangle(model, meshNode, skin);

        model.Save(Path.Combine(outputDirectory, "two-influence-skin.gltf"));
    }

    private static void WriteAnimatedSkin(string outputDirectory)
    {
        var (model, jointA, jointB) = CreateSkinModel("animated-skin");
        var meshNode = model.LogicalScenes[0].CreateNode("skinned");

        var skin = model.CreateSkin("skin");
        skin.BindJoints(Matrix4x4.Identity, [jointB, jointA]);
        AddSkinnedTriangle(model, meshNode, skin);

        var animation = model.CreateAnimation("flex");
        animation.CreateRotationChannel(
            jointB,
            new SortedDictionary<float, Quaternion>
            {
                [0f] = Quaternion.Identity,
                [1f] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 6),
                [2f] = Quaternion.Identity,
            },
            linear: true);
        animation.CreateTranslationChannel(
            jointB,
            new SortedDictionary<float, Vector3>
            {
                [0f] = new Vector3(0, 0.4f, 0),
                [1f] = new Vector3(0, 0.6f, 0),
                [2f] = new Vector3(0, 0.4f, 0),
            },
            linear: false);

        model.SaveGLB(Path.Combine(outputDirectory, "animated-skin.glb"));
    }

    private static void WritePbrMaterials(string outputDirectory)
    {
        var model = CreateModel("pbr-materials");
        var scene = model.UseScene(0);
        scene.Name = "materials";

        var baseColorImage = model.CreateImage("baseColor");
        baseColorImage.Content = new MemoryImage(PbrFixtureImages.BaseColorPng);
        var emissiveImage = model.CreateImage("emissive");
        emissiveImage.Content = new MemoryImage(PbrFixtureImages.EmissiveJpeg);

        var material = model.CreateMaterial("textured");
        material.InitializePBRMetallicRoughness();
        if (material.FindChannel("BaseColor") is { } baseColorChannel)
        {
            baseColorChannel.SetTexture(0, baseColorImage);
        }

        if (material.FindChannel("MetallicRoughness") is { } metallicRoughnessChannel)
        {
            metallicRoughnessChannel.SetFactor("MetallicFactor", 0.2f);
            metallicRoughnessChannel.SetFactor("RoughnessFactor", 0.8f);
        }

        if (material.FindChannel("Emissive") is { } emissiveChannel)
        {
            emissiveChannel.SetTexture(0, emissiveImage);
        }

        material.DoubleSided = true;
        material.Alpha = AlphaMode.MASK;
        material.AlphaCutoff = 0.4f;

        var node = scene.CreateNode("materialsNode");
        var mesh = model.CreateMesh("materialsMesh");
        node.Mesh = mesh;
        var primitive = mesh.CreatePrimitive();
        AddTriangleGeometry(model, primitive, withUv1: true);
        primitive.Material = material;

        model.MergeBuffers();
        model.Save(Path.Combine(outputDirectory, "pbr-materials.gltf"));
        File.WriteAllBytes(Path.Combine(outputDirectory, "base-color.png"), PbrFixtureImages.BaseColorPng);
        File.WriteAllBytes(Path.Combine(outputDirectory, "emissive.jpg"), PbrFixtureImages.EmissiveJpeg);
    }
}
