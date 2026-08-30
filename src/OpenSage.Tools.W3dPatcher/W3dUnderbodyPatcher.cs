using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.FileFormats.W3d;
using OpenSage.Mathematics;

namespace OpenSage.Tools.W3dPatcher;

public static class W3dUnderbodyPatcher
{
    public const string MeshName = "UNDERBODY";
    public const string MaterialName = "W3D Underbody";
    public const byte DefaultGrey = 128;

    private const float InsetFraction = 0.05f;
    private const float ZOffset = 0.1f;

    public static W3dFile Patch(W3dFile file, byte greyLevel = DefaultGrey)
    {
        var body = file.RenderableObjects.OfType<W3dMesh>()
            .OrderByDescending(m => m.Header.NumVertices)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("File contains no meshes.");

        var hLod = file.HLod
            ?? throw new InvalidOperationException("File contains no HLod; an appended mesh would not render.");

        var bodySubObjectName = $"{body.Header.ContainerName}.{body.Header.MeshName}";
        var boneIndex = hLod.Lods[0].SubObjects
            .FirstOrDefault(s => string.Equals(s.Name, bodySubObjectName, StringComparison.OrdinalIgnoreCase))
            ?.BoneIndex
            ?? throw new InvalidOperationException($"HLod does not reference body mesh '{bodySubObjectName}'.");

        var underbody = CreateUnderbodyMesh(body, greyLevel);
        var patchedHlod = CreatePatchedHlod(hLod, body.Header.ContainerName, boneIndex);

        var chunks = new List<W3dChunk>(file.Chunks.Count + 1);
        var lastMeshIndex = FindLastMeshIndex(file.Chunks);
        for (var i = 0; i < file.Chunks.Count; i++)
        {
            if (ReferenceEquals(file.Chunks[i], file.HLod))
            {
                chunks.Add(patchedHlod);
            }
            else
            {
                chunks.Add(file.Chunks[i]);
            }

            if (i == lastMeshIndex)
            {
                chunks.Add(underbody);
            }
        }

        var renderableObjects = new List<W3dChunk>(file.RenderableObjects)
        {
            underbody
        };

        var renderableObjectsByName = new Dictionary<string, W3dChunk>(file.RenderableObjectsByName)
        {
            [$"{body.Header.ContainerName}.{MeshName}"] = underbody
        };

        return new W3dFile(file.FilePath, chunks, renderableObjects, renderableObjectsByName, file.Hierarchy, patchedHlod);
    }

    private static int FindLastMeshIndex(IReadOnlyList<W3dChunk> chunks)
    {
        var lastMeshIndex = -1;
        for (var i = 0; i < chunks.Count; i++)
        {
            if (chunks[i] is W3dMesh)
            {
                lastMeshIndex = i;
            }
        }

        return lastMeshIndex;
    }

    private static W3dHLod CreatePatchedHlod(W3dHLod hLod, string containerName, uint boneIndex)
    {
        var lodZero = hLod.Lods[0];
        var patchedLod = lodZero with
        {
            Header = lodZero.Header with { ModelCount = lodZero.Header.ModelCount + 1 },
            SubObjects =
            [
                .. lodZero.SubObjects,
                new W3dHLodSubObject(boneIndex, $"{containerName}.{MeshName}")
            ]
        };

        var lods = new List<W3dHLodArray>(hLod.Lods)
        {
            [0] = patchedLod
        };

        return hLod with { Lods = lods };
    }

