using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.Content;
using OpenSage.Content.Importing;
using OpenSage.Content.Loaders;
using OpenSage.Graphics;
using OpenSage.Graphics.Rendering;
using OpenSage.Graphics.Shaders;
using OpenSage.IO;
using OpenSage.Mathematics;
using OpenSage.Mods.BuiltIn;
using OpenSage.Rendering;
using Veldrid;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public sealed class ImportedModelMaterializerTests
{
    private static ImportedTextureMapping CreateDefaultTextureMapping()
    {
        return new ImportedTextureMapping(
            ImportedTextureMappingType.Uv,
            Vector2.Zero,
            Vector2.One,
            Vector2.Zero,
            Vector2.One,
            Vector2.One,
            Vector2.Zero,
            Vector2.One,
            speed: 0.1f,
            fps: 1.0f,
            log2Width: 1,
            stepsPerSecond: 0.0f);
    }

    private static ImportedMaterial CreateFixedFunctionMaterial(
        params ImportedMaterialProperty[] properties)
    {
        return new ImportedMaterial(
            ImportedMaterial.FixedFunctionFamily,
            ImportedCullMode.Back,
            depthWriteEnabled: true,
            depthComparison: ImportedDepthComparison.LessEqual,
            blendEnabled: false,
            sourceBlendFactor: ImportedBlendFactor.One,
            destinationColorBlendFactor: ImportedBlendFactor.Zero,
            destinationAlphaBlendFactor: ImportedBlendFactor.Zero,
            new ImportedFixedFunctionShading(
                diffuseLightingType: ImportedDiffuseLightingType.Modulate,
                specularEnabled: false,
                texturingEnabled: true,
                secondaryTextureColorBlend: ImportedSecondaryTextureBlend.Disable,
                secondaryTextureAlphaBlend: ImportedSecondaryTextureBlend.Disable,
                alphaTest: false,
                ambient: new Vector3(0.1f, 0.2f, 0.3f),
                diffuse: new Vector3(0.4f, 0.5f, 0.6f),
                specular: Vector3.Zero,
                emissive: Vector3.Zero,
                shininess: 1.0f,
                opacity: 1.0f,
                textureStageCount: 1,
                CreateDefaultTextureMapping(),
                CreateDefaultTextureMapping()),
            properties);
    }

    private static ImportedMesh CreateStaticMesh(
        ImportedMaterialProperty[]? materialProperties = null,
        uint boneIndex0 = 0,
        uint boneIndex1 = 0,
        float boneWeight0 = 0.0f,
        float boneWeight1 = 0.0f,
        bool isSkinned = false)
    {
        var vertices = new[]
        {
            new ImportedVertex(
                new Vector3(0, 0, 0),
                Vector3.UnitZ,
                boneIndex0: boneIndex0,
                boneIndex1: boneIndex1,
                boneWeight0: boneWeight0,
                boneWeight1: boneWeight1),
            new ImportedVertex(
                new Vector3(1, 0, 0),
                Vector3.UnitZ,
                boneIndex0: boneIndex0,
                boneIndex1: boneIndex1,
                boneWeight0: boneWeight0,
                boneWeight1: boneWeight1),
            new ImportedVertex(
                new Vector3(0, 1, 0),
                Vector3.UnitZ,
                boneIndex0: boneIndex0,
                boneIndex1: boneIndex1,
                boneWeight0: boneWeight0,
                boneWeight1: boneWeight1),
        };

        var texCoords = new[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
        };

        return new ImportedMesh(
            vertices,
            [0, 1, 2],
            new AxisAlignedBoundingBox(new Vector3(-1, -2, -3), new Vector3(4, 5, 6)),
            new BoundingSphere(new Vector3(1, 2, 3), 2.5f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: isSkinned,
            materials: [CreateFixedFunctionMaterial(materialProperties ?? [])],
            meshParts:
            [
                new ImportedMeshPart(
                    textureCoordinates0: texCoords,
                    textureCoordinates1: null,
                    startIndex: 0,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 0),
            ]);
    }

    private static ImportedModel CreateModel(
        ImportedRenderObject renderObject,
        int parentBoneIndex = 0)
    {
        return new ImportedModel(
            "TestModel",
            new ImportedSkeleton(
                "TestHierarchy",
                [
                    new ImportedBone("root", -1, Vector3.Zero, Quaternion.Identity),
                    new ImportedBone("turret", 0, new Vector3(1, 2, 3), Quaternion.Identity),
                ]),
            [new ImportedSubObject("TestContainer.MeshA", "MeshA", parentBoneIndex, renderObject)]);
    }

    [GraphicsFact]
    public void StaticModel_Materializes()
    {
        using var graphics = new TestGraphicsContext();

        var model = ImportedModelMaterializer.MaterializeModel(
            CreateModel(CreateStaticMesh()),
            graphics.Context);

        Assert.Equal("TestModel", model.Name);
        Assert.False(model.HasSkinnedMeshes);
        Assert.Equal("TestHierarchy", model.BoneHierarchy.Name);
        Assert.Equal(2, model.BoneHierarchy.Bones.Length);
        Assert.Null(model.BoneHierarchy.Bones[0].Parent);
        Assert.Same(model.BoneHierarchy.Bones[0], model.BoneHierarchy.Bones[1].Parent);
        Assert.Equal(new Vector3(1, 2, 3), model.BoneHierarchy.Bones[1].Transform.Translation);

        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal("TestContainer.MeshA", subObject.FullName);
        Assert.Equal("MeshA", subObject.Name);
        Assert.Same(model.BoneHierarchy.Bones[0], subObject.Bone);

        var mesh = Assert.IsType<ModelMesh>(subObject.RenderObject);
        Assert.Equal("MeshA", mesh.Name);
        Assert.False(mesh.Skinned);
        Assert.False(mesh.Hidden);
        Assert.Equal(new Vector3(-1, -2, -3), mesh.BoundingBox.Min);
        Assert.Equal(new Vector3(4, 5, 6), mesh.BoundingBox.Max);
        Assert.NotNull(mesh.VertexBuffer);
        Assert.NotNull(mesh.MeshConstantsBuffer);

        var meshPart = Assert.Single(mesh.MeshParts);
        Assert.Equal(0u, meshPart.StartIndex);
        Assert.Equal(3u, meshPart.IndexCount);
        Assert.False(meshPart.BlendEnabled);
        Assert.NotNull(meshPart.TexCoordVertexBuffer);
        Assert.NotNull(meshPart.Material.ForwardPass);
    }

    [GraphicsFact]
    public void SkinnedModel_Materializes()
    {
        using var graphics = new TestGraphicsContext();

        var model = ImportedModelMaterializer.MaterializeModel(
            CreateModel(CreateStaticMesh(
                boneIndex0: 1,
                boneIndex1: 0,
                boneWeight0: 0.7f,
                boneWeight1: 0.3f,
                isSkinned: true)),
            graphics.Context);

        Assert.True(model.HasSkinnedMeshes);

        var mesh = Assert.IsType<ModelMesh>(Assert.Single(model.SubObjects).RenderObject);
        Assert.True(mesh.Skinned);
    }

    [GraphicsFact]
    public void Box_Materializes()
    {
        using var graphics = new TestGraphicsContext();

        var model = ImportedModelMaterializer.MaterializeModel(
            CreateModel(new ImportedBox(new Vector3(1, 2, 3), new Vector3(4, 5, 6))),
            graphics.Context);

        var subObject = Assert.Single(model.SubObjects);
        Assert.Equal("TestContainer.MeshA", subObject.FullName);

        var box = Assert.IsType<ModelBox>(subObject.RenderObject);
        Assert.False(box.Hidden);
        Assert.Equal(new Vector3(-3, -3, -3), box.BoundingBox.Min);
        Assert.Equal(new Vector3(5, 7, 9), box.BoundingBox.Max);
        Assert.Equal(new Vector3(4, 5, 6).Length(), box.BoundingSphere.Radius);
    }

    [GraphicsFact]
    public void FixedFunctionMaterial_MapsTextureNamesThroughAssetStore()
    {
        using var graphics = new TestGraphicsContext();

        var model = ImportedModelMaterializer.MaterializeModel(
            CreateModel(CreateStaticMesh(
                materialProperties:
                [
                    ImportedMaterialProperty.FromTextureName("Texture0", "MissingTexture"),
                ])),
            graphics.Context);

        var mesh = Assert.IsType<ModelMesh>(Assert.Single(model.SubObjects).RenderObject);
        var meshPart = Assert.Single(mesh.MeshParts);
        Assert.NotNull(meshPart.Material.ForwardPass);
        Assert.NotNull(meshPart.MaterialBlend.ForwardPass);
    }

    [GraphicsFact]
    public void ShaderMaterial_AllPropertyKinds_ReachResourceSet()
    {
        using var graphics = new TestGraphicsContext();

        var shaderResources = new TestShaderMaterialResources(graphics.ShaderSetStore);
        using var _ = shaderResources;

        var material = new ImportedMaterial(
            "Simple",
            ImportedCullMode.Back,
            depthWriteEnabled: true,
            depthComparison: ImportedDepthComparison.LessEqual,
            blendEnabled: false,
            sourceBlendFactor: ImportedBlendFactor.One,
            destinationColorBlendFactor: ImportedBlendFactor.Zero,
            destinationAlphaBlendFactor: ImportedBlendFactor.Zero,
            fixedFunctionShading: null,
            properties:
            [
                ImportedMaterialProperty.FromBool("BoolValue", true),
                ImportedMaterialProperty.FromFloat("FloatValue", 1.5f),
                ImportedMaterialProperty.FromVector2("Vector2Value", new Vector2(1, 2)),
                ImportedMaterialProperty.FromVector3("Vector3Value", new Vector3(3, 4, 5)),
                ImportedMaterialProperty.FromVector4("Vector4Value", new Vector4(6, 7, 8, 9)),
                ImportedMaterialProperty.FromInt32("IntValue", 42),
                ImportedMaterialProperty.FromTextureName("Texture_0", "MissingTexture"),
            ]);

        var cachedMaterial = shaderResources.GetCachedMaterial(material, graphics.Context);

        Assert.NotNull(cachedMaterial);

        // A second materialization with identical properties hits the cache.
        Assert.Same(cachedMaterial, shaderResources.GetCachedMaterial(material, graphics.Context));
    }

    [GraphicsFact]
    public void MaterializeSkeleton_ProducesHierarchy()
    {
        using var graphics = new TestGraphicsContext();

        var skeleton = new ImportedSkeleton(
            "TestHierarchy",
            [new ImportedBone("root", -1, new Vector3(1, 2, 3), Quaternion.Identity)]);

        var hierarchy = ImportedModelMaterializer.MaterializeSkeleton(skeleton);

        Assert.Equal("TestHierarchy", hierarchy.Name);
        var bone = Assert.Single(hierarchy.Bones);
        Assert.Equal("root", bone.Name);
        Assert.Equal(new Vector3(1, 2, 3), bone.Transform.Translation);
    }

    /// <summary>
    /// Shader set reusing the real compiled "Simple" shaders with a test constants layout that
    /// covers every material-property kind.
    /// </summary>
    private sealed class TestShaderMaterialResources : ShaderMaterialShaderResources
    {
        public TestShaderMaterialResources(ShaderSetStore store)
            : base(store, "Simple", CreateMaterialResourceBindings)
        {
        }

        private static IEnumerable<ResourceBinding> CreateMaterialResourceBindings()
        {
            yield return new ResourceBinding(
                0,
                new ResourceLayoutElementDescription("MaterialConstants", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceType(
                    "MaterialConstants",
                    64,
                    new ResourceTypeMember("BoolValue", ResourceType.Int, 0),
                    new ResourceTypeMember("FloatValue", ResourceType.Float, 4),
                    new ResourceTypeMember("IntValue", ResourceType.Int, 8),
                    new ResourceTypeMember("_Padding", ResourceType.Float, 12),
                    new ResourceTypeMember("Vector2Value", ResourceType.Vec2, 16),
                    new ResourceTypeMember("_Padding2", ResourceType.Vec2, 24),
                    new ResourceTypeMember("Vector3Value", ResourceType.Vec3, 32),
                    new ResourceTypeMember("_Padding3", ResourceType.Float, 44),
                    new ResourceTypeMember("Vector4Value", ResourceType.Vec4, 48)));

            yield return new ResourceBinding(
                1,
                new ResourceLayoutElementDescription("Texture_0", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                ResourceType.Texture2D);

            yield return new ResourceBinding(
                2,
                new ResourceLayoutElementDescription("Sampler", ResourceKind.Sampler, ShaderStages.Fragment),
                ResourceType.Sampler);
        }
    }
}
