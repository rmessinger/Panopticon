using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.W3d;
using OpenSage.FileFormats.W3d;
using OpenSage.Graphics.Mathematics;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public sealed class W3dMaterialConverterTests
{
    private static W3dMeshHeader3 CreateHeader(uint numTris, uint numVertices, W3dMeshFlags attributes = 0)
    {
        return new W3dMeshHeader3(
            Version: 0,
            Attributes: attributes,
            MeshName: "TestMesh",
            ContainerName: "TestContainer",
            NumTris: numTris,
            NumVertices: numVertices,
            NumMaterials: 1,
            NumDamageStages: 0,
            SortLevel: 0,
            PrelitVersion: 0,
            FutureCounts: 0,
            VertexChannels: 0,
            FaceChannels: 0,
            Min: Vector3.Zero,
            Max: Vector3.One,
            SphCenter: Vector3.Zero,
            SphRadius: 1.0f);
    }

    private static W3dVertexMaterialInfo CreateVertexMaterialInfo()
    {
        return new W3dVertexMaterialInfo(
            Attributes: W3dVertexMaterialFlags.None,
            Stage0Mapping: W3dVertexMappingType.Uv,
            Stage1Mapping: W3dVertexMappingType.Uv,
            Ambient: new ColorRgb(10, 20, 30),
            Diffuse: new ColorRgb(40, 50, 60),
            Specular: new ColorRgb(0, 0, 0),
            Emissive: new ColorRgb(70, 80, 90),
            Shininess: 4.0f,
            Opacity: 0.75f,
            Translucency: 0.0f);
    }

    private static W3dShader CreateShader(
        W3dShaderSrcBlendFunc srcBlend = W3dShaderSrcBlendFunc.One,
        W3dShaderDestBlendFunc destBlend = W3dShaderDestBlendFunc.Zero,
        W3dShaderPrimaryGradient primaryGradient = W3dShaderPrimaryGradient.Enable)
    {
        return new W3dShader(
            DepthCompare: W3dShaderDepthCompare.PassLEqual,
            DepthMask: W3dShaderDepthMask.WriteEnable,
            ColorMask: 0,
            DestBlend: destBlend,
            FogFunc: 0,
            PrimaryGradient: primaryGradient,
            SecondaryGradient: W3dShaderSecondaryGradient.Disable,
            SrcBlend: srcBlend,
            Texturing: W3dShaderTexturing.Enable,
            DetailColorFunc: W3dShaderDetailColorFunc.Disable,
            DetailAlphaFunc: W3dShaderDetailAlphaFunc.Disable,
            ShaderPreset: 0,
            AlphaTest: W3dShaderAlphaTest.Disable,
            PostDetailColorFunc: W3dShaderDetailColorFunc.Disable,
            PostDetailAlphaFunc: W3dShaderDetailAlphaFunc.Disable);
    }

    private static W3dMesh CreateMesh(
        W3dMeshHeader3 header,
        IReadOnlyList<W3dTriangle> triangles,
        IReadOnlyList<W3dVertexMaterial> vertexMaterials,
        IReadOnlyList<W3dShader> shaders,
        IReadOnlyList<W3dTexture> textures,
        IReadOnlyList<W3dMaterialPass> materialPasses,
        W3dShaderMaterials? shaderMaterials = null)
    {
        return new W3dMesh(
            Header: header,
            UserText: null,
            Vertices: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTICES, new Vector3[header.NumVertices]),
            Normals: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTEX_NORMALS, new Vector3[header.NumVertices]),
            Vertices2: null,
            Normals2: null,
            Tangents: null,
            Bitangents: null,
            Influences: null,
            Triangles: new W3dTriangles([.. triangles]),
            ShadeIndices: new W3dUInt32List(W3dChunkType.W3D_CHUNK_VERTEX_SHADE_INDICES, []),
            MaterialInfo: new W3dMaterialInfo(
                PassCount: (uint)materialPasses.Count,
                VertexMaterialCount: (uint)vertexMaterials.Count,
                ShaderCount: (uint)shaders.Count,
                TextureCount: (uint)textures.Count),
            VertexMaterials: new W3dVertexMaterials(vertexMaterials),
            Shaders: new W3dShaders(shaders),
            ShadersPs2: null,
            Textures: textures.Count > 0 ? new W3dTextures(textures) : null,
            MaterialPasses: materialPasses,
            AabTree: null,
            ShaderMaterials: shaderMaterials,
            PrelitVertex: null,
            PrelitLightMapMultiPass: null,
            PrelitLightMapMultiTexture: null,
            PrelitUnlit: null);
    }

    private static W3dMaterialPass CreatePass(
        uint[]? vertexMaterialIds,
        uint[]? shaderIds,
        IReadOnlyList<W3dTextureStage> textureStages,
        W3dVector2List? texCoords = null,
        uint[]? shaderMaterialIds = null)
    {
        return new W3dMaterialPass(
            VertexMaterialIds: vertexMaterialIds != null
                ? new W3dUInt32List(W3dChunkType.W3D_CHUNK_VERTEX_MATERIAL_IDS, vertexMaterialIds)
                : null,
            ShaderIds: shaderIds != null
                ? new W3dUInt32List(W3dChunkType.W3D_CHUNK_SHADER_IDS, shaderIds)
                : null,
            ShaderMaterialIds: shaderMaterialIds != null
                ? new W3dUInt32List(W3dChunkType.W3D_CHUNK_SHADER_MATERIAL_ID, shaderMaterialIds)
                : null,
            Dcg: null,
            Dig: null,
            Scg: null,
            TextureStages: textureStages,
            TexCoords: texCoords);
    }

    private static W3dTriangle CreateTriangle(uint v0, uint v1, uint v2)
    {
        return new W3dTriangle { VIndex0 = v0, VIndex1 = v1, VIndex2 = v2 };
    }

    [Fact]
    public void FixedFunction_SingleMaterial_FastPath()
    {
        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3),
            [CreateTriangle(0, 1, 2)],
            [new W3dVertexMaterial(new W3dVertexMaterialName("mat0"), CreateVertexMaterialInfo(), null, null)],
            [CreateShader()],
            [new W3dTexture(new W3dTextureName("test.tga"), null)],
            [CreatePass(
                vertexMaterialIds: [0],
                shaderIds: [0],
                textureStages: new[]
                {
                    new W3dTextureStage(
                        TextureIds: new W3dTextureIds([0]),
                        TexCoords: new W3dVector2List(W3dChunkType.W3D_CHUNK_STAGE_TEXCOORDS, new Vector2[] { new(0, 0), new(1, 0), new(0, 1) }),
                        PerFaceTexCoordIds: null),
                })]);

        var result = W3dMaterialConverter.Convert(mesh);

        var (material, part) = result.SingleZip();
        Assert.Equal(ImportedMaterial.FixedFunctionFamily, material.ShaderFamily);
        Assert.Equal(ImportedCullMode.Back, material.CullMode);
        Assert.True(material.DepthWriteEnabled);
        Assert.Equal(ImportedDepthComparison.LessEqual, material.DepthComparison);
        Assert.False(material.BlendEnabled);
        Assert.Equal(ImportedBlendFactor.One, material.SourceBlendFactor);
        Assert.Equal(ImportedBlendFactor.Zero, material.DestinationColorBlendFactor);

        Assert.NotNull(material.FixedFunctionShading);
        var shading = material.FixedFunctionShading!;
        Assert.Equal(ImportedDiffuseLightingType.Modulate, shading.DiffuseLightingType);
        Assert.Equal(new Vector3(10f / 255, 20f / 255, 30f / 255), shading.Ambient);
        Assert.Equal(0.75f, shading.Opacity);
        Assert.Equal(4.0f, shading.Shininess);
        Assert.Equal(1, shading.TextureStageCount);

        var textureProperty = Assert.Single(material.Properties);
        Assert.Equal("Texture0", textureProperty.Name);
        Assert.Equal("test.tga", textureProperty.TextureName);

        Assert.Equal(0u, part.StartIndex);
        Assert.Equal(3u, part.IndexCount);
        Assert.False(part.BlendEnabled);
        Assert.Equal(3, part.TextureCoordinates0!.Count);
    }

    [Fact]
    public void FixedFunction_PerFaceShaderSplit_ProducesRunParts()
    {
        var mesh = CreateMesh(
            CreateHeader(numTris: 2, numVertices: 3),
            [CreateTriangle(0, 1, 2), CreateTriangle(0, 2, 1)],
            [new W3dVertexMaterial(new W3dVertexMaterialName("mat0"), CreateVertexMaterialInfo(), null, null)],
            [CreateShader(), CreateShader(srcBlend: W3dShaderSrcBlendFunc.SrcAlpha, destBlend: W3dShaderDestBlendFunc.SrcColor)],
            [],
            [CreatePass(
                vertexMaterialIds: [0],
                shaderIds: [0, 1],
                textureStages: [])]);

        var result = W3dMaterialConverter.Convert(mesh);

        Assert.Equal(2, result.MeshParts.Count);
        Assert.Equal(2, result.Materials.Count);

        Assert.Equal(0u, result.MeshParts[0].StartIndex);
        Assert.Equal(3u, result.MeshParts[0].IndexCount);
        Assert.False(result.Materials[0].BlendEnabled);

        Assert.Equal(3u, result.MeshParts[1].StartIndex);
        Assert.Equal(3u, result.MeshParts[1].IndexCount);
        Assert.True(result.Materials[1].BlendEnabled);
        Assert.Equal(ImportedBlendFactor.SourceAlpha, result.Materials[1].SourceBlendFactor);
        Assert.Equal(ImportedBlendFactor.SourceColor, result.Materials[1].DestinationColorBlendFactor);
        Assert.Equal(ImportedBlendFactor.SourceAlpha, result.Materials[1].DestinationAlphaBlendFactor);
    }

    [Fact]
    public void TwoSidedMesh_UsesNoCulling()
    {
        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3, W3dMeshFlags.TwoSided),
            [CreateTriangle(0, 1, 2)],
            [new W3dVertexMaterial(new W3dVertexMaterialName("mat0"), CreateVertexMaterialInfo(), null, null)],
            [CreateShader()],
            [],
            [CreatePass(vertexMaterialIds: [0], shaderIds: [0], textureStages: [])]);

        var result = W3dMaterialConverter.Convert(mesh);

        Assert.Equal(ImportedCullMode.None, result.Materials[0].CullMode);
    }

    [Fact]
    public void ShaderMaterial_ProducesFamilyAndTypedProperties()
    {
        var shaderMaterial = new W3dShaderMaterial(
            new W3dShaderMaterialHeader(Number: 0, TypeName: "NormalMapped.fx"),
            new List<W3dShaderMaterialProperty>
            {
                new(W3dShaderMaterialPropertyType.Texture, "SkinTexture", "skin.tga", new W3dShaderMaterialPropertyValue()),
                new(W3dShaderMaterialPropertyType.Float, "BumpScale", "", new W3dShaderMaterialPropertyValue { Float = 1.5f }),
                new(W3dShaderMaterialPropertyType.Vector2, "Scroll", "", new W3dShaderMaterialPropertyValue { Vector2 = new Vector2(1, 2) }),
                new(W3dShaderMaterialPropertyType.Vector3, "Tint", "", new W3dShaderMaterialPropertyValue { Vector3 = new Vector3(1, 2, 3) }),
                new(W3dShaderMaterialPropertyType.Vector4, "Fade", "", new W3dShaderMaterialPropertyValue { Vector4 = new Vector4(1, 2, 3, 4) }),
                new(W3dShaderMaterialPropertyType.Int, "Samples", "", new W3dShaderMaterialPropertyValue { Int = 8 }),
                new(W3dShaderMaterialPropertyType.Bool, "Enable", "", new W3dShaderMaterialPropertyValue { Bool = new Bool32(true) }),
            });

        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3),
            [CreateTriangle(0, 1, 2)],
            [],
            [],
            [],
            [CreatePass(
                vertexMaterialIds: null,
                shaderIds: null,
                textureStages: [],
                texCoords: new W3dVector2List(W3dChunkType.W3D_CHUNK_STAGE_TEXCOORDS, new Vector2[] { new(0, 0), new(1, 0), new(0, 1) }),
                shaderMaterialIds: [0])],
            shaderMaterials: new W3dShaderMaterials([shaderMaterial]));

        var result = W3dMaterialConverter.Convert(mesh);

        var (material, part) = result.SingleZip();
        Assert.Equal("NormalMapped", material.ShaderFamily);
        Assert.Null(material.FixedFunctionShading);
        Assert.False(material.BlendEnabled);

        var properties = material.Properties.ToDictionary(p => p.Name);
        Assert.Equal(7, properties.Count);
        Assert.Equal("skin.tga", properties["SkinTexture"].TextureName);
        Assert.Equal(1.5f, properties["BumpScale"].FloatValue);
        Assert.Equal(new Vector2(1, 2), properties["Scroll"].Vector2Value);
        Assert.Equal(new Vector3(1, 2, 3), properties["Tint"].Vector3Value);
        Assert.Equal(new Vector4(1, 2, 3, 4), properties["Fade"].Vector4Value);
        Assert.Equal(8, properties["Samples"].Int32Value);
        Assert.True(properties["Enable"].BoolValue);

        Assert.Equal(3u, part.IndexCount);
        Assert.Equal(3, part.TextureCoordinates0!.Count);
    }

    [Fact]
    public void MultiFrameTexture_ThrowsWithContext()
    {
        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3),
            [CreateTriangle(0, 1, 2)],
            [new W3dVertexMaterial(new W3dVertexMaterialName("mat0"), CreateVertexMaterialInfo(), null, null)],
            [CreateShader()],
            [new W3dTexture(new W3dTextureName("anim.tga"), new W3dTextureInfo(default, default, FrameCount: 4, FrameRate: 1.0f))],
            [CreatePass(
                vertexMaterialIds: [0],
                shaderIds: [0],
                textureStages: new[]
                {
                    new W3dTextureStage(new W3dTextureIds([0]), null, null),
                })]);

        var exception = Assert.Throws<NotImplementedException>(() => W3dMaterialConverter.Convert(mesh));
        Assert.Contains("TestMesh", exception.Message);
    }

    [Fact]
    public void MixedMaterialsOnOneTriangle_ThrowsWithContext()
    {
        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3),
            [CreateTriangle(0, 1, 2)],
            new[]
            {
                new W3dVertexMaterial(new W3dVertexMaterialName("mat0"), CreateVertexMaterialInfo(), null, null),
                new W3dVertexMaterial(new W3dVertexMaterialName("mat1"), CreateVertexMaterialInfo(), null, null),
            },
            [CreateShader()],
            [],
            [CreatePass(vertexMaterialIds: [0, 0, 1], shaderIds: [0], textureStages: [])]);

        var exception = Assert.Throws<NotSupportedException>(() => W3dMaterialConverter.Convert(mesh));
        Assert.Contains("TestMesh", exception.Message);
    }
}

file static class W3dMaterialConverterTestExtensions
{
    public static (ImportedMaterial, ImportedMeshPart) SingleZip(this W3dMaterialConversion result)
    {
        var material = Assert.Single(result.Materials);
        var part = Assert.Single(result.MeshParts);
        return (material, part);
    }
}