    private static W3dMesh CreateUnderbodyMesh(W3dMesh body, byte greyLevel)
    {
        var min = body.Header.Min;
        var max = body.Header.Max;

        var insetX = (max.X - min.X) * InsetFraction;
        var insetY = (max.Y - min.Y) * InsetFraction;

        var x0 = min.X + insetX;
        var x1 = max.X - insetX;
        var y0 = min.Y + insetY;
        var y1 = max.Y - insetY;
        var z = min.Z - ZOffset;

        var vertices = new[]
        {
            new Vector3(x0, y0, z),
            new Vector3(x1, y0, z),
            new Vector3(x1, y1, z),
            new Vector3(x0, y1, z)
        };

        var normal = new Vector3(0, 0, -1);
        var triangles = new[]
        {
            CreateTriangle(0, 1, 2, normal, vertices[0]),
            CreateTriangle(0, 2, 3, normal, vertices[0])
        };

        var boundsMin = new Vector3(x0, y0, z);
        var boundsMax = new Vector3(x1, y1, z);
        var sphereCenter = (boundsMin + boundsMax) / 2;
        var sphereRadius = Vector3.Distance(boundsMin, boundsMax) / 2;

        var header = new W3dMeshHeader3(
            body.Header.Version,
            W3dMeshFlags.TwoSided | W3dMeshFlags.CastShadow,
            MeshName,
            body.Header.ContainerName,
            NumTris: 2,
            NumVertices: 4,
            NumMaterials: 1,
            NumDamageStages: 0,
            SortLevel: 0,
            PrelitVersion: 0,
            FutureCounts: 0,
            VertexChannels: W3dVertexChannels.Location | W3dVertexChannels.Normal,
            FaceChannels: W3dFaceChannels.Face,
            Min: boundsMin,
            Max: boundsMax,
            SphCenter: sphereCenter,
            SphRadius: sphereRadius);

        var grey = new ColorRgb(greyLevel, greyLevel, greyLevel);
        var black = new ColorRgb(0, 0, 0);

        var vertexMaterial = new W3dVertexMaterial(
            new W3dVertexMaterialName(MaterialName),
            new W3dVertexMaterialInfo(
                W3dVertexMaterialFlags.None,
                Stage0Mapping: W3dVertexMappingType.Uv,
                Stage1Mapping: W3dVertexMappingType.Uv,
                Ambient: grey,
                Diffuse: grey,
                Specular: black,
                Emissive: black,
                Shininess: 0,
                Opacity: 1,
                Translucency: 0),
            MapperArgs0: null,
            MapperArgs1: null);

        var shader = new W3dShader(
            DepthCompare: W3dShaderDepthCompare.PassLEqual,
            DepthMask: W3dShaderDepthMask.WriteEnable,
            ColorMask: 0,
            DestBlend: W3dShaderDestBlendFunc.Zero,
            FogFunc: 2,
            PrimaryGradient: W3dShaderPrimaryGradient.Modulate,
            SecondaryGradient: W3dShaderSecondaryGradient.Disable,
            SrcBlend: W3dShaderSrcBlendFunc.One,
            Texturing: W3dShaderTexturing.Disable,
            DetailColorFunc: W3dShaderDetailColorFunc.Disable,
            DetailAlphaFunc: W3dShaderDetailAlphaFunc.Disable,
            ShaderPreset: 2,
            AlphaTest: W3dShaderAlphaTest.Disable,
            PostDetailColorFunc: W3dShaderDetailColorFunc.Disable,
            PostDetailAlphaFunc: W3dShaderDetailAlphaFunc.Disable);

        var materialPass = new W3dMaterialPass(
            VertexMaterialIds: new W3dUInt32List(W3dChunkType.W3D_CHUNK_VERTEX_MATERIAL_IDS, [0u]),
            ShaderIds: new W3dUInt32List(W3dChunkType.W3D_CHUNK_SHADER_IDS, [0u]),
            ShaderMaterialIds: null,
            Dcg: null,
            Dig: null,
            Scg: null,
            TextureStages: [],
            TexCoords: null);

        return new W3dMesh(
            header,
            UserText: null,
            Vertices: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTICES, vertices),
            Normals: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTEX_NORMALS, [normal, normal, normal, normal]),
            Vertices2: null,
            Normals2: null,
            Tangents: null,
            Bitangents: null,
            Influences: null,
            Triangles: new W3dTriangles(triangles),
            ShadeIndices: new W3dUInt32List(W3dChunkType.W3D_CHUNK_VERTEX_SHADE_INDICES, [0u, 0, 0, 0]),
            MaterialInfo: new W3dMaterialInfo(PassCount: 1, VertexMaterialCount: 1, ShaderCount: 1, TextureCount: 0),
            VertexMaterials: new W3dVertexMaterials([vertexMaterial]),
            Shaders: new W3dShaders([shader]),
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

    private static W3dTriangle CreateTriangle(uint v0, uint v1, uint v2, Vector3 normal, Vector3 vertex0)
    {
        return new W3dTriangle
        {
            VIndex0 = v0,
            VIndex1 = v1,
            VIndex2 = v2,
            SurfaceType = W3dSurfaceType.LightMetal,
            Normal = normal,
            Distance = Vector3.Dot(normal, vertex0)
        };
    }
}
