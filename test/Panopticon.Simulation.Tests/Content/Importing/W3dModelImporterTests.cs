using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.FileFormats.W3d;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class W3dModelImporterTests
{
    private static W3dMeshHeader3 CreateMeshHeader(string meshName = "MeshA", string containerName = "TestContainer")
    {
        return new W3dMeshHeader3(
            Version: 0,
            Attributes: 0,
            MeshName: meshName,
            ContainerName: containerName,
            NumTris: 1,
            NumVertices: 3,
            NumMaterials: 1,
            NumDamageStages: 0,
            SortLevel: 0,
            PrelitVersion: 0,
            FutureCounts: 0,
            VertexChannels: W3dVertexChannels.Location | W3dVertexChannels.Normal,
            FaceChannels: W3dFaceChannels.Face,
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

    private static W3dMesh CreateMesh(string meshName = "MeshA", string containerName = "TestContainer")
    {
        var header = CreateMeshHeader(meshName, containerName);
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
            Vertices: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTICES,
                [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)]),
            Normals: new W3dVector3List(W3dChunkType.W3D_CHUNK_VERTEX_NORMALS,
                [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]),
            Vertices2: null,
            Normals2: null,
            Tangents: null,
            Bitangents: null,
            Influences: null,
            Triangles: new W3dTriangles([new W3dTriangle { VIndex0 = 0, VIndex1 = 1, VIndex2 = 2 }]),
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

    private static W3dPivot CreatePivot(
        string name,
        int parentIdx,
        Vector3? translation = null,
        Quaternion? rotation = null)
    {
        return new W3dPivot(
            name,
            parentIdx,
            translation ?? Vector3.Zero,
            EulerAngles: Vector3.Zero,
            Rotation: rotation ?? Quaternion.Identity);
    }

    private static W3dHierarchyDef CreateHierarchyDef(
        string name = "TestHierarchy",
        IReadOnlyList<W3dPivot>? pivots = null)
    {
        return new W3dHierarchyDef(
            new W3dHierarchy(Version: 0, Name: name, NumPivots: (uint)(pivots?.Count ?? 0), Center: Vector3.Zero),
            new W3dPivots(pivots ?? [CreatePivot("root", -1)]),
            PivotFixups: null);
    }

    private static W3dHLod CreateHLod(
        string hierarchyName = "TestHierarchy",
        IReadOnlyList<W3dHLodSubObject>? subObjects = null,
        IReadOnlyList<W3dHLodArray>? lods = null)
    {
        var lodArray = new W3dHLodArray(
            new W3dHLodArrayHeader(ModelCount: (uint)(subObjects?.Count ?? 0), MaxScreenSize: 0f),
            [.. (subObjects ?? [])]);

        return new W3dHLod(
            new W3dHLodHeader(Version: 0, LodCount: 1, Name: "TestModel", HierarchyName: hierarchyName),
            [.. (lods ?? [lodArray])],
            Aggregate: null,
            Proxy: null);
    }

    private static W3dFile CreateFile(
        IReadOnlyList<W3dChunk>? chunks = null,
        Dictionary<string, W3dChunk>? renderablesByName = null,
        W3dHierarchyDef? hierarchy = null,
        W3dHLod? hlod = null,
        string filePath = "art/w3d/test.w3d")
    {
        var chunkList = new List<W3dChunk>(chunks ?? []);
        if (hierarchy != null)
        {
            chunkList.Add(hierarchy);
        }

        if (hlod != null)
        {
            chunkList.Add(hlod);
        }

        if (renderablesByName != null)
        {
            chunkList.AddRange(renderablesByName.Values);
        }

        var renderables = new List<W3dChunk>();
        if (renderablesByName != null)
        {
            renderables.AddRange(renderablesByName.Values);
        }

        return new W3dFile(
            filePath,
            chunkList,
            renderables,
            renderablesByName ?? [],
            hierarchy,
            hlod);
    }

    private static AssetImportContext CreateContext(MemoryFileSystem? fileSystem = null)
    {
        return new AssetImportContext(fileSystem ?? new MemoryFileSystem(), "english");
    }

    private static W3dModelImporter CreateImporter()
    {
        return new W3dModelImporter(PathResolvers.W3d);
    }

    private static void AddFileToSystem(MemoryFileSystem fileSystem, W3dFile w3dFile)
    {
        using var stream = new MemoryStream();
        w3dFile.WriteTo(stream);
        fileSystem.AddFile(w3dFile.FilePath, stream.ToArray());
    }

    [Fact]
    public void ImportModel_ResolvesFileThroughPathResolver()
    {
        var fileSystem = new MemoryFileSystem();
        AddFileToSystem(fileSystem, CreateFile(
            renderablesByName: new Dictionary<string, W3dChunk>
            {
                ["TestContainer.MeshA"] = CreateMesh(),
            },
            filePath: "art/w3d/mymodel.w3d"));

        var imported = CreateImporter().ImportModel("mymodel", CreateContext(fileSystem));

        Assert.NotNull(imported);
        Assert.Equal("mymodel", imported!.Name);
        var subObject = Assert.Single(imported.SubObjects);
        Assert.Equal("TestContainer.MeshA", subObject.FullName);
        Assert.Equal(0, subObject.ParentBoneIndex);
    }

    [Fact]
    public void MissingFiles_ReturnNull()
    {
        var importer = CreateImporter();
        var context = CreateContext();

        Assert.Null(importer.ImportModel("missing", context));
        Assert.Null(importer.ImportSkeleton("missing", context));
    }

    [Fact]
    public void EmbeddedSkeleton_ConvertsPivotsInOrder()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        var hierarchy = CreateHierarchyDef(pivots:
        [
            CreatePivot("root", -1, new Vector3(1, 2, 3)),
            CreatePivot("child", 0, new Vector3(4, 5, 6), rotation),
        ]);

        var imported = CreateImporter().Convert(
            CreateFile(
                renderablesByName: new Dictionary<string, W3dChunk>
                {
                    ["TestContainer.MeshA"] = CreateMesh(),
                },
                hierarchy: hierarchy),
            CreateContext());

        Assert.Equal("TestHierarchy", imported.Skeleton.Name);
        Assert.Equal(2, imported.Skeleton.Bones.Count);
        Assert.Equal("root", imported.Skeleton.Bones[0].Name);
        Assert.Equal(-1, imported.Skeleton.Bones[0].ParentIndex);
        Assert.Equal(new Vector3(1, 2, 3), imported.Skeleton.Bones[0].BindTranslation);
        Assert.Equal("child", imported.Skeleton.Bones[1].Name);
        Assert.Equal(0, imported.Skeleton.Bones[1].ParentIndex);
        Assert.Equal(rotation, imported.Skeleton.Bones[1].BindRotation);
    }

    [Fact]
    public void EmbeddedSkeleton_ForwardParentReference_Throws()
    {
        var hierarchy = CreateHierarchyDef(pivots:
        [
            CreatePivot("root", -1),
            CreatePivot("child", 2),
        ]);

        var exception = Assert.Throws<InvalidDataException>(
            () => W3dModelImporter.ConvertSkeleton(hierarchy));
        Assert.Contains("TestHierarchy", exception.Message);
        Assert.Contains("child", exception.Message);
    }

    [Fact]
    public void ConvertSkeleton_EmptyPivots_Throws()
    {
        Assert.Throws<InvalidDataException>(
            () => W3dModelImporter.ConvertSkeleton(CreateHierarchyDef(pivots: [])));
    }

    [Fact]
    public void ReferencedSkeleton_LoadsFromSeparateFile()
    {
        var fileSystem = new MemoryFileSystem();
        AddFileToSystem(fileSystem, CreateFile(
            renderablesByName: new Dictionary<string, W3dChunk>
            {
                ["TestContainer.MeshA"] = CreateMesh(),
            },
            hlod: CreateHLod(
                hierarchyName: "TestHierarchy",
                subObjects: [new W3dHLodSubObject(BoneIndex: 0, Name: "TestContainer.MeshA")]),
            filePath: "art/w3d/mymodel.w3d"));
        AddFileToSystem(fileSystem, CreateFile(
            hierarchy: CreateHierarchyDef(name: "TestHierarchy", pivots:
            [
                CreatePivot("root", -1),
                CreatePivot("child", 0),
            ]),
            filePath: "art/w3d/TestHierarchy.w3d"));

        var imported = CreateImporter().ImportModel("mymodel", CreateContext(fileSystem));

        Assert.NotNull(imported);
        Assert.Equal("TestHierarchy", imported!.Skeleton.Name);
        Assert.Equal(2, imported.Skeleton.Bones.Count);
        var subObject = Assert.Single(imported.SubObjects);
        Assert.Equal(0, subObject.ParentBoneIndex);
    }

    [Fact]
    public void ReferencedSkeleton_MissingFile_ThrowsWithContext()
    {
        var fileSystem = new MemoryFileSystem();
        AddFileToSystem(fileSystem, CreateFile(
            renderablesByName: new Dictionary<string, W3dChunk>
            {
                ["TestContainer.MeshA"] = CreateMesh(),
            },
            hlod: CreateHLod(hierarchyName: "TestHierarchy"),
            filePath: "art/w3d/mymodel.w3d"));

        var exception = Assert.Throws<InvalidDataException>(
            () => CreateImporter().ImportModel("mymodel", CreateContext(fileSystem)));

        Assert.Contains("mymodel", exception.Message);
        Assert.Contains("TestHierarchy", exception.Message);
    }

    [Fact]
    public void NoHierarchyOrHlod_UsesDefaultSkeleton()
    {
        var imported = CreateImporter().Convert(
            CreateFile(renderablesByName: new Dictionary<string, W3dChunk>
            {
                ["TestContainer.MeshA"] = CreateMesh(),
            }),
            CreateContext());

        Assert.Equal("[Default]", imported.Skeleton.Name);
        var bone = Assert.Single(imported.Skeleton.Bones);
        Assert.Equal(-1, bone.ParentIndex);
        Assert.Equal(Vector3.Zero, bone.BindTranslation);
        Assert.Equal(Quaternion.Identity, bone.BindRotation);
    }

    [Fact]
    public void HLod_ComposesFirstLodSubObjects()
    {
        var hierarchy = CreateHierarchyDef(pivots:
        [
            CreatePivot("root", -1),
            CreatePivot("turret", 0),
        ]);
        var hlod = CreateHLod(subObjects:
        [
            new W3dHLodSubObject(BoneIndex: 1, Name: "TestContainer.MeshA"),
            new W3dHLodSubObject(BoneIndex: 0, Name: "TestContainer.MeshB"),
        ]);

        var imported = CreateImporter().Convert(
            CreateFile(
                renderablesByName: new Dictionary<string, W3dChunk>
                {
                    ["TestContainer.MeshA"] = CreateMesh("MeshA"),
                    ["TestContainer.MeshB"] = CreateMesh("MeshB"),
                },
                hierarchy: hierarchy,
                hlod: hlod),
            CreateContext());

        Assert.Equal(2, imported.SubObjects.Count);

        Assert.Equal("TestContainer.MeshA", imported.SubObjects[0].FullName);
        Assert.Equal("MeshA", imported.SubObjects[0].DisplayName);
        Assert.Equal(1, imported.SubObjects[0].ParentBoneIndex);

        Assert.Equal("TestContainer.MeshB", imported.SubObjects[1].FullName);
        Assert.Equal(0, imported.SubObjects[1].ParentBoneIndex);

        var mesh = Assert.IsType<ImportedMesh>(imported.SubObjects[0].RenderObject);
        Assert.False(mesh.IsSkinned);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Equal(new ushort[] { 0, 1, 2 }, mesh.Indices);
        Assert.Equal(new Vector3(-1, -2, -3), mesh.BoundingBox.Min);
        Assert.Equal(new Vector3(4, 5, 6), mesh.BoundingBox.Max);
    }

    [Fact]
    public void HLod_UsesOnlyFirstLod()
    {
        var hierarchy = CreateHierarchyDef();
        var secondLod = new W3dHLodArray(
            new W3dHLodArrayHeader(ModelCount: 1, MaxScreenSize: 0f),
            [new W3dHLodSubObject(BoneIndex: 0, Name: "TestContainer.NotInFile")]);
        var hlod = CreateHLod(subObjects: [new W3dHLodSubObject(BoneIndex: 0, Name: "TestContainer.MeshA")]);

        var imported = CreateImporter().Convert(
            CreateFile(
                renderablesByName: new Dictionary<string, W3dChunk>
                {
                    ["TestContainer.MeshA"] = CreateMesh(),
                },
                hierarchy: hierarchy,
                hlod: hlod with { Lods = [hlod.Lods[0], secondLod] }),
            CreateContext());

        var subObject = Assert.Single(imported.SubObjects);
        Assert.Equal("TestContainer.MeshA", subObject.FullName);
    }

    [Fact]
    public void HLod_MissingRenderObject_ThrowsWithContext()
    {
        var hierarchy = CreateHierarchyDef();
        var hlod = CreateHLod(subObjects: [new W3dHLodSubObject(BoneIndex: 0, Name: "TestContainer.Missing")]);

        var exception = Assert.Throws<InvalidDataException>(() => CreateImporter().Convert(
            CreateFile(
                renderablesByName: new Dictionary<string, W3dChunk>
                {
                    ["TestContainer.MeshA"] = CreateMesh(),
                },
                hierarchy: hierarchy,
                hlod: hlod),
            CreateContext()));

        Assert.Contains("TestContainer.Missing", exception.Message);
    }

    [Fact]
    public void HLod_InvalidBoneIndex_ThrowsWithContext()
    {
        var hierarchy = CreateHierarchyDef(pivots: [CreatePivot("root", -1)]);
        var hlod = CreateHLod(subObjects: [new W3dHLodSubObject(BoneIndex: 5, Name: "TestContainer.MeshA")]);

        var exception = Assert.Throws<InvalidDataException>(() => CreateImporter().Convert(
            CreateFile(
                renderablesByName: new Dictionary<string, W3dChunk>
                {
                    ["TestContainer.MeshA"] = CreateMesh(),
                },
                hierarchy: hierarchy,
                hlod: hlod),
            CreateContext()));

        Assert.Contains("TestContainer.MeshA", exception.Message);
        Assert.Contains("5", exception.Message);
    }

    [Fact]
    public void HLod_NoLodArrays_Throws()
    {
        var hierarchy = CreateHierarchyDef();
        var hlod = CreateHLod() with { Lods = [] };

        Assert.Throws<InvalidDataException>(() => CreateImporter().Convert(
            CreateFile(
                renderablesByName: new Dictionary<string, W3dChunk>
                {
                    ["TestContainer.MeshA"] = CreateMesh(),
                },
                hierarchy: hierarchy,
                hlod: hlod),
            CreateContext()));
    }

    [Fact]
    public void SingleRenderable_BindsToRootBone()
    {
        var imported = CreateImporter().Convert(
            CreateFile(renderablesByName: new Dictionary<string, W3dChunk>
            {
                ["TestContainer.MeshA"] = CreateMesh(),
            }),
            CreateContext());

        var subObject = Assert.Single(imported.SubObjects);
        Assert.Equal("TestContainer.MeshA", subObject.FullName);
        Assert.Equal("MeshA", subObject.DisplayName);
        Assert.Equal(0, subObject.ParentBoneIndex);
    }

    [Fact]
    public void MultipleRenderables_WithoutHlod_Throws()
    {
        var exception = Assert.Throws<InvalidDataException>(() => CreateImporter().Convert(
            CreateFile(renderablesByName: new Dictionary<string, W3dChunk>
            {
                ["TestContainer.MeshA"] = CreateMesh("MeshA"),
                ["TestContainer.MeshB"] = CreateMesh("MeshB"),
            }),
            CreateContext()));

        Assert.Contains("2", exception.Message);
    }

    [Fact]
    public void EmptyModel_Throws()
    {
        Assert.Throws<InvalidDataException>(() => CreateImporter().Convert(
            CreateFile(),
            CreateContext()));
    }

    [Fact]
    public void Box_ConvertsToBounds()
    {
        var hierarchy = CreateHierarchyDef();
        var hlod = CreateHLod(subObjects: [new W3dHLodSubObject(BoneIndex: 0, Name: "TestContainer.BoxA")]);

        var imported = CreateImporter().Convert(
            CreateFile(
                renderablesByName: new Dictionary<string, W3dChunk>
                {
                    ["TestContainer.BoxA"] = new W3dBox(
                        Version: 0,
                        BoxType: W3dBoxType.Aligned,
                        CollisionTypes: 0,
                        Name: "TestContainer.BoxA",
                        Color: new ColorRgb(255, 0, 0),
                        Center: new Vector3(1, 2, 3),
                        Extent: new Vector3(4, 5, 6)),
                },
                hierarchy: hierarchy,
                hlod: hlod),
            CreateContext());

        var subObject = Assert.Single(imported.SubObjects);
        Assert.Equal("BoxA", subObject.DisplayName);

        var box = Assert.IsType<ImportedBox>(subObject.RenderObject);
        Assert.Equal(new Vector3(1, 2, 3), box.Center);
        Assert.Equal(new Vector3(4, 5, 6), box.Extent);
        Assert.Equal(new Vector3(-3, -3, -3), box.BoundingBox.Min);
        Assert.Equal(new Vector3(5, 7, 9), box.BoundingBox.Max);
        Assert.Equal(new Vector3(4, 5, 6).Length(), box.BoundingSphere.Radius);
    }

    [Fact]
    public void UnsupportedRenderObjectChunk_Throws()
    {
        var hierarchy = CreateHierarchyDef();
        var hlod = CreateHLod(subObjects: [new W3dHLodSubObject(BoneIndex: 0, Name: "TestContainer.Other")]);

        var exception = Assert.Throws<NotSupportedException>(() => CreateImporter().Convert(
            CreateFile(
                renderablesByName: new Dictionary<string, W3dChunk>
                {
                    ["TestContainer.Other"] = new W3dUInt32List(W3dChunkType.W3D_CHUNK_SPHERE, []),
                },
                hierarchy: hierarchy,
                hlod: hlod),
            CreateContext()));

        Assert.Contains("W3D_CHUNK_SPHERE", exception.Message);
    }

    [Fact]
    public void ImportSkeleton_EmbeddedHierarchy_ReturnsSkeleton()
    {
        var fileSystem = new MemoryFileSystem();
        AddFileToSystem(fileSystem, CreateFile(
            hierarchy: CreateHierarchyDef(name: "TestHierarchy"),
            filePath: "art/w3d/TestHierarchy.w3d"));

        var imported = CreateImporter().ImportSkeleton("TestHierarchy", CreateContext(fileSystem));

        Assert.NotNull(imported);
        Assert.Equal("TestHierarchy", imported!.Name);
    }

    [Fact]
    public void ImportSkeleton_FileWithoutHierarchy_ReturnsDefaultSkeleton()
    {
        var fileSystem = new MemoryFileSystem();
        AddFileToSystem(fileSystem, CreateFile(
            renderablesByName: new Dictionary<string, W3dChunk>
            {
                ["TestContainer.MeshA"] = CreateMesh(),
            },
            filePath: "art/w3d/mymodel.w3d"));

        var imported = CreateImporter().ImportSkeleton("mymodel", CreateContext(fileSystem));

        Assert.NotNull(imported);
        Assert.Equal("[Default]", imported!.Name);
    }
}
