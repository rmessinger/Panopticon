#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public class GltfSceneConverterTests
{
    private static readonly GltfImportOptions Options = GltfImportOptions.Default;

    private static GltfScenePlan Convert(SharpGLTF.Schema2.ModelRoot root)
    {
        return GltfSceneConverter.CreatePlan("model.glb", root, Options, hasDeclaredDefaultScene: true);
    }

    private static SharpGLTF.Schema2.ModelRoot CreateModel()
    {
        var model = SharpGLTF.Schema2.ModelRoot.CreateModel();
        model.UseScene(0);
        return model;
    }

    private static SharpGLTF.Schema2.Scene DefaultScene(SharpGLTF.Schema2.ModelRoot model)
    {
        return model.LogicalScenes[0];
    }

    [Fact]
    public void NoDefaultScene_WithExactlyOneScene_IsSelected()
    {
        var model = CreateModel();
        DefaultScene(model).CreateNode("single");

        var plan = Convert(model);

        Assert.Single(plan.Skeleton.Bones);
    }

    [Fact]
    public void NoDefaultScene_WithMultipleScenes_Throws()
    {
        // UseScene would declare a default scene, so build the document from JSON.
        var json = "{ \"asset\": { \"version\": \"2.0\" }, \"scenes\": [ { \"nodes\": [0] }, { \"nodes\": [1] } ], \"nodes\": [ { \"name\": \"n1\" }, { \"name\": \"n2\" } ] }";
        var fileSystem = new Panopticon.Tests.Content.MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);

        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, Options, externalResolver: null);

        Assert.NotNull(document);
        Assert.False(document!.HasDeclaredDefaultScene);
        Assert.Throws<InvalidDataException>(() =>
            GltfSceneConverter.CreatePlan("model.gltf", document.Root, Options, document.HasDeclaredDefaultScene));
    }

    [Fact]
    public void NoScene_Throws()
    {
        var model = SharpGLTF.Schema2.ModelRoot.CreateModel();

        Assert.Throws<InvalidDataException>(() => Convert(model));
    }

    [Fact]
    public void MultipleRoots_InsertOneSyntheticRoot()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        scene.CreateNode("a");
        scene.CreateNode("b");

        var plan = Convert(model);

        Assert.Equal(3, plan.Skeleton.Bones.Count);
        Assert.Equal(-1, plan.Skeleton.Bones[0].ParentIndex);
        Assert.All(plan.Skeleton.Bones.Skip(1), bone => Assert.Equal(0, bone.ParentIndex));

        // The synthetic root exists only in the skeleton; node plans cover real nodes only.
        Assert.Equal(2, plan.Nodes.Count);
        Assert.All(plan.Nodes.Select(n => n.AuthoredName), name => Assert.NotNull(name));
    }

    [Fact]
    public void SingleRoot_DoesNotInsertSyntheticRoot()
    {
        var model = CreateModel();
        DefaultScene(model).CreateNode("only");

        var plan = Convert(model);

        var bone = Assert.Single(plan.Skeleton.Bones);
        Assert.Equal(-1, bone.ParentIndex);
        Assert.Equal("only", bone.Name);
    }

    [Fact]
    public void WalkOrder_IsParentBeforeChild_DepthFirst_SourceOrder()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var root = scene.CreateNode("root");
        root.CreateNode("childB").CreateNode("grandchild");
        root.CreateNode("childA");

        var plan = Convert(model);

        var names = plan.Skeleton.Bones.Select(b => b.Name).ToArray();
        Assert.Equal(new[] { "root", "childB", "grandchild", "childA" }, names);
    }

    [Fact]
    public void UnnamedNodes_GetDeterministicReservedNames()
    {
        var model = CreateModel();
        DefaultScene(model).CreateNode();

        var plan = Convert(model);

        var nodePlan = Assert.Single(plan.Nodes);
        Assert.Null(nodePlan.AuthoredName);
        Assert.StartsWith("__gltf_node_", nodePlan.EffectiveName);
        Assert.Equal(nodePlan.EffectiveName, plan.Skeleton.Bones[0].Name);
    }

    [Fact]
    public void UnnamedNodeName_CannotCollideWithAuthoredName()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var collision = scene.CreateNode("__gltf_node_1");
        collision.CreateNode();

        var plan = Convert(model);

        Assert.Equal("__gltf_node_1", plan.Skeleton.Bones[0].Name);
        Assert.NotEqual("__gltf_node_1", plan.Skeleton.Bones[1].Name);
        Assert.StartsWith("__gltf_node_", plan.Skeleton.Bones[1].Name);
    }

    [Fact]
    public void OneMeter_BecomesApproximately_3_28084_WorldUnits()
    {
        var model = CreateModel();
        var node = DefaultScene(model).CreateNode("m");
        node.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: null,
            rotation: null,
            translation: new Vector3(0, 0, 1));

        var plan = Convert(model);

        // glTF +Z (asset front) becomes Panopticon +X (object forward).
        var bone = Assert.Single(plan.Skeleton.Bones);
        Assert.Equal(1f / 0.3048f, bone.BindTranslation.X, 4);
        Assert.Equal(0, bone.BindTranslation.Y);
        Assert.Equal(0, bone.BindTranslation.Z);
    }

    [Fact]
    public void Basis_ConvertsGltfAxes()
    {
        var model = CreateModel();
        var node = DefaultScene(model).CreateNode("axes");
        node.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: null,
            rotation: null,
            translation: new Vector3(2, 4, 6));

        var plan = Convert(model);

        var bone = Assert.Single(plan.Skeleton.Bones);

        // glTF (x, y, z) meters -> Panopticon (z, x, y) world units at s = 1/0.3048 per meter.
        var s = 1f / 0.3048f;
        Assert.Equal(6 * s, bone.BindTranslation.X, 2);
        Assert.Equal(2 * s, bone.BindTranslation.Y, 2);
        Assert.Equal(4 * s, bone.BindTranslation.Z, 2);
    }

    [Fact]
    public void Rotation_ConvertsAroundBasis()
    {
        var model = CreateModel();
        var node = DefaultScene(model).CreateNode("rot");
        var quarterTurnAroundGltfUp = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        node.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: Vector3.One,
            rotation: quarterTurnAroundGltfUp,
            translation: Vector3.Zero);

        var plan = Convert(model);

        var bone = Assert.Single(plan.Skeleton.Bones);
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        Assert.Equal(expected.X, bone.BindRotation.X, 4);
        Assert.Equal(expected.Y, bone.BindRotation.Y, 4);
        Assert.Equal(expected.Z, bone.BindRotation.Z, 4);
        Assert.Equal(expected.W, bone.BindRotation.W, 4);
    }

    [Fact]
    public void MatrixNode_ConvertsLikeTrsNode()
    {
        var model = CreateModel();
        var node = DefaultScene(model).CreateNode("matrix");
        var matrix = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f)) *
                     Matrix4x4.CreateTranslation(1, 2, 3);
        node.LocalMatrix = matrix;

        var plan = Convert(model);

        var trsModel = CreateModel();
        var trsNode = DefaultScene(trsModel).CreateNode("trs");
        trsNode.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: Vector3.One,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f),
            translation: new Vector3(1, 2, 3));

        var trsPlan = GltfSceneConverter.CreatePlan("model.glb", trsModel, Options, hasDeclaredDefaultScene: true);

        Assert.Equal(trsPlan.Skeleton.Bones[0].BindTranslation, plan.Skeleton.Bones[0].BindTranslation);
        Assert.Equal(trsPlan.Skeleton.Bones[0].BindRotation, plan.Skeleton.Bones[0].BindRotation);
    }

    [Fact]
    public void UniformScale_IsAbsorbedIntoDescendantTranslations()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var root = scene.CreateNode("root");
        root.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: new Vector3(2, 2, 2),
            rotation: Quaternion.Identity,
            translation: Vector3.Zero);
        var child = root.CreateNode("child");
        child.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: Vector3.One,
            rotation: Quaternion.Identity,
            translation: new Vector3(1, 0, 0));

        var plan = Convert(model);

        // glTF +X becomes Panopticon +Y, then the parent's uniform scale doubles it.
        Assert.Equal(new Vector3(0, 2f / 0.3048f, 0), plan.Skeleton.Bones[1].BindTranslation);
    }

    [Fact]
    public void UniformScaleOnMeshNode_RecordsGeometryTransform()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var meshNode = scene.CreateNode("meshNode");
        meshNode.Mesh = model.CreateMesh("mesh");
        meshNode.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: new Vector3(3, 3, 3),
            rotation: Quaternion.Identity,
            translation: Vector3.Zero);

        var plan = Convert(model);

        var attachment = Assert.Single(plan.Attachments);
        Assert.Equal(0, attachment.BoneIndex);
        Assert.Equal(Matrix4x4.CreateScale(3), attachment.GeometryTransform);
        Assert.False(attachment.RequiresTangentReorthogonalization);
    }

    [Fact]
    public void NonUniformScale_OnLeafUnskinnedMeshNode_IsAccepted()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var meshNode = scene.CreateNode("meshNode");
        meshNode.Mesh = model.CreateMesh("mesh");
        meshNode.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: new Vector3(1, 2, 3),
            rotation: Quaternion.Identity,
            translation: Vector3.Zero);

        var plan = Convert(model);

        var attachment = Assert.Single(plan.Attachments);
        Assert.Equal(Matrix4x4.CreateScale(1, 2, 3), attachment.GeometryTransform);
        Assert.True(attachment.RequiresTangentReorthogonalization);

        Matrix4x4.Invert(Matrix4x4.CreateScale(1, 2, 3), out var inverseScale);
        var expectedNormalMatrix = Matrix4x4.Transpose(inverseScale);
        Assert.Equal(expectedNormalMatrix, attachment.NormalMatrix);
    }

    [Fact]
    public void NonUniformScale_OnInnerNode_Throws()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var inner = scene.CreateNode("inner");
        inner.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: new Vector3(1, 2, 1),
            rotation: Quaternion.Identity,
            translation: Vector3.Zero);
        inner.CreateNode("child");

        Assert.Throws<NotSupportedException>(() => Convert(model));
    }

    [Fact]
    public void MirroredTransform_Throws()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var node = scene.CreateNode("mirror");
        node.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: new Vector3(-1, 1, 1),
            rotation: Quaternion.Identity,
            translation: Vector3.Zero);

        Assert.Throws<NotSupportedException>(() => Convert(model));
    }

    [Fact]
    public void Scale_OnJointPath_Throws()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var skin = model.CreateSkin("skin");
        var joint = scene.CreateNode("joint");
        skin.BindJoints(new[] { joint });
        joint.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            scale: new Vector3(2, 2, 2),
            rotation: Quaternion.Identity,
            translation: Vector3.Zero);

        Assert.Throws<NotSupportedException>(() => Convert(model));
    }

    [Fact]
    public void SkinnedMeshNode_IsMarkedNotAttached()
    {
        var model = CreateModel();
        var scene = DefaultScene(model);
        var skin = model.CreateSkin("skin");
        var joint = scene.CreateNode("joint");
        skin.BindJoints(new[] { joint });
        var meshNode = scene.CreateNode("skinned");
        meshNode.Mesh = model.CreateMesh("mesh");
        meshNode.Skin = skin;

        var plan = Convert(model);

        Assert.Empty(plan.Attachments);
        var skinned = Assert.Single(plan.SkinnedNodes);
        Assert.Equal(meshNode.LogicalIndex, skinned.SourceNodeIndex);
        Assert.Equal(0, skinned.SkinIndex);
    }

    [Fact]
    public void BoundsCalculator_ComputesBoxAndSphere()
    {
        var positions = new[]
        {
            new Vector3(-1, -2, -3),
            new Vector3(1, 2, 3),
        };

        var box = GltfBoundsCalculator.ComputeBox(positions);
        var sphere = GltfBoundsCalculator.ComputeSphere(positions);

        Assert.Equal(new Vector3(-1, -2, -3), box.Min);
        Assert.Equal(new Vector3(1, 2, 3), box.Max);
        Assert.Equal(Vector3.Zero, sphere.Center);
        Assert.Equal(new Vector3(1, 2, 3).Length(), sphere.Radius, 4);
    }
}
