using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.FileFormats.W3d;
using Panopticon.Mathematics;

namespace Panopticon.Tools.W3dPatcher;

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

        if (file.HLod == null)
        {
            var singleMeshUnderbody = CreateUnderbodyMesh(body, Matrix4x4.Identity, greyLevel);
            return PatchSingleMeshFile(file, body, singleMeshUnderbody);
        }

        var hierarchy = file.Hierarchy
            ?? throw new InvalidOperationException("File has an HLod but no hierarchy.");
        var bodySubObjectName = $"{body.Header.ContainerName}.{body.Header.MeshName}";
        var boneIndex = file.HLod.Lods[0].SubObjects
            .FirstOrDefault(s => string.Equals(s.Name, bodySubObjectName, StringComparison.OrdinalIgnoreCase))
            ?.BoneIndex
            ?? throw new InvalidOperationException($"HLod does not reference body mesh '{bodySubObjectName}'.");

        var bodyToModel = GetBindPoseTransform(hierarchy, boneIndex);
        var underbody = CreateUnderbodyMesh(body, bodyToModel, greyLevel);
        var patchedHlod = CreatePatchedHlod(file.HLod, body.Header.ContainerName, boneIndex);

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

    private static W3dFile PatchSingleMeshFile(W3dFile file, W3dMesh body, W3dMesh underbody)
    {
        if (file.Hierarchy != null)
        {
            throw new InvalidOperationException("File has a hierarchy but no HLod; unsupported layout.");
        }

        if (file.RenderableObjects.Count != 1)
        {
            throw new InvalidOperationException("File has no HLod but contains multiple renderable objects.");
        }

        var bodySubObjectName = file.RenderableObjectsByName.First(kv => ReferenceEquals(kv.Value, body)).Key;
        var underbodySubObjectName = $"{body.Header.ContainerName}.{MeshName}";

        var hierarchyName = body.Header.MeshName;
        var hierarchy = new W3dHierarchyDef(
            new W3dHierarchy(65538, hierarchyName, 1, Vector3.Zero),
            new W3dPivots(
            [
                new W3dPivot("ROOTTRANSFORM", -1, Vector3.Zero, Vector3.Zero, Quaternion.Identity)
            ]),
            PivotFixups: null);

        var hLod = new W3dHLod(
            new W3dHLodHeader(65536, 1, hierarchyName, hierarchyName),
        [
            new W3dHLodArray(
                new W3dHLodArrayHeader(2, 100000.0f),
            [
                new W3dHLodSubObject(0, bodySubObjectName),
                new W3dHLodSubObject(0, underbodySubObjectName)
            ])
        ],
            Aggregate: null,
            Proxy: null);

        var chunks = new List<W3dChunk>
        {
            hierarchy,
            body,
            underbody,
            hLod
        };

        var renderableObjects = new List<W3dChunk>
        {
            body,
            underbody
        };

        var renderableObjectsByName = new Dictionary<string, W3dChunk>
        {
            [bodySubObjectName] = body,
            [underbodySubObjectName] = underbody
        };

        return new W3dFile(file.FilePath, chunks, renderableObjects, renderableObjectsByName, hierarchy, hLod);
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

    private static Matrix4x4 GetBindPoseTransform(W3dHierarchyDef hierarchy, uint boneIndex)
    {
        if (boneIndex >= (uint)hierarchy.Pivots.Items.Count)
        {
            throw new InvalidOperationException(
                $"HLod bone index {boneIndex} is outside hierarchy '{hierarchy.Header.Name}'.");
        }

        var result = Matrix4x4.Identity;
        var currentIndex = (int)boneIndex;
        for (var depth = 0; currentIndex >= 0; depth++)
        {
            if (depth >= hierarchy.Pivots.Items.Count)
            {
                throw new InvalidOperationException(
                    $"Hierarchy '{hierarchy.Header.Name}' contains a cyclic parent chain.");
            }

            var pivot = hierarchy.Pivots.Items[currentIndex];
            result *= Matrix4x4.CreateFromQuaternion(pivot.Rotation)
                * Matrix4x4.CreateTranslation(pivot.Translation);
            currentIndex = pivot.ParentIdx;
            if (currentIndex >= hierarchy.Pivots.Items.Count)
            {
                throw new InvalidOperationException(
                    $"Hierarchy '{hierarchy.Header.Name}' contains invalid parent index {currentIndex}.");
            }
        }

        return result;
    }

    private static W3dMesh CreateUnderbodyMesh(
        W3dMesh body,
        in Matrix4x4 bodyToModel,
        byte greyLevel)
    {
        if (!Matrix4x4.Invert(bodyToModel, out var modelToBody))
        {
            throw new InvalidOperationException(
                $"Body mesh '{body.Header.MeshName}' has a non-invertible bind-pose transform.");
        }

        var bodyModelMin = new Vector3(float.MaxValue);
        var bodyModelMax = new Vector3(float.MinValue);
        for (var cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            var corner = new Vector3(
                (cornerIndex & 1) == 0 ? body.Header.Min.X : body.Header.Max.X,
                (cornerIndex & 2) == 0 ? body.Header.Min.Y : body.Header.Max.Y,
                (cornerIndex & 4) == 0 ? body.Header.Min.Z : body.Header.Max.Z);
            var modelCorner = Vector3.Transform(corner, bodyToModel);
            bodyModelMin = Vector3.Min(bodyModelMin, modelCorner);
            bodyModelMax = Vector3.Max(bodyModelMax, modelCorner);
        }

        var insetX = (bodyModelMax.X - bodyModelMin.X) * InsetFraction;
        var insetY = (bodyModelMax.Y - bodyModelMin.Y) * InsetFraction;

        var x0 = bodyModelMin.X + insetX;
        var x1 = bodyModelMax.X - insetX;
        var y0 = bodyModelMin.Y + insetY;
        var y1 = bodyModelMax.Y - insetY;
        var z = bodyModelMin.Z - ZOffset;

        var vertices = new[]
        {
            Vector3.Transform(new Vector3(x0, y0, z), modelToBody),
            Vector3.Transform(new Vector3(x1, y0, z), modelToBody),
            Vector3.Transform(new Vector3(x1, y1, z), modelToBody),
            Vector3.Transform(new Vector3(x0, y1, z), modelToBody)
        };

        var normal = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, modelToBody));
        var triangles = new[]
        {
            CreateTriangle(0, 2, 1, normal, vertices[0]),
            CreateTriangle(0, 3, 2, normal, vertices[0])
        };

        var boundsMin = vertices.Aggregate(Vector3.Min);
        var boundsMax = vertices.Aggregate(Vector3.Max);
        var sphereCenter = vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex) / vertices.Length;
        var sphereRadius = vertices.Max(vertex => Vector3.Distance(sphereCenter, vertex));

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
