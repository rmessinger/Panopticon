using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenSage.Content;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.W3d;
using OpenSage.Content.Loaders;
using OpenSage.Data;
using OpenSage.FileFormats.W3d;
using OpenSage.Graphics;
using OpenSage.Graphics.Animation;
using OpenSage.Graphics.Shaders;
using OpenSage.IO;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Rendering;
using OpenSage.Tests.Data;
using Veldrid;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

/// <summary>
/// Locks W3D import parity per the C1 parity contract: representative installed assets must
/// produce the same observable runtime description through the legacy loaders and through
/// importer plus materializer, including fixed-time animation playback. Both paths stay
/// independently callable so comparisons run before the loader cutover.
/// </summary>
public sealed class W3dImportParityTests
{
    [GameFact(SageGame.CncGenerals)]
    public void ModelDescriptor_Parity()
    {
        // Models materialize GPU resources; skip silently on hosts without a headless backend.
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        foreach (var modelName in ModelParityAssets)
        {
            using var fileSystem = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals).CreateFileSystem();
            using var graphics = new TestGraphicsContext(
                fileSystem,
                OnDemandAssetLoadStrategy.CreateW3d(PathResolvers.W3d, PathResolvers.GeneralsTexture));

            var loaderModel = graphics.Context.AssetStore.Models.GetByName(modelName);
            Assert.NotNull(loaderModel);

            var directModel = LoadImportedModel(graphics.Context, modelName);
            Assert.NotNull(directModel);

            AssertModelParity(loaderModel, directModel);

            AssertMaterialsShared(loaderModel, directModel, graphics, modelName);
        }
    }

    // cvswatch01: single-mesh static prop, default hierarchy.
    // cvasncar1: embedded hierarchy plus HLod, multi-mesh, environment-mapped materials.
    // avcomanche: skinned multi-mesh with house-color mesh and embedded animation.
    // airngr_skn: skinned infantry, external hierarchy reference, box sub-object.
    private static readonly string[] ModelParityAssets =
    [
        "cvswatch01",
        "cvasncar1",
        "avcomanche",
        "airngr_skn",
    ];

    [GameFact(SageGame.CncGenerals)]
    public void ReferencedHierarchyDescriptor_Parity()
    {
        using var fileSystem = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals).CreateFileSystem();
        var context = CreateImportingDataOnlyContext(fileSystem);

        var loaderHierarchy = context.AssetStore.ModelBoneHierarchies.GetByName("airngr_skl");
        Assert.NotNull(loaderHierarchy);

        var importedSkeleton = new W3dModelImporter(PathResolvers.W3d).ImportSkeleton(
            "airngr_skl",
            new AssetImportContext(fileSystem, "english"));
        Assert.NotNull(importedSkeleton);

        var importedHierarchy = ImportedModelMaterializer.MaterializeSkeleton(importedSkeleton);

        AssertHierarchyParity(loaderHierarchy, importedHierarchy);
    }

    [GameFact(SageGame.CncGenerals)]
    public void AnimationDescriptor_Parity()
    {
        foreach (var animationKey in AnimationParityAssets)
        {
            using var fileSystem = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals).CreateFileSystem();
            var context = CreateImportingDataOnlyContext(fileSystem);

            var loaderAnimation = context.AssetStore.ModelAnimations.GetByName(animationKey);
            Assert.NotNull(loaderAnimation);

            var importedAnimation = new W3dAnimationImporter(PathResolvers.W3d).ImportAnimation(
                animationKey,
                new AssetImportContext(fileSystem, "english"));
            Assert.NotNull(importedAnimation);

            var materializedAnimation = ImportedAnimationMaterializer.Materialize(importedAnimation);

            AssertAnimationParity(loaderAnimation, materializedAnimation);
        }
    }

    private static readonly string[] AnimationParityAssets =
    [
        "AIRNGR_SKL.AIRNGR_IDA",
        "AVCOMANCHE.AVCOMANCHE",
    ];

    [GameFact(SageGame.CncGenerals)]
    public void AnimationPlayback_FixedTime_Parity()
    {
        foreach (var (skeletonName, animationKey) in PlaybackParityAssets)
        {
            AnimationPlayback_Parity_Run(skeletonName, animationKey);
        }
    }

    private static readonly (string SkeletonName, string AnimationKey)[] PlaybackParityAssets =
    [
        ("airngr_skl", "AIRNGR_SKL.AIRNGR_IDA"),
        ("avcomanche", "AVCOMANCHE.AVCOMANCHE"),
    ];

    private static void AnimationPlayback_Parity_Run(string skeletonName, string animationKey)
    {
        using var fileSystem = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals).CreateFileSystem();
        var context = CreateImportingDataOnlyContext(fileSystem);

        var loaderAnimation = context.AssetStore.ModelAnimations.GetByName(animationKey);
        Assert.NotNull(loaderAnimation);

        var importedAnimation = new W3dAnimationImporter(PathResolvers.W3d).ImportAnimation(
            animationKey,
            new AssetImportContext(fileSystem, "english"));
        Assert.NotNull(importedAnimation);
        var materializedAnimation = ImportedAnimationMaterializer.Materialize(importedAnimation);

        var importedSkeleton = new W3dModelImporter(PathResolvers.W3d).ImportSkeleton(
            skeletonName,
            new AssetImportContext(fileSystem, "english"));
        Assert.NotNull(importedSkeleton);
        var hierarchy = ImportedModelMaterializer.MaterializeSkeleton(importedSkeleton);

        var legacyBones = hierarchy.Bones.Select(b => new ModelBoneInstance(b)).ToArray();
        var importedBones = hierarchy.Bones.Select(b => new ModelBoneInstance(b)).ToArray();

        var legacyInstance = new AnimationInstance(
            legacyBones, loaderAnimation.Bind(hierarchy), AnimationMode.Once, AnimationFlags.StartFrameFirst, null, new QuoteUnquoteRandom());
        var importedInstance = new AnimationInstance(
            importedBones, materializedAnimation.Bind(hierarchy), AnimationMode.Once, AnimationFlags.StartFrameFirst, null, new QuoteUnquoteRandom());

        legacyInstance.Play();
        importedInstance.Play();

        var totalTime = TimeSpan.Zero;
        var step = TimeSpan.FromSeconds(0.5);
        while (totalTime <= loaderAnimation.Duration)
        {
            var time = new TimeInterval(totalTime, step);
            legacyInstance.Update(time);
            importedInstance.Update(time);

            for (var i = 0; i < legacyBones.Length; i++)
            {
                Assert.True(
                    Matrix4x4.Decompose(legacyBones[i].Matrix, out _, out var legacyRotation, out var legacyTranslation),
                    "Legacy bone matrix decomposition failed.");
                Assert.True(
                    Matrix4x4.Decompose(importedBones[i].Matrix, out _, out var importedRotation, out var importedTranslation),
                    "Imported bone matrix decomposition failed.");

                Assert.Equal(legacyTranslation, importedTranslation);
                Assert.Equal(legacyRotation, importedRotation);
                Assert.Equal(legacyBones[i].Visible, importedBones[i].Visible);
            }

            totalTime += step;
        }
    }

    [Fact]
    public void ShaderMaterialModel_DescriptorAndMaterial_Parity()
    {
        // No shipped Generals asset uses named shader materials, so this representative case
        // is synthetic. Both runtime paths receive the same parsed W3D file.
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        var w3dFile = ShaderMaterialTestFile.Create();

        var fileSystem = new MemoryFileSystem();
        using var output = new MemoryStream();
        w3dFile.WriteTo(output);
        fileSystem.AddFile(w3dFile.FilePath, output.ToArray());

        using var graphics = new TestGraphicsContext(
            fileSystem,
            OnDemandAssetLoadStrategy.CreateW3d(PathResolvers.W3d, PathResolvers.GeneralsTexture));

        var loaderModel = graphics.Context.AssetStore.Models.GetByName("shadermaterialtest");
        Assert.NotNull(loaderModel);
        var loaderMesh = (ModelMesh)loaderModel.SubObjects[0].RenderObject;

        var direct = new W3dModelImporter(PathResolvers.W3d).Convert(
            w3dFile,
            new AssetImportContext(fileSystem, "english"));
        Assert.NotNull(direct);
        var directModel = ImportedModelMaterializer.MaterializeModel(direct, graphics.Context);
        var directMesh = (ModelMesh)directModel.SubObjects[0].RenderObject;

        AssertMeshParity(DescribeMesh(loaderMesh), DescribeMesh(directMesh));

        // Named shader-material properties are compared at conversion level in
        // W3dMaterialConverterTests and at resource-set level in ImportedModelMaterializerTests;
        // here shader selection and pipeline identity must agree. A named shader-material
        // shader set owns a single shared pipeline, so equality is instance identity.
        Assert.Same(
            loaderMesh.MeshParts[0].Material.ForwardPass.ShaderSet,
            directMesh.MeshParts[0].Material.ForwardPass.ShaderSet);
        Assert.Same(
            loaderMesh.MeshParts[0].Material.ForwardPass.Pipeline,
            directMesh.MeshParts[0].Material.ForwardPass.Pipeline);
    }

    private static AssetLoadContext CreateImportingDataOnlyContext(FileSystem fileSystem)
    {
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

    private static Model LoadImportedModel(AssetLoadContext context, string name)
    {
        var importer = new W3dModelImporter(PathResolvers.W3d);
        var imported = importer.ImportModel(name, new AssetImportContext(context.FileSystem, context.Language));
        Assert.NotNull(imported);
        return ImportedModelMaterializer.MaterializeModel(imported, context);
    }

    private static void AssertMaterialsShared(Model legacyModel, Model importedModel, TestGraphicsContext graphics, string modelName)
    {
        var legacyMeshes = legacyModel.SubObjects.Select(s => s.RenderObject).OfType<ModelMesh>().ToList();
        var importedMeshes = importedModel.SubObjects.Select(s => s.RenderObject).OfType<ModelMesh>().ToList();

        Assert.Equal(legacyMeshes.Count, importedMeshes.Count);

        for (var i = 0; i < legacyMeshes.Count; i++)
        {
            Assert.Equal(legacyMeshes[i].MeshParts.Count, importedMeshes[i].MeshParts.Count);

            for (var j = 0; j < legacyMeshes[i].MeshParts.Count; j++)
            {
                var legacyPart = legacyMeshes[i].MeshParts[j];
                var importedPart = importedMeshes[i].MeshParts[j];

                // Shader selection parity for every part.
                Assert.Same(
                    legacyPart.Material.ForwardPass.ShaderSet,
                    importedPart.Material.ForwardPass.ShaderSet);

                // Fixed-function materials share one cache, so identical inputs must reuse the
                // same Material instance. This proves render state, shading constants, texture
                // mapping, and bound textures are equal for valid shipped assets.
                if (legacyPart.Material.ForwardPass.ShaderSet == graphics.ShaderResources.FixedFunction)
                {
                    Assert.Same(legacyPart.Material.ForwardPass, importedPart.Material.ForwardPass);
                    Assert.Same(legacyPart.MaterialBlend.ForwardPass, importedPart.MaterialBlend.ForwardPass);
                }
                else
                {
                    Assert.Same(
                        legacyPart.Material.ForwardPass.Pipeline,
                        importedPart.Material.ForwardPass.Pipeline);
                }
            }
        }
    }

    private sealed record ModelDescriptor(
        string Name,
        HierarchyDescriptor Hierarchy,
        SubObjectDescriptor[] SubObjects,
        bool HasSkinnedMeshes);

    private sealed record HierarchyDescriptor(
        string Name,
        BoneDescriptor[] Bones);

    private sealed record BoneDescriptor(
        string Name,
        int ParentIndex,
        Vector3 Translation,
        Quaternion Rotation);

    private sealed record SubObjectDescriptor(
        string FullName,
        string Name,
        int BoneIndex,
        string RenderObjectName,
        MeshDescriptor? Mesh,
        BoxDescriptor? Box);

    private sealed record MeshDescriptor(
        string Name,
        uint VertexCount,
        uint IndexCount,
        Vector3 BoxMin,
        Vector3 BoxMax,
        Vector3 SphereCenter,
        float SphereRadius,
        bool Skinned,
        bool Hidden,
        bool CameraOriented,
        MeshPartDescriptor[] Parts);

    private sealed record MeshPartDescriptor(
        uint StartIndex,
        uint IndexCount,
        bool BlendEnabled,
        int ShaderSetId);

    private sealed record BoxDescriptor(
        Vector3 BoxMin,
        Vector3 BoxMax,
        Vector3 SphereCenter,
        float SphereRadius);

    private sealed record AnimationDescriptor(
        string Name,
        TimeSpan Duration,
        AnimationClipDescriptor[] Clips);

    private sealed record AnimationClipDescriptor(
        AnimationClipType ClipType,
        int Bone,
        KeyframeDescriptor[] Keyframes);

    private sealed record KeyframeDescriptor(
        TimeSpan Time,
        Quaternion Quaternion,
        float FloatValue,
        bool BoolValue);

    // Records compare nested arrays by reference, so parity walks each level explicitly.
    private static void AssertModelParity(Model expected, Model actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        AssertHierarchyParity(expected.BoneHierarchy, actual.BoneHierarchy);

        var expectedSubObjects = expected.SubObjects.Select(DescribeSubObject).ToArray();
        var actualSubObjects = actual.SubObjects.Select(DescribeSubObject).ToArray();
        Assert.Equal(expectedSubObjects.Length, actualSubObjects.Length);
        for (var i = 0; i < expectedSubObjects.Length; i++)
        {
            var expectedSubObject = expectedSubObjects[i];
            var actualSubObject = actualSubObjects[i];
            Assert.Equal(expectedSubObject.FullName, actualSubObject.FullName);
            Assert.Equal(expectedSubObject.Name, actualSubObject.Name);
            Assert.Equal(expectedSubObject.BoneIndex, actualSubObject.BoneIndex);
            Assert.Equal(expectedSubObject.RenderObjectName, actualSubObject.RenderObjectName);
            Assert.Equal(expectedSubObject.Box, actualSubObject.Box);

            if (expectedSubObject.Mesh is { } expectedMesh)
            {
                Assert.NotNull(actualSubObject.Mesh);
                AssertMeshParity(expectedMesh, actualSubObject.Mesh!);
            }
            else
            {
                Assert.Null(actualSubObject.Mesh);
            }
        }

        Assert.Equal(expected.HasSkinnedMeshes, actual.HasSkinnedMeshes);
    }

    private static void AssertHierarchyParity(ModelBoneHierarchy expected, ModelBoneHierarchy actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(
            expected.Bones.Select(DescribeBone).ToArray(),
            actual.Bones.Select(DescribeBone).ToArray());
    }

    private static void AssertMeshParity(MeshDescriptor expectedDescriptor, MeshDescriptor actualDescriptor)
    {
        Assert.Equal(expectedDescriptor.Name, actualDescriptor.Name);
        Assert.Equal(expectedDescriptor.VertexCount, actualDescriptor.VertexCount);
        Assert.Equal(expectedDescriptor.IndexCount, actualDescriptor.IndexCount);
        Assert.Equal(expectedDescriptor.BoxMin, actualDescriptor.BoxMin);
        Assert.Equal(expectedDescriptor.BoxMax, actualDescriptor.BoxMax);
        Assert.Equal(expectedDescriptor.SphereCenter, actualDescriptor.SphereCenter);
        Assert.Equal(expectedDescriptor.SphereRadius, actualDescriptor.SphereRadius);
        Assert.Equal(expectedDescriptor.Skinned, actualDescriptor.Skinned);
        Assert.Equal(expectedDescriptor.Hidden, actualDescriptor.Hidden);
        Assert.Equal(expectedDescriptor.CameraOriented, actualDescriptor.CameraOriented);
        Assert.Equal(expectedDescriptor.Parts, actualDescriptor.Parts);
    }

    private static void AssertAnimationParity(ModelAnimation expected, ModelAnimation actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Duration, actual.Duration);

        var hierarchy = CreateWideHierarchy();
        var expectedClips = expected.Bind(hierarchy).Clips.Select(DescribeClip).ToArray();
        var actualClips = actual.Bind(hierarchy).Clips.Select(DescribeClip).ToArray();
        Assert.Equal(expectedClips.Length, actualClips.Length);
        for (var i = 0; i < expectedClips.Length; i++)
        {
            Assert.Equal(expectedClips[i].ClipType, actualClips[i].ClipType);
            Assert.Equal(expectedClips[i].Bone, actualClips[i].Bone);
            Assert.Equal(expectedClips[i].Keyframes, actualClips[i].Keyframes);
        }
    }

    private static ModelDescriptor DescribeModel(Model model)
    {
        return new ModelDescriptor(
            model.Name,
            DescribeHierarchy(model.BoneHierarchy),
            model.SubObjects.Select(DescribeSubObject).ToArray(),
            model.HasSkinnedMeshes);
    }

    private static HierarchyDescriptor DescribeHierarchy(ModelBoneHierarchy hierarchy)
    {
        return new HierarchyDescriptor(
            hierarchy.Name,
            hierarchy.Bones.Select(DescribeBone).ToArray());
    }

    private static BoneDescriptor DescribeBone(ModelBone bone)
    {
        return new BoneDescriptor(
            bone.Name ?? string.Empty,
            bone.Parent?.Index ?? -1,
            bone.Transform.Translation,
            bone.Transform.Rotation);
    }

    private static AnimationClipDescriptor DescribeClip(AnimationClip clip)
    {
        return new AnimationClipDescriptor(
            clip.ClipType,
            clip.Bone,
            clip.Keyframes.Select(k => new KeyframeDescriptor(
                k.Time,
                k.Value.Quaternion,
                k.Value.FloatValue,
                k.Value.BoolValue)).ToArray());
    }

    private static SubObjectDescriptor DescribeSubObject(ModelSubObject subObject)
    {
        return new SubObjectDescriptor(
            subObject.FullName,
            subObject.Name,
            subObject.Bone.Index,
            subObject.RenderObject.Name,
            subObject.RenderObject as ModelMesh is { } mesh ? DescribeMesh(mesh) : null,
            subObject.RenderObject as ModelBox is { } box
                ? new BoxDescriptor(
                    box.BoundingBox.Min,
                    box.BoundingBox.Max,
                    box.BoundingSphere.Center,
                    box.BoundingSphere.Radius)
                : null);
    }

    private static MeshDescriptor DescribeMesh(ModelMesh mesh)
    {
        return new MeshDescriptor(
            mesh.Name,
            (uint)(mesh.VertexBuffer.SizeInBytes / Marshal.SizeOf<MeshShaderResources.MeshVertex.Basic>()),
            mesh.MeshParts.Aggregate(0u, (total, part) => total + part.IndexCount),
            mesh.BoundingBox.Min,
            mesh.BoundingBox.Max,
            mesh.BoundingSphere.Center,
            mesh.BoundingSphere.Radius,
            mesh.Skinned,
            mesh.Hidden,
            mesh.CameraOriented,
            mesh.MeshParts.Select(DescribeMeshPart).ToArray());
    }

    private static MeshPartDescriptor DescribeMeshPart(ModelMeshPart part)
    {
        var material = part.Material.ForwardPass;
        return new MeshPartDescriptor(
            part.StartIndex,
            part.IndexCount,
            part.BlendEnabled,
            material.ShaderSet.Id);
    }

    private static AnimationDescriptor DescribeAnimation(ModelAnimation animation)
    {
        return new AnimationDescriptor(
            animation.Name,
            animation.Duration,
            animation.Bind(CreateWideHierarchy()).Clips.Select(DescribeClip).ToArray());
    }

    private static ModelBoneHierarchy CreateWideHierarchy()
    {
        var bones = new ImportedBone[1024];
        for (var i = 0; i < bones.Length; i++)
        {
            bones[i] = new ImportedBone($"Bone{i}", i - 1, Vector3.Zero, Quaternion.Identity);
        }

        return new ModelBoneHierarchy(new ImportedSkeleton("ParitySkeleton", bones));
    }

    private sealed class QuoteUnquoteRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int lo, int hi)
        {
            return lo;
        }

        public float NextSingle(float lo, float hi)
        {
            return lo;
        }
    }

    private static class ShaderMaterialTestFile
    {
        public static W3dFile Create()
        {
            var mesh = new W3dMesh(
                Header: CreateHeader(),
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
                MaterialInfo: new W3dMaterialInfo(
                    PassCount: 1,
                    VertexMaterialCount: 0,
                    ShaderCount: 0,
                    TextureCount: 0),
                VertexMaterials: null,
                Shaders: null,
                ShadersPs2: null,
                Textures: null,
                MaterialPasses:
                [
                    new W3dMaterialPass(
                        VertexMaterialIds: null,
                        ShaderIds: null,
                        ShaderMaterialIds: new W3dUInt32List(W3dChunkType.W3D_CHUNK_SHADER_MATERIAL_ID, [0u]),
                        Dcg: null,
                        Dig: null,
                        Scg: null,
                        TextureStages: [],
                        TexCoords: new W3dVector2List(W3dChunkType.W3D_CHUNK_STAGE_TEXCOORDS,
                            [new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1)])),
                ],
                AabTree: null,
                ShaderMaterials: new W3dShaderMaterials(
                [
                    new W3dShaderMaterial(
                        new W3dShaderMaterialHeader(Number: 0, TypeName: "Simple.fx"),
                    [
                        new W3dShaderMaterialProperty(
                            W3dShaderMaterialPropertyType.Vector4,
                            "ColorEmissive",
                            "",
                            new W3dShaderMaterialPropertyValue { Vector4 = new Vector4(1, 2, 3, 4) }),
                        new W3dShaderMaterialProperty(
                            W3dShaderMaterialPropertyType.Vector4,
                            "TexCoordTransform_0",
                            "",
                            new W3dShaderMaterialPropertyValue { Vector4 = new Vector4(0.1f, 0.2f, 0.3f, 0.4f) }),
                        new W3dShaderMaterialProperty(
                            W3dShaderMaterialPropertyType.Texture,
                            "Texture_0",
                            "TestTexture.tga",
                            new W3dShaderMaterialPropertyValue()),
                    ]),
                ]),
                PrelitVertex: null,
                PrelitLightMapMultiPass: null,
                PrelitLightMapMultiTexture: null,
                PrelitUnlit: null);

            return new W3dFile(
                FilePath: "art/w3d/shadermaterialtest.w3d",
                Chunks: [mesh],
                RenderableObjects: [mesh],
                RenderableObjectsByName: new Dictionary<string, W3dChunk> { ["ShaderMatTest"] = mesh },
                Hierarchy: null,
                HLod: null);
        }

        private static W3dMeshHeader3 CreateHeader()
        {
            return new W3dMeshHeader3(
                Version: 0,
                Attributes: 0,
                MeshName: "ShaderMatTest",
                ContainerName: "TestContainer",
                NumTris: 1,
                NumVertices: 3,
                NumMaterials: 1,
                NumDamageStages: 0,
                SortLevel: 0,
                PrelitVersion: 0,
                FutureCounts: 0,
                VertexChannels: W3dVertexChannels.Location | W3dVertexChannels.Normal,
                FaceChannels: W3dFaceChannels.Face,
                Min: Vector3.Zero,
                Max: Vector3.One,
                SphCenter: new Vector3(0.5f, 0.5f, 0.5f),
                SphRadius: 1.5f);
        }
    }
}
