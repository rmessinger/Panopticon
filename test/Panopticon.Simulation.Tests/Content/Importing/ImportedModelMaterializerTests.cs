using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.IO;
using Panopticon.Mathematics;
using Panopticon.Mods.BuiltIn;
using Panopticon.Rendering;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

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
            [new ImportedSubObject("TestContainer.MeshA", "MeshA", parentBoneIndex, renderObject)],
                ImportedSkeletonOrigin.Embedded);
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

    [Fact]
    public void MaterializeSkeleton_DefaultBoneScaleIsUnit()
    {
        var skeleton = new ImportedSkeleton(
            "TestHierarchy",
            [
                new ImportedBone("root", -1, Vector3.Zero, Quaternion.Identity),
                new ImportedBone("turret", 0, new Vector3(1, 2, 3), Quaternion.Identity, Matrix4x4.Identity),
            ]);

        var hierarchy = ImportedModelMaterializer.MaterializeSkeleton(skeleton);

        Assert.All(hierarchy.Bones, bone => Assert.Equal(1.0f, bone.Transform.Scale));
    }

    [Fact]
    public void MaterializeSkeleton_ScaledRestBone_ProducesScaledTransform()
    {
        var skeleton = new ImportedSkeleton(
            "TestHierarchy",
            [new ImportedBone("root", -1, new Vector3(4, 5, 6), Quaternion.Identity, bindScale: 2.5f)]);

        var bone = Assert.Single(ImportedModelMaterializer.MaterializeSkeleton(skeleton).Bones);

        Assert.Equal(2.5f, bone.Transform.Scale);
        Assert.True(Matrix4x4.Decompose(bone.Transform.Matrix, out var scale, out _, out var translation));
        Assert.Equal(2.5f, scale.X);
        Assert.Equal(2.5f, scale.Y);
        Assert.Equal(2.5f, scale.Z);
        Assert.Equal(new Vector3(4, 5, 6), translation);
    }

    [Theory]
    [InlineData(-1.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void BindScale_RejectsNegativeAndNonFinite(float bindScale)
    {
        Assert.Throws<ArgumentException>(() =>
            new ImportedBone("root", -1, Vector3.Zero, Quaternion.Identity, bindScale));
    }

    [Fact]
    public void PbrModel_MalformedInput_FailsBeforeGpuOrFileSystemAccess()
    {
        // No graphics device and no real file system: any attempt to allocate GPU resources or
        // read external images would fail, proving the validator rejects the model first.

        var mesh = CreateStaticMesh();
        var payload = new ImportedPbrMetallicRoughness(
            baseColorTexture: new ImportedTextureBinding(
                ImportedImageSource.CreateVirtualPath("textures/base.png"),
                uvSet: 1,
                new ImportedSampler(),
                ImportedTextureRole.BaseColor));
        var pbrMesh = ReplaceMaterials(mesh, ImportedMaterial.CreatePbrMetallicRoughness(payload));

        var context = CreateDataOnlyContext();

        var exception = Assert.Throws<InvalidDataException>(() =>
            ImportedModelMaterializer.MaterializeModel(CreateModel(pbrMesh), context));
        Assert.Contains("TestModel", exception.Message);
        Assert.Contains("mesh part 0", exception.Message);
        Assert.Contains("UV set 1", exception.Message);
    }

    [Fact]
    public void PbrModel_NonFiniteFactor_FailsWithContext()
    {
        var context = CreateDataOnlyContext();
        var mesh = CreateStaticMesh();
        var payload = new ImportedPbrMetallicRoughness(metallicFactor: float.NaN);
        var pbrMesh = ReplaceMaterials(mesh, ImportedMaterial.CreatePbrMetallicRoughness(payload));

        var exception = Assert.Throws<InvalidDataException>(() =>
            ImportedModelMaterializer.MaterializeModel(CreateModel(pbrMesh), context));
        Assert.Contains("metallic factor", exception.Message);
    }

    [Fact]
    public void PbrModel_FactorOutOfRange_FailsWithContext()
    {
        var context = CreateDataOnlyContext();
        var mesh = CreateStaticMesh();
        var payload = new ImportedPbrMetallicRoughness(roughnessFactor: 1.5f);
        var pbrMesh = ReplaceMaterials(mesh, ImportedMaterial.CreatePbrMetallicRoughness(payload));

        var exception = Assert.Throws<InvalidDataException>(() =>
            ImportedModelMaterializer.MaterializeModel(CreateModel(pbrMesh), context));
        Assert.Contains("roughness factor", exception.Message);
    }

    [Fact]
    public void PbrModel_BlendFlagMustAgreeWithAlphaMode()
    {
        var context = CreateDataOnlyContext();
        var mesh = CreateStaticMesh();
        var payload = new ImportedPbrMetallicRoughness(alphaMode: ImportedAlphaMode.Blend);
        var pbrMesh = ReplaceMaterials(mesh, ImportedMaterial.CreatePbrMetallicRoughness(payload));

        // CreateStaticMesh sets blendEnabled: false, which contradicts Blend alpha mode.
        var exception = Assert.Throws<InvalidDataException>(() =>
            ImportedModelMaterializer.MaterializeModel(CreateModel(pbrMesh), context));
        Assert.Contains("alpha mode 'Blend'", exception.Message);
    }

    [Fact]
    public void PbrModel_PartialTangents_AreMalformed()
    {
        var context = CreateDataOnlyContext();
        var mesh = CreateStaticMesh();
        var payload = new ImportedPbrMetallicRoughness(
            normalTexture: new ImportedTextureBinding(
                ImportedImageSource.CreateVirtualPath("textures/normal.png"),
                uvSet: 0,
                new ImportedSampler(),
                ImportedTextureRole.Normal));
        var pbrMesh = ReplaceMaterials(mesh, ImportedMaterial.CreatePbrMetallicRoughness(payload));

        // CreateStaticMesh vertices carry no tangents: normal mapping with no UV set 0 fails as
        // unsupported only when UV data is missing; with UV present, generation inputs are valid.
        // Force the malformed path by giving exactly one vertex a tangent.
        var mixedMesh = WithTangentOnFirstVertex(pbrMesh);
        var exception = Assert.Throws<InvalidDataException>(() =>
            ImportedModelMaterializer.MaterializeModel(CreateModel(mixedMesh), context));
        Assert.Contains("tangent", exception.Message);
    }

    [Fact]
    public void PbrModel_ConflictingSharedVertexUvs_AreUnsupported()
    {
        var context = CreateDataOnlyContext();
        var normalBinding = new ImportedTextureBinding(
            ImportedImageSource.CreateVirtualPath("textures/normal.png"),
            uvSet: 0,
            new ImportedSampler(),
            ImportedTextureRole.Normal);
        var materials = new[]
        {
            ImportedMaterial.CreatePbrMetallicRoughness(new ImportedPbrMetallicRoughness(normalTexture: normalBinding)),
            ImportedMaterial.CreatePbrMetallicRoughness(new ImportedPbrMetallicRoughness(normalTexture: normalBinding)),
        };

        var vertices = new[]
        {
            new ImportedVertex(new Vector3(0, 0, 0), Vector3.UnitZ),
            new ImportedVertex(new Vector3(1, 0, 0), Vector3.UnitZ),
            new ImportedVertex(new Vector3(0, 1, 0), Vector3.UnitZ),
        };

        // Both parts draw the same three vertices, but with conflicting UV values for vertex 0.
        var mesh = new ImportedMesh(
            vertices,
            [0, 1, 2, 0, 1, 2],
            new AxisAlignedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)),
            new BoundingSphere(Vector3.Zero, 1.5f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: materials,
            meshParts:
            [
                new ImportedMeshPart(
                    textureCoordinates0: new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) },
                    textureCoordinates1: null,
                    startIndex: 0,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 0),
                new ImportedMeshPart(
                    textureCoordinates0: new[] { new Vector2(0.5f, 0.5f), new Vector2(1, 0), new Vector2(0, 1) },
                    textureCoordinates1: null,
                    startIndex: 3,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 1),
            ]);

        var exception = Assert.Throws<NotSupportedException>(() =>
            ImportedModelMaterializer.MaterializeModel(CreateModel(mesh), context));
        Assert.Contains("vertex 0", exception.Message);
        Assert.Contains("part order", exception.Message);
    }

    [GraphicsFact]
    public void PbrModel_MaterializesThroughDedicatedPbrShaderPath()
    {
        using var graphics = new TestGraphicsContext();

        // Normal-mapped PBR mesh without tangents: the materializer must validate, generate the
        // tangent basis, upload the embedded image, and produce a cached PBR material.
        var baseColorTexture = new ImportedTextureBinding(
            ImportedImageSource.CreateEmbeddedBytes(EncodePngBytes(), "image/png", "helmet.glb#0"),
            uvSet: 0,
            new ImportedSampler(),
            ImportedTextureRole.BaseColor);
        var normalTexture = new ImportedTextureBinding(
            ImportedImageSource.CreateEmbeddedBytes(EncodePngBytes(), "image/png", "helmet.glb#1"),
            uvSet: 0,
            new ImportedSampler(),
            ImportedTextureRole.Normal);

        var payload = ImportedMaterial.CreatePbrMetallicRoughness(
            new ImportedPbrMetallicRoughness(
                baseColorTexture: baseColorTexture,
                normalTexture: normalTexture,
                alphaMode: ImportedAlphaMode.Blend));

        var mesh = ReplaceMaterials(CreateStaticMesh(), payload, blendEnabled: true);
        var model = ImportedModelMaterializer.MaterializeModel(CreateModel(mesh), graphics.Context);

        var modelMesh = Assert.IsType<ModelMesh>(model.SubObjects[0].RenderObject);
        var meshPart = Assert.Single(modelMesh.MeshParts);
        Assert.True(meshPart.BlendEnabled);
        Assert.Same(
            graphics.Context.ShaderResources.PbrMetallicRoughness,
            meshPart.Material.ForwardPass!.ShaderSet);

        // Equivalent second materialization reuses the cached material.
        var modelAgain = ImportedModelMaterializer.MaterializeModel(
            CreateModel(ReplaceMaterials(CreateStaticMesh(), payload, blendEnabled: true)),
            graphics.Context);
        var meshPartAgain = Assert.Single(Assert.IsType<ModelMesh>(modelAgain.SubObjects[0].RenderObject).MeshParts);
        Assert.Same(meshPart.Material.ForwardPass, meshPartAgain.Material.ForwardPass);
    }

    [GraphicsFact]
    public void PbrModel_AllRoles_Embedded_MaterializeStaticAndSkinned()
    {
        using var graphics = new TestGraphicsContext();

        // Skinned quad uses two bone influences; both variants omit tangents so generation runs.
        var staticModel = ImportedModelMaterializer.MaterializeModel(
            PbrImportedModelTestData.CreateModel(
                PbrImportedModelTestData.CreateQuadMesh(PbrImportedModelTestData.CreateFullRoleMaterial(embedded: true))),
            graphics.Context);
        var skinnedModel = ImportedModelMaterializer.MaterializeModel(
            PbrImportedModelTestData.CreateModel(
                PbrImportedModelTestData.CreateQuadMesh(PbrImportedModelTestData.CreateFullRoleMaterial(embedded: true), skinned: true)),
            graphics.Context);

        var staticMesh = Assert.IsType<ModelMesh>(staticModel.SubObjects[0].RenderObject);
        var skinnedMesh = Assert.IsType<ModelMesh>(skinnedModel.SubObjects[0].RenderObject);

        Assert.False(staticMesh.Skinned);
        Assert.True(skinnedMesh.Skinned);
        Assert.Single(staticMesh.MeshParts);
        Assert.Single(skinnedMesh.MeshParts);

        Assert.Same(
            graphics.Context.ShaderResources.PbrMetallicRoughness,
            staticMesh.MeshParts[0].Material.ForwardPass!.ShaderSet);
        Assert.Same(
            graphics.Context.ShaderResources.PbrMetallicRoughness,
            skinnedMesh.MeshParts[0].Material.ForwardPass!.ShaderSet);

        // Same payload values and equivalent embedded content: one cached material for both.
        Assert.Same(
            staticMesh.MeshParts[0].Material.ForwardPass,
            skinnedMesh.MeshParts[0].Material.ForwardPass);
    }

    [GraphicsFact]
    public void PbrModel_AllRoles_External_MaterializesThroughExactPaths()
    {
        var fileSystem = new MemoryFileSystem();
        foreach (var name in new[] { "base", "metallicRoughness", "normal", "occlusion", "emissive" })
        {
            fileSystem.AddFile($"textures/{name}.png", PbrImportedModelTestData.EncodePng());
        }

        using var graphics = new TestGraphicsContext(fileSystem);

        var model = ImportedModelMaterializer.MaterializeModel(
            PbrImportedModelTestData.CreateModel(
                PbrImportedModelTestData.CreateQuadMesh(PbrImportedModelTestData.CreateFullRoleMaterial(embedded: false))),
            graphics.Context);

        var mesh = Assert.IsType<ModelMesh>(model.SubObjects[0].RenderObject);
        Assert.Same(
            graphics.Context.ShaderResources.PbrMetallicRoughness,
            Assert.Single(mesh.MeshParts).Material.ForwardPass!.ShaderSet);
    }

    [GraphicsFact]
    public void PbrModel_ImportedTangents_MaterializeWithoutGeneration()
    {
        using var graphics = new TestGraphicsContext();

        var material = PbrImportedModelTestData.CreateFullRoleMaterial(embedded: true);
        var mesh = PbrImportedModelTestData.CreateQuadMesh(material, withTangents: true);

        var model = ImportedModelMaterializer.MaterializeModel(
            PbrImportedModelTestData.CreateModel(mesh),
            graphics.Context);

        var modelMesh = Assert.IsType<ModelMesh>(model.SubObjects[0].RenderObject);
        Assert.Same(
            graphics.Context.ShaderResources.PbrMetallicRoughness,
            Assert.Single(modelMesh.MeshParts).Material.ForwardPass!.ShaderSet);
    }

    private static byte[] EncodePngBytes()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2, 2);
        using var stream = new System.IO.MemoryStream();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return stream.ToArray();
    }

    private static AssetLoadContext CreateDataOnlyContext()
    {
        var fileSystem = new MemoryFileSystem();
        var assetStore = new AssetStore(
            SageGame.CncGenerals,
            fileSystem,
            "english",
            null,
            null,
            null,
            null,
            OnDemandAssetLoadStrategy.CreateW3d(PathResolvers.W3d, PathResolvers.GeneralsTexture));
        assetStore.PushScope();
        return new AssetLoadContext(fileSystem, "english", null, null, null, null, assetStore);
    }

    private static ImportedMesh ReplaceMaterials(ImportedMesh mesh, ImportedMaterial material, bool blendEnabled = false)
    {
        return new ImportedMesh(
            mesh.Vertices,
            mesh.Indices,
            mesh.BoundingBox,
            mesh.BoundingSphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: [material],
            meshParts:
            [
                new ImportedMeshPart(
                    mesh.MeshParts[0].TextureCoordinates0?.ToArray(),
                    mesh.MeshParts[0].TextureCoordinates1?.ToArray(),
                    mesh.MeshParts[0].StartIndex,
                    mesh.MeshParts[0].IndexCount,
                    blendEnabled,
                    materialIndex: 0),
            ]);
    }

    private static ImportedMesh WithTangentOnFirstVertex(ImportedMesh mesh)
    {
        var oldVertices = mesh.Vertices;
        var vertices = new ImportedVertex[oldVertices.Count];
        for (var i = 0; i < oldVertices.Count; i++)
        {
            var v = oldVertices[i];
            vertices[i] = i == 0
                ? new ImportedVertex(
                    v.Position,
                    v.Normal,
                    tangent: v.Tangent ?? Vector3.UnitX,
                    binormal: v.Binormal,
                    boneIndex0: v.BoneIndex0,
                    boneIndex1: v.BoneIndex1,
                    boneWeight0: v.BoneWeight0,
                    boneWeight1: v.BoneWeight1)
                : v;
        }

        return new ImportedMesh(
            vertices,
            mesh.Indices,
            mesh.BoundingBox,
            mesh.BoundingSphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: mesh.Materials,
            meshParts: mesh.MeshParts);
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
