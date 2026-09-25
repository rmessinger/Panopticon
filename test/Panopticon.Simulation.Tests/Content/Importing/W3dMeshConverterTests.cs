using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.FileFormats.W3d;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class W3dMeshConverterTests
{
    private static W3dMeshHeader3 CreateHeader(uint numTris, uint numVertices, W3dMeshFlags attributes = 0, string meshName = "TestMesh")
    {
        return new W3dMeshHeader3(
            Version: 0,
            Attributes: attributes,
            MeshName: meshName,
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
            Min: new Vector3(-1, -2, -3),
            Max: new Vector3(4, 5, 6),
            SphCenter: new Vector3(1, 2, 3),
            SphRadius: 2.5f);
    }

    private static W3dShader CreateShader()
    {
        return new W3dShader(
            DepthCompare: W3dShaderDepthCompare.PassLEqual,
            DepthMask: W3dShaderDepthMask.WriteEnable,
            ColorMask: 0,
            DestBlend: W3dShaderDestBlendFunc.Zero,
            FogFunc: 0,
            PrimaryGradient: W3dShaderPrimaryGradient.Enable,
            SecondaryGradient: W3dShaderSecondaryGradient.Disable,
            SrcBlend: W3dShaderSrcBlendFunc.One,
            Texturing: W3dShaderTexturing.Enable,
            DetailColorFunc: W3dShaderDetailColorFunc.Disable,
            DetailAlphaFunc: W3dShaderDetailAlphaFunc.Disable,
            ShaderPreset: 0,
            AlphaTest: W3dShaderAlphaTest.Disable,
            PostDetailColorFunc: W3dShaderDetailColorFunc.Disable,
            PostDetailAlphaFunc: W3dShaderDetailAlphaFunc.Disable);
    }

    private static W3dVertexMaterial CreateVertexMaterial()
    {
        return new W3dVertexMaterial(
            new W3dVertexMaterialName("mat0"),
            new W3dVertexMaterialInfo(
                Attributes: W3dVertexMaterialFlags.None,
                Stage0Mapping: W3dVertexMappingType.Uv,
                Stage1Mapping: W3dVertexMappingType.Uv,
                Ambient: new ColorRgb(10, 20, 30),
                Diffuse: new ColorRgb(40, 50, 60),
                Specular: new ColorRgb(0, 0, 0),
                Emissive: new ColorRgb(70, 80, 90),
                Shininess: 4.0f,
                Opacity: 1.0f,
                Translucency: 0.0f),
            null,
            null);
    }

    private static W3dMesh CreateMesh(
        W3dMeshHeader3 header,
        IReadOnlyList<W3dTriangle> triangles,
        Vector3[] vertices,
        Vector3[] normals,
        W3dVertexInfluences? influences = null,
        W3dVector3List? vertices2 = null,
        W3dVector3List? normals2 = null,
        W3dVector3List? tangents = null,
        W3dVector3List? bitangents = null)
    {
        var materialPass = new W3dMaterialPass(
            VertexMaterialIds: new W3dUInt32List(W3dChunkType.W3D_CHUNK_VERTEX_MATERIAL_IDS, [0]),
            ShaderIds: new W3dUInt32List(W3dChunkType.W3D_CHUNK_SHADER_IDS, [0]),
            ShaderMaterialIds: null,
            Dcg: null,
            Dig: null,
            Scg: null,
            TextureStages: [],
            TexCoords: null);

        return new W3dMesh(
            Header: header,
            UserText: null,
            Vertices: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTICES, vertices),
            Normals: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTEX_NORMALS, normals),
            Vertices2: vertices2,
            Normals2: normals2,
            Tangents: tangents,
            Bitangents: bitangents,
            Influences: influences,
            Triangles: new W3dTriangles([.. triangles]),
            ShadeIndices: new W3dUInt32List(W3dChunkType.W3D_CHUNK_VERTEX_SHADE_INDICES, []),
            MaterialInfo: new W3dMaterialInfo(1, 1, 1, 0),
            VertexMaterials: new W3dVertexMaterials([CreateVertexMaterial()]),
            Shaders: new W3dShaders([CreateShader()]),
            ShadersPs2: null,
            Textures: null,
            MaterialPasses: [materialPass],
            AabTree: null,
            ShaderMaterials: null,
            PrelitVertex: null,
            PrelitLightMapMultiPass: null,
            PrelitLightMapMultiTexture: null,
            PrelitUnlit: null);
    }

    private static W3dTriangle CreateTriangle(uint v0, uint v1, uint v2)
    {
        return new W3dTriangle { VIndex0 = v0, VIndex1 = v1, VIndex2 = v2 };
    }

    [Fact]
    public void StaticMesh_Converts()
    {
        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3),
            [CreateTriangle(0, 1, 2)],
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);

        var imported = W3dMeshConverter.Convert(mesh);

        Assert.False(imported.IsSkinned);
        Assert.False(imported.Hidden);
        Assert.False(imported.CameraOriented);
        Assert.False(imported.HasHouseColor);

        Assert.Equal(3, imported.Vertices.Count);
        Assert.Equal(new Vector3(1, 0, 0), imported.Vertices[1].Position);
        Assert.Equal(Vector3.UnitZ, imported.Vertices[1].Normal);
        Assert.Null(imported.Vertices[0].SecondaryPosition);
        Assert.Equal(0u, imported.Vertices[0].BoneIndex0);
        Assert.Equal(0f, imported.Vertices[0].BoneWeight0);

        Assert.Equal(new ushort[] { 0, 1, 2 }, imported.Indices);

        Assert.Equal(new Vector3(-1, -2, -3), imported.BoundingBox.Min);
        Assert.Equal(new Vector3(4, 5, 6), imported.BoundingBox.Max);
        Assert.Equal(new Vector3(1, 2, 3), imported.BoundingSphere.Center);
        Assert.Equal(2.5f, imported.BoundingSphere.Radius);

        var material = Assert.Single(imported.Materials);
        Assert.Equal(ImportedMaterial.FixedFunctionFamily, material.ShaderFamily);
        Assert.Null(imported.Vertices[0].Tangent);
        Assert.NotNull(imported.MeshParts);
        Assert.Single(imported.MeshParts);
    }

    [Fact]
    public void SkinnedMesh_ConvertsInfluences()
    {
        var influences = new W3dVertexInfluences(new[]
        {
            new W3dVertexInfluence { BoneIndex0 = 1, BoneIndex1 = 2, BoneWeight0 = 70, BoneWeight1 = 30 },
            new W3dVertexInfluence { BoneIndex0 = 2, BoneIndex1 = 2, BoneWeight0 = 100, BoneWeight1 = 0 },
            new W3dVertexInfluence { BoneIndex0 = 0, BoneIndex1 = 1, BoneWeight0 = 50, BoneWeight1 = 50 },
        });

        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3, W3dMeshFlags.GeometryTypeSkin),
            [CreateTriangle(0, 1, 2)],
            [Vector3.Zero, Vector3.Zero, Vector3.Zero],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            influences: influences);

        var imported = W3dMeshConverter.Convert(mesh);

        Assert.True(imported.IsSkinned);
        Assert.Equal(1u, imported.Vertices[0].BoneIndex0);
        Assert.Equal(2u, imported.Vertices[0].BoneIndex1);
        Assert.Equal(0.7f, imported.Vertices[0].BoneWeight0);
        Assert.Equal(0.3f, imported.Vertices[0].BoneWeight1);
        Assert.Equal(1.0f, imported.Vertices[1].BoneWeight0);
    }

    [Fact]
    public void SecondaryGeometry_Converts()
    {
        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 1),
            [CreateTriangle(0, 0, 0)],
            [Vector3.Zero],
            [Vector3.UnitZ],
            vertices2: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTICES_2, [new Vector3(1, 2, 3)]),
            normals2: new W3dVector3List(W3dChunkType.W3D_CHUNK_NORMALS_2, [new Vector3(4, 5, 6)]),
            tangents: new W3dVector3List(W3dChunkType.W3D_CHUNK_TANGENTS, [new Vector3(7, 8, 9)]),
            bitangents: new W3dVector3List(W3dChunkType.W3D_CHUNK_BITANGENTS, [new Vector3(10, 11, 12)]));

        var imported = W3dMeshConverter.Convert(mesh);

        Assert.Equal(new Vector3(1, 2, 3), imported.Vertices[0].SecondaryPosition);
        Assert.Equal(new Vector3(4, 5, 6), imported.Vertices[0].SecondaryNormal);
        Assert.Equal(new Vector3(7, 8, 9), imported.Vertices[0].Tangent);
        Assert.Equal(new Vector3(10, 11, 12), imported.Vertices[0].Binormal);
    }

    [Fact]
    public void Flags_HiddenCameraOrientedHouseColor_Convert()
    {
        var mesh = CreateMesh(
            CreateHeader(
                numTris: 1,
                numVertices: 3,
                W3dMeshFlags.Hidden | W3dMeshFlags.GeometryTypeCameraOriented,
                meshName: "HOUSECOLOR_Turret"),
            [CreateTriangle(0, 1, 2)],
            [Vector3.Zero, Vector3.Zero, Vector3.Zero],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);

        var imported = W3dMeshConverter.Convert(mesh);

        Assert.True(imported.Hidden);
        Assert.True(imported.CameraOriented);
        Assert.True(imported.HasHouseColor);
    }

    [Fact]
    public void TriangleIndexAbove16Bit_ThrowsWithContext()
    {
        var mesh = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3),
            [CreateTriangle(0, 70000, 2)],
            [Vector3.Zero, Vector3.Zero, Vector3.Zero],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);

        var exception = Assert.Throws<InvalidDataException>(() => W3dMeshConverter.Convert(mesh));
        Assert.Contains("TestMesh", exception.Message);
    }

    [Fact]
    public void ArrayLengthMismatches_ThrowWithContext()
    {
        var normalsMismatch = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3),
            [CreateTriangle(0, 1, 2)],
            [Vector3.Zero, Vector3.Zero, Vector3.Zero],
            [Vector3.UnitZ, Vector3.UnitZ]);

        var normalsException = Assert.Throws<InvalidDataException>(() => W3dMeshConverter.Convert(normalsMismatch));
        Assert.Contains("TestMesh", normalsException.Message);

        var missingInfluences = CreateMesh(
            CreateHeader(numTris: 1, numVertices: 3, W3dMeshFlags.GeometryTypeSkin),
            [CreateTriangle(0, 1, 2)],
            [Vector3.Zero, Vector3.Zero, Vector3.Zero],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);

        var influencesException = Assert.Throws<InvalidDataException>(() => W3dMeshConverter.Convert(missingInfluences));
        Assert.Contains("TestMesh", influencesException.Message);
    }
}
