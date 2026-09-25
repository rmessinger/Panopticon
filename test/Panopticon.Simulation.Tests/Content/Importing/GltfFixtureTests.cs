#nullable enable

using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public class GltfFixtureTests
{
    private static readonly GltfImportOptions Options = GltfImportOptions.Default;

    private static string FixtureRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null && current.Name != "bin")
            {
                current = current.Parent;
            }

            current = current?.Parent ?? throw new InvalidOperationException("Could not locate test output root.");

            return Path.Combine(current.FullName, "Content", "Importing", "Gltf", "Fixtures");
        }
    }

    private static AssetImportContext CreateFixtureContext()
    {
        return new AssetImportContext(new DiskFileSystem(FixtureRoot), language: null);
    }

    private static ImportedModel ImportModel(string fileName)
    {
        var importer = new GltfModelImporter(Options);
        return importer.ImportModel(fileName, CreateFixtureContext())
            ?? throw new InvalidOperationException($"Fixture import returned null: {fileName}");
    }

    private static ImportedAnimation ImportAnimation(string fileName, int index)
    {
        var importer = new GltfAnimationImporter(Options);
        return importer.ImportAnimation($"{fileName}#animation={index}", CreateFixtureContext())
            ?? throw new InvalidOperationException($"Fixture animation import returned null: {fileName}");
    }

    [Fact]
    public void StaticTriangle_ResourceForms_ProduceEquivalentDescriptors()
    {
        var external = ImportModel("external-triangle.gltf");
        var dataUri = ImportModel("data-uri-triangle.gltf");
        var glb = ImportModel("static-triangle.glb");

        foreach (var model in new[] { dataUri, glb })
        {
            Assert.Equal(external.SubObjects.Count, model.SubObjects.Count);
            var externalMesh = (ImportedMesh)external.SubObjects[0].RenderObject;
            var modelMesh = (ImportedMesh)model.SubObjects[0].RenderObject;

            Assert.Equal(
                externalMesh.Vertices.Select(v => v.Position).ToArray(),
                modelMesh.Vertices.Select(v => v.Position).ToArray());
            Assert.Equal(externalMesh.Indices, modelMesh.Indices);
            Assert.Equal(externalMesh.BoundingBox.Min, modelMesh.BoundingBox.Min);
            Assert.Equal(externalMesh.BoundingBox.Max, modelMesh.BoundingBox.Max);
        }
    }

    [Fact]
    public void AccessorLayouts_InterleavedSparseAndUintIndices_Decode()
    {
        var model = ImportModel("accessor-layouts.gltf");

        Assert.Equal(3, model.SubObjects.Count);

        // Sparse overlay replaces vertex 1 of the sparse mesh with (1, 1, 1).
        var sparseMesh = (ImportedMesh)model.SubObjects.First(s => s.DisplayName == "sparse_0").RenderObject;
        var s = 1f / 0.3048f;
        Assert.Contains(sparseMesh.Vertices, v => v.Position == new System.Numerics.Vector3(s, s, s));

        // Unsigned-int indices preserve triangle order on the uintIndices mesh.
        var uintMesh = (ImportedMesh)model.SubObjects.First(s2 => s2.DisplayName == "uintIndices_0").RenderObject;
        Assert.Equal(3, uintMesh.Indices.Count);

        // Interleaved normals decode as glTF +Z -> Panopticon +X.
        var interleavedMesh = (ImportedMesh)model.SubObjects.First(s3 => s3.DisplayName == "interleaved_0").RenderObject;
        Assert.All(interleavedMesh.Vertices, v => Assert.Equal(System.Numerics.Vector3.UnitX, v.Normal));
    }

    [Fact]
    public void RigidScene_ImportsNamedAnimation_WithLinearAndStepChannels()
    {
        var model = ImportModel("rigid-scene.gltf");
        var animation = ImportAnimation("rigid-scene.gltf", 0);

        Assert.Equal("armWave", animation.Name);
        Assert.Equal(TimeSpan.FromSeconds(2), animation.Duration);

        // Three linear translation channels plus one step rotation channel target "arm".
        Assert.Equal(4, animation.Channels.Count);
        Assert.Contains(animation.Channels, c => c.ChannelType == ImportedAnimationChannelType.Quaternion && c.Interpolation == ImportedAnimationInterpolation.Step);
        Assert.All(
            animation.Channels.Where(c => c.ChannelType != ImportedAnimationChannelType.Quaternion),
            c => Assert.Equal(ImportedAnimationInterpolation.Linear, c.Interpolation));
        Assert.All(animation.Channels, c =>
        {
            Assert.Equal(ImportedAnimationTargetKind.BoneName, c.Target.Kind);
            Assert.Equal("arm", c.Target.BoneName);
        });

        // The rig's one-meter translation converts to world units on the converted +X axis.
        var rig = model.Skeleton.Bones.Single(b => b.Name == "rig");
        Assert.Equal(1f / 0.3048f, rig.BindTranslation.X, 3);
    }

    [Fact]
    public void PbrMaterials_CarryTexturesAlphaAndDoubleSided()
    {
        var model = ImportModel("pbr-materials.gltf");

        var mesh = (ImportedMesh)model.SubObjects[0].RenderObject;
        var material = Assert.Single(mesh.Materials);
        var payload = material.PbrMetallicRoughness!;

        Assert.Equal(ImportedAlphaMode.Mask, payload.AlphaMode);
        Assert.Equal(0.4f, payload.AlphaCutoff, 4);
        Assert.True(payload.DoubleSided);
        Assert.Equal(0.2f, payload.MetallicFactor, 4);
        Assert.Equal(0.8f, payload.RoughnessFactor, 4);

        Assert.NotNull(payload.BaseColorTexture);
        Assert.Equal(ImportedImageSourceKind.VirtualPath, payload.BaseColorTexture!.ImageSource.Kind);

        Assert.NotNull(payload.EmissiveTexture);
        Assert.Equal(ImportedImageSourceKind.VirtualPath, payload.EmissiveTexture!.ImageSource.Kind);
        Assert.EndsWith(".jpg", payload.EmissiveTexture!.ImageSource.VirtualPath, StringComparison.Ordinal);

        // Both UV sets are present on the part.
        Assert.NotNull(mesh.MeshParts[0].TextureCoordinates0);
        Assert.NotNull(mesh.MeshParts[0].TextureCoordinates1);
    }

    [Fact]
    public void TwoInfluenceSkin_HasReorderedJointsAndNonIdentityBinds()
    {
        var model = ImportModel("two-influence-skin.gltf");

        Assert.All(model.Skeleton.Bones, bone =>
        {
            if (bone.Name is "jointA" or "jointB")
            {
                Assert.NotEqual(System.Numerics.Matrix4x4.Identity, bone.InverseBindMatrix);
            }
        });

        var mesh = model.SubObjects.Select(s => s.RenderObject).OfType<ImportedMesh>().Single(m => m.IsSkinned);
        var blended = mesh.Vertices.Single(v => v.BoneWeight1 > 0);

        // Vertex 0 blends skin joints 1 and 0 (jointA first, jointB second).
        var boneNames = model.Skeleton.Bones.Select(b => b.Name).ToArray();
        Assert.Equal("jointA", boneNames[(int)blended.BoneIndex0]);
        Assert.Equal("jointB", boneNames[(int)blended.BoneIndex1]);
        Assert.Equal(0.5f, blended.BoneWeight0, 4);
        Assert.Equal(0.5f, blended.BoneWeight1, 4);
        Assert.Equal(blended.Position, blended.SecondaryPosition);
    }

    [Fact]
    public void AnimatedSkin_ImportsSkinnedModelAndAnimation()
    {
        var model = ImportModel("animated-skin.glb");
        var animation = ImportAnimation("animated-skin.glb", 0);

        Assert.Equal("flex", animation.Name);
        Assert.Equal(TimeSpan.FromSeconds(2), animation.Duration);

        var quaternionChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.Quaternion);
        Assert.Equal(ImportedAnimationInterpolation.Linear, quaternionChannel.Interpolation);

        var translationChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationX);
        Assert.Equal(ImportedAnimationInterpolation.Step, translationChannel.Interpolation);
        Assert.All(animation.Channels, c => Assert.Equal("jointB", c.Target.BoneName));

        Assert.Contains(model.SubObjects, s => s.RenderObject is ImportedMesh { IsSkinned: true });
    }

    [Theory]
    [InlineData("unsupported-morph.gltf", "model", typeof(NotSupportedException), "morph")]
    [InlineData("unsupported-mirrored-node.gltf", "model", typeof(NotSupportedException), "mirror")]
    [InlineData("unsupported-required-extension.gltf", "model", typeof(System.IO.InvalidDataException), "model.gltf")]
    [InlineData("malformed-accessor.gltf", "model", typeof(System.IO.InvalidDataException), "model.gltf")]
    [InlineData("unsupported-cubicspline.gltf", "animation", typeof(NotSupportedException), "CUBICSPLINE")]
    [InlineData("unsupported-third-influence.gltf", "model", typeof(NotSupportedException), "third or fourth influence")]
    [InlineData("unsupported-multiple-skins.gltf", "model", typeof(NotSupportedException), "skins")]
    public void ExclusionFixtures_FailWithDocumentedTypeAndContext(
        string fileName,
        string stage,
        Type exceptionType,
        string contextFragment)
    {
        var context = CreateFixtureContext();

        Exception exception = stage == "model"
            ? Assert.ThrowsAny<Exception>(() => new GltfModelImporter(Options).ImportModel(fileName, context))
            : Assert.ThrowsAny<Exception>(() => new GltfAnimationImporter(Options).ImportAnimation($"{fileName}#animation=0", context));

        Assert.IsType(exceptionType, exception);

        if (contextFragment != "model.gltf")
        {
            Assert.Contains(contextFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}


