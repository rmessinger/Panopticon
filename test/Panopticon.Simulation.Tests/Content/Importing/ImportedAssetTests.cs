using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class ImportedAssetTests
{
    private static ImportedMaterial CreateMaterial()
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
            fixedFunctionShading: new ImportedFixedFunctionShading(
                ImportedDiffuseLightingType.Modulate,
                specularEnabled: false,
                texturingEnabled: true,
                secondaryTextureColorBlend: ImportedSecondaryTextureBlend.Disable,
                secondaryTextureAlphaBlend: ImportedSecondaryTextureBlend.Disable,
                alphaTest: false,
                ambient: Vector3.One,
                diffuse: Vector3.One,
                specular: Vector3.Zero,
                emissive: Vector3.Zero,
                shininess: 0.0f,
                opacity: 1.0f,
                textureStageCount: 1,
                CreateDefaultTextureMapping(),
                CreateDefaultTextureMapping()),
            properties: new[] { ImportedMaterialProperty.FromTextureName("Texture0", "AVTest.tga") });
    }

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

    private static ImportedMesh CreateMesh(
        ImportedVertex[] vertices,
        ushort[] indices,
        bool isSkinned = false,
        string? primitiveId = null,
        IReadOnlyList<ImportedMorphTargetSet>? morphTargetSets = null)
    {
        return new ImportedMesh(
            vertices,
            indices,
            new AxisAlignedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)),
            new BoundingSphere(Vector3.Zero, 1.0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: isSkinned,
            materials: new[] { CreateMaterial() },
            meshParts: new[]
            {
                new ImportedMeshPart(
                    new Vector2[] { new(0, 0), new(1, 0), new(0, 1) },
                    null,
                    startIndex: 0,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 0),
            },
            primitiveId,
            morphTargetSets);
    }

    private static ImportedVertex StaticVertex(float x, float y, float z)
    {
        return new ImportedVertex(new Vector3(x, y, z), Vector3.UnitZ);
    }

    private static ImportedModel CreateStaticModel()
    {
        var skeleton = new ImportedSkeleton(
            "TestSkeleton",
            new[] { new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity) });

        return new ImportedModel(
            "TestModel",
            skeleton,
            new[]
            {
                new ImportedSubObject(
                    "TestModel.Mesh",
                    "Mesh",
                    parentBoneIndex: 0,
                    renderObject: CreateMesh(
                        new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) },
                        new ushort[] { 0, 1, 2 })),
            },
            ImportedSkeletonOrigin.Embedded);
    }

    [Fact]
    public void ValidStaticModel_Constructs_And_CopiesCallerArrays()
    {
        var vertices = new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) };
        var indices = new ushort[] { 0, 1, 2 };


        var mesh = CreateMesh(vertices, indices);
        var model = CreateStaticModel();

        vertices[0] = StaticVertex(9, 9, 9);
        indices[0] = 2;

        Assert.NotEqual(vertices[0].Position, mesh.Vertices[0].Position);
        Assert.Equal(0, mesh.Indices[0]);
        Assert.Single(model.SubObjects);
        Assert.Equal("TestModel", model.Name);
        Assert.Single(model.Skeleton.Bones);
    }

    [Fact]
    public void ValidSkinnedModel_And_Animation_Construct()
    {
        var skeleton = new ImportedSkeleton(
            "SkinnedSkeleton",
            new[]
            {
                new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity),
                new ImportedBone("Arm", 0, new Vector3(0, 1, 0), Quaternion.Identity),
            });

        var skinnedVertices = new[]
        {
            new ImportedVertex(
                new Vector3(0, 0, 0),
                Vector3.UnitZ,
                boneIndex0: 0,
                boneIndex1: 1,
                boneWeight0: 0.7f,
                boneWeight1: 0.3f),
            new ImportedVertex(
                new Vector3(1, 0, 0),
                Vector3.UnitZ,
                boneIndex0: 1,
                boneIndex1: 1,
                boneWeight0: 1.0f,
                boneWeight1: 0.0f),
            new ImportedVertex(
                new Vector3(0, 1, 0),
                Vector3.UnitZ,
                boneIndex0: 0,
                boneIndex1: 0,
                boneWeight0: 0.5f,
                boneWeight1: 0.5f),
        };

        var model = new ImportedModel(
            "SkinnedModel",
            skeleton,
            new[]
            {
                new ImportedSubObject(
                    "SkinnedModel.Body",
                    "Body",
                    parentBoneIndex: 0,
                    renderObject: CreateMesh(skinnedVertices, new ushort[] { 0, 1, 2 }, isSkinned: true)),
            },
            ImportedSkeletonOrigin.Embedded);

        var animation = new ImportedAnimation(
            "SkinnedSkeleton.Run",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 1,
                    ImportedAnimationChannelType.Quaternion,
                    new[]
                    {
                        ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity),
                        ImportedKeyframe.FromQuaternion(
                            TimeSpan.FromSeconds(1.0),
                            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4)),
                    }),
                new ImportedAnimationChannel(
                    boneIndex: 0,
                    ImportedAnimationChannelType.Visibility,
                    new[]
                    {
                        ImportedKeyframe.FromBool(TimeSpan.Zero, true),
                    }),
            });

        Assert.True(model.SubObjects[0].RenderObject is ImportedMesh { IsSkinned: true });
        Assert.Equal(2, animation.Channels.Count);
    }

    [Fact]
    public void FourSlots_Construct_WhenSecondaryGeometryMatchesPrimary()
    {
        var vertex = new ImportedVertex(
            new Vector3(0, 0, 0),
            Vector3.UnitZ,
            secondaryPosition: new Vector3(0, 0, 0),
            secondaryNormal: Vector3.UnitZ,
            boneIndex0: 0,
            boneIndex1: 1,
            boneIndex2: 1,
            boneIndex3: 0,
            boneWeight0: 0.4f,
            boneWeight1: 0.3f,
            boneWeight2: 0.2f,
            boneWeight3: 0.1f);

        Assert.Equal(1u, vertex.BoneIndex2);
        Assert.Equal(0u, vertex.BoneIndex3);
        Assert.Equal(0.2f, vertex.BoneWeight2);
        Assert.Equal(0.1f, vertex.BoneWeight3);
    }

    [Fact]
    public void Constructor_RejectsOutOfRangeWeightOnSlotsTwoAndThree()
    {
        Assert.Throws<ArgumentException>(() => new ImportedVertex(
            Vector3.Zero, Vector3.UnitZ, boneWeight2: 1.5f));

        Assert.Throws<ArgumentException>(() => new ImportedVertex(
            Vector3.Zero, Vector3.UnitZ, boneWeight3: -0.1f));
    }

    [Fact]
    public void UnskinnedMesh_RejectsSkinningOnSlotsTwoAndThree()
    {
        var vertices = new[]
        {
            new ImportedVertex(Vector3.Zero, Vector3.UnitZ, boneIndex2: 1),
            StaticVertex(1, 0, 0),
            StaticVertex(0, 1, 0),
        };

        var ex = Assert.Throws<ArgumentException>(
            () => CreateMesh(vertices, new ushort[] { 0, 1, 2 }));
        Assert.Contains("not skinned", ex.Message);

        var weighted = new[]
        {
            new ImportedVertex(Vector3.Zero, Vector3.UnitZ, boneWeight3: 0.5f),
            StaticVertex(1, 0, 0),
            StaticVertex(0, 1, 0),
        };

        Assert.Throws<ArgumentException>(
            () => CreateMesh(weighted, new ushort[] { 0, 1, 2 }));
    }

    [Fact]
    public void SkinnedMesh_RejectsSlotTwoWeightWithDifferingSecondaryGeometry()
    {
        var vertices = new[]
        {
            new ImportedVertex(
                new Vector3(0, 0, 0),
                Vector3.UnitZ,
                secondaryPosition: new Vector3(1, 0, 0),
                secondaryNormal: Vector3.UnitZ,
                boneIndex2: 1,
                boneWeight2: 0.5f),
            StaticVertex(1, 0, 0),
            StaticVertex(0, 1, 0),
        };

        var ex = Assert.Throws<ArgumentException>(
            () => CreateMesh(vertices, new ushort[] { 0, 1, 2 }, isSkinned: true));
        Assert.Contains("Vertex 0", ex.Message);
        Assert.Contains("slots 2 and 3", ex.Message);
    }

    [Fact]
    public void SkinnedModel_RejectsBoneIndexOutOfRangeOnSlotsTwoAndThree()
    {
        var skeleton = new ImportedSkeleton(
            "S",
            new[] { new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity) });

        var vertices = new[]
        {
            new ImportedVertex(
                Vector3.Zero, Vector3.UnitZ, boneIndex2: 3, boneWeight2: 0.5f),
            StaticVertex(1, 0, 0),
            StaticVertex(0, 1, 0),
        };

        var ex = Assert.Throws<ArgumentException>(() => new ImportedModel(
            "M",
            skeleton,
            new[]
            {
                new ImportedSubObject(
                    "M.Mesh",
                    "Mesh",
                    parentBoneIndex: 0,
                    renderObject: CreateMesh(vertices, new ushort[] { 0, 1, 2 }, isSkinned: true)),
            },
            ImportedSkeletonOrigin.Embedded));
        Assert.Contains("outside the skeleton", ex.Message);
    }

    [Fact]
    public void Skeleton_RejectsParentAfterChild()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImportedSkeleton(
                "S",
                new[]
                {
                    new ImportedBone("Child", 1, Vector3.Zero, Quaternion.Identity),
                    new ImportedBone("Parent", -1, Vector3.Zero, Quaternion.Identity),
                }));
    }

    [Fact]
    public void Skeleton_RejectsEmptyBoneList()
    {
        Assert.Throws<ArgumentException>(() => new ImportedSkeleton("S", Array.Empty<ImportedBone>()));
    }

    [Fact]
    public void Bone_RejectsInvalidParentIndex()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImportedBone("B", -2, Vector3.Zero, Quaternion.Identity));
    }

    [Fact]
    public void Model_RejectsSubObjectBoneIndexOutOfRange()
    {
        var skeleton = new ImportedSkeleton(
            "S",
            new[] { new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity) });

        Assert.Throws<ArgumentException>(() =>
            new ImportedModel(
                "M",
                skeleton,
                new[]
                {
                    new ImportedSubObject(
                        "M.Mesh",
                        "Mesh",
                        parentBoneIndex: 1,
                        renderObject: CreateMesh(
                            new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) },
                            new ushort[] { 0, 1, 2 })),
                },
                ImportedSkeletonOrigin.Embedded));
    }

    [Fact]
    public void Model_RejectsSkinnedVertexBoneIndexOutOfRange()
    {
        var skeleton = new ImportedSkeleton(
            "S",
            new[] { new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity) });

        var skinnedVertex = new ImportedVertex(
            Vector3.Zero,
            Vector3.UnitZ,
            boneIndex0: 2,
            boneIndex1: 0,
            boneWeight0: 1.0f,
            boneWeight1: 0.0f);

        Assert.Throws<ArgumentException>(() =>
            new ImportedModel(
                "M",
                skeleton,
                new[]
                {
                    new ImportedSubObject(
                        "M.Mesh",
                        "Mesh",
                        parentBoneIndex: 0,
                        renderObject: CreateMesh(
                            new[] { skinnedVertex, skinnedVertex, skinnedVertex },
                            new ushort[] { 0, 1, 2 },
                            isSkinned: true)),
                },
                ImportedSkeletonOrigin.Embedded));
    }

    [Fact]
    public void Mesh_RejectsIndexBeyondVertexCount()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateMesh(
                new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) },
                new ushort[] { 0, 1, 3 }));
    }

    [Fact]
    public void Mesh_RejectsPartRangeBeyondIndexCount()
    {
        var vertices = new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) };

        Assert.Throws<ArgumentException>(() =>
            new ImportedMesh(
                vertices,
                new ushort[] { 0, 1, 2 },
                new AxisAlignedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)),
                new BoundingSphere(Vector3.Zero, 1.0f),
                hidden: false,
                cameraOriented: false,
                hasHouseColor: false,
                isSkinned: false,
                materials: new[] { CreateMaterial() },
                meshParts: new[]
                {
                    new ImportedMeshPart(
                        null,
                        null,
                        startIndex: 3,
                        indexCount: 3,
                        blendEnabled: false,
                        materialIndex: 0),
                }));
    }

    [Fact]
    public void Mesh_RejectsPartMaterialIndexOutOfRange()
    {
        var vertices = new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) };

        Assert.Throws<ArgumentException>(() =>
            new ImportedMesh(
                vertices,
                new ushort[] { 0, 1, 2 },
                new AxisAlignedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)),
                new BoundingSphere(Vector3.Zero, 1.0f),
                hidden: false,
                cameraOriented: false,
                hasHouseColor: false,
                isSkinned: false,
                materials: new[] { CreateMaterial() },
                meshParts: new[]
                {
                    new ImportedMeshPart(
                        null,
                        null,
                        startIndex: 0,
                        indexCount: 3,
                        blendEnabled: false,
                        materialIndex: 1),
                }));
    }

    [Fact]
    public void Mesh_RejectsTextureCoordinateCountMismatch()
    {
        var vertices = new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) };

        Assert.Throws<ArgumentException>(() =>
            new ImportedMesh(
                vertices,
                new ushort[] { 0, 1, 2 },
                new AxisAlignedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)),
                new BoundingSphere(Vector3.Zero, 1.0f),
                hidden: false,
                cameraOriented: false,
                hasHouseColor: false,
                isSkinned: false,
                materials: new[] { CreateMaterial() },
                meshParts: new[]
                {
                    new ImportedMeshPart(
                        new Vector2[] { new(0, 0), new(1, 0) },
                        null,
                        startIndex: 0,
                        indexCount: 3,
                        blendEnabled: false,
                        materialIndex: 0),
                }));
    }

    [Fact]
    public void Mesh_RejectsSkinDataOnNonSkinnedMesh()
    {
        var skinnedVertex = new ImportedVertex(
            Vector3.Zero,
            Vector3.UnitZ,
            boneIndex0: 0,
            boneIndex1: 0,
            boneWeight0: 1.0f,
            boneWeight1: 0.0f);

        Assert.Throws<ArgumentException>(() =>
            CreateMesh(
                new[] { skinnedVertex, skinnedVertex, skinnedVertex },
                new ushort[] { 0, 1, 2 }));
    }

    [Fact]
    public void Vertex_RejectsOutOfRangeBoneWeight()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImportedVertex(
                Vector3.Zero,
                Vector3.UnitZ,
                boneIndex0: 0,
                boneIndex1: 0,
                boneWeight0: 1.5f,
                boneWeight1: 0.0f));
    }

    [Fact]
    public void Mesh_RejectsTooManyVerticesForIndexFormat()
    {
        var vertices = new ImportedVertex[ushort.MaxValue + 1];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = StaticVertex(0, 0, 0);
        }

        Assert.Throws<ArgumentException>(() =>
            CreateMesh(vertices, new ushort[] { 0, 1, 2 }));
    }

    [Fact]
    public void Mesh_RejectsPartIndexCountNotMultipleOfThree()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImportedMeshPart(
                null,
                null,
                startIndex: 0,
                indexCount: 4,
                blendEnabled: false,
                materialIndex: 0));
    }

    [Fact]
    public void Animation_RejectsNegativeBoneIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ImportedAnimationChannel(
                boneIndex: -1,
                ImportedAnimationChannelType.Quaternion,
                new[] { ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity) }));
    }

    [Fact]
    public void Animation_RejectsDecreasingKeyframeTimes()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImportedAnimationChannel(
                boneIndex: 0,
                ImportedAnimationChannelType.TranslationX,
                new[]
                {
                    ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(1.0), 1.0f),
                    ImportedKeyframe.FromFloat(TimeSpan.Zero, 0.0f),
                }));
    }

    [Fact]
    public void Animation_RejectsKeyframeKindMismatch()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImportedAnimationChannel(
                boneIndex: 0,
                ImportedAnimationChannelType.Quaternion,
                new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 0.0f) }));
    }

    [Fact]
    public void Animation_RejectsEmptyKeyframes()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImportedAnimationChannel(
                boneIndex: 0,
                ImportedAnimationChannelType.Visibility,
                Array.Empty<ImportedKeyframe>()));
    }

    [Fact]
    public void MaterialProperty_RejectsKindMismatchOnRead()
    {
        var property = ImportedMaterialProperty.FromFloat("Opacity", 0.5f);

        Assert.Equal(0.5f, property.FloatValue);
        Assert.Throws<InvalidOperationException>(() => property.BoolValue);
        Assert.Throws<InvalidOperationException>(() => property.TextureName);
    }

    [Fact]
    public void MaterialProperty_RejectsEmptyNameAndTextureName()
    {
        Assert.Throws<ArgumentException>(() => ImportedMaterialProperty.FromBool("", true));
        Assert.Throws<ArgumentException>(() => ImportedMaterialProperty.FromTextureName("Texture0", ""));
    }

    [Fact]
    public void Keyframe_RejectsKindMismatchOnRead()
    {
        var keyframe = ImportedKeyframe.FromBool(TimeSpan.Zero, true);

        Assert.True(keyframe.BoolValue);
        Assert.Throws<InvalidOperationException>(() => keyframe.FloatValue);
    }

    [Fact]
    public void Box_ComputesBounds_And_RejectsNegativeExtent()
    {
        var box = new ImportedBox(new Vector3(1, 2, 3), new Vector3(1, 1, 1));

        Assert.Equal(new Vector3(0, 1, 2), box.BoundingBox.Min);
        Assert.Equal(new Vector3(2, 3, 4), box.BoundingBox.Max);
        Assert.Equal(MathF.Sqrt(3), box.BoundingSphere.Radius, 5);

        Assert.Throws<ArgumentException>(() => new ImportedBox(Vector3.Zero, new Vector3(-1, 1, 1)));
    }

    [Fact]
    public void AnimationTarget_DirectIndex_HoldsIndex_And_RejectsWrongKindAccess()
    {
        var target = ImportedAnimationTarget.CreateDirectIndex(7);

        Assert.Equal(ImportedAnimationTargetKind.DirectIndex, target.Kind);
        Assert.Equal(7, target.DirectIndex);
        Assert.Throws<InvalidOperationException>(() => target.BoneName);
    }

    [Fact]
    public void AnimationTarget_BoneName_PreservesExactName_And_RejectsWrongKindAccess()
    {
        var target = ImportedAnimationTarget.CreateBoneName("  Root_BONE.01 ");

        Assert.Equal(ImportedAnimationTargetKind.BoneName, target.Kind);
        Assert.Equal("  Root_BONE.01 ", target.BoneName);
        Assert.Throws<InvalidOperationException>(() => target.DirectIndex);
    }

    [Fact]
    public void AnimationTarget_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImportedAnimationTarget.CreateDirectIndex(-1));
        Assert.Throws<ArgumentNullException>(() => ImportedAnimationTarget.CreateBoneName(null!));
        Assert.Throws<ArgumentException>(() => ImportedAnimationTarget.CreateBoneName(""));
    }

    [Fact]
    public void AnimationChannel_TargetConstructor_CreatesBoundChannel_And_IntConstructorStaysDirectIndex()
    {
        var keyframes = new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 1.0f) };

        var namedChannel = new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateBoneName("Bip01"),
            ImportedAnimationChannelType.TranslationX,
            keyframes);
        Assert.Equal(ImportedAnimationTargetKind.BoneName, namedChannel.Target.Kind);
        Assert.Equal("Bip01", namedChannel.Target.BoneName);
        Assert.Throws<InvalidOperationException>(() => namedChannel.BoneIndex);

        var directChannel = new ImportedAnimationChannel(
            boneIndex: 4,
            ImportedAnimationChannelType.TranslationX,
            keyframes);
        Assert.Equal(ImportedAnimationTargetKind.DirectIndex, directChannel.Target.Kind);
        Assert.Equal(4, directChannel.Target.DirectIndex);
        Assert.Equal(4, directChannel.BoneIndex);
    }

    [Fact]
    public void AnimationChannel_RejectsNullTarget()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ImportedAnimationChannel(
                null!,
                ImportedAnimationChannelType.Quaternion,
                new[] { ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity) }));
    }

    [Fact]
    public void ImageSource_HoldsExactIdentity_And_RejectsInvalidInputs()
    {
        var path = ImportedImageSource.CreateVirtualPath("textures/helmet_base.png");
        Assert.Equal(ImportedImageSourceKind.VirtualPath, path.Kind);
        Assert.Equal("textures/helmet_base.png", path.VirtualPath);
        Assert.Throws<InvalidOperationException>(() => path.EmbeddedBytes);

        var rawBytes = new byte[] { 1, 2, 3 };
        var embedded = ImportedImageSource.CreateEmbeddedBytes(rawBytes, "image/png", "helmet.glb#bufferView-2");
        Assert.Equal(ImportedImageSourceKind.EmbeddedBytes, embedded.Kind);
        Assert.Equal("image/png", embedded.MimeType);
        Assert.Equal("helmet.glb#bufferView-2", embedded.DiagnosticName);
        Assert.Equal(new byte[] { 1, 2, 3 }, embedded.EmbeddedBytes.ToArray());
        rawBytes[0] = 99;
        Assert.Equal(1, embedded.EmbeddedBytes.Span[0]);
        Assert.Throws<InvalidOperationException>(() => embedded.VirtualPath);

        Assert.Throws<ArgumentException>(() => ImportedImageSource.CreateVirtualPath(""));
        Assert.Throws<ArgumentException>(() => ImportedImageSource.CreateVirtualPath("textures\\helmet.png"));
        Assert.Throws<ArgumentException>(() => ImportedImageSource.CreateVirtualPath("/textures/helmet.png"));
        Assert.Throws<ArgumentException>(() => ImportedImageSource.CreateVirtualPath("textures/../helmet.png"));
        Assert.Throws<ArgumentException>(() => ImportedImageSource.CreateEmbeddedBytes(Array.Empty<byte>(), "image/png", "name"));
        Assert.Throws<ArgumentException>(() => ImportedImageSource.CreateEmbeddedBytes(new byte[] { 1 }, "", "name"));
        Assert.Throws<ArgumentException>(() => ImportedImageSource.CreateEmbeddedBytes(new byte[] { 1 }, "image/png", ""));
    }

    [Fact]
    public void TextureBinding_HoldsValues_And_RejectsInvalidInputs()
    {
        var source = ImportedImageSource.CreateVirtualPath("textures/base.png");
        var sampler = new ImportedSampler(
            ImportedAddressMode.Clamp,
            ImportedAddressMode.MirroredRepeat,
            ImportedFilterMode.Nearest,
            ImportedFilterMode.Linear,
            ImportedFilterMode.Nearest);

        var binding = new ImportedTextureBinding(source, uvSet: 1, sampler, ImportedTextureRole.BaseColor);

        Assert.Same(source, binding.ImageSource);
        Assert.Equal(1, binding.UvSet);
        Assert.Same(sampler, binding.Sampler);
        Assert.Equal(ImportedTextureRole.BaseColor, binding.Role);

        Assert.Throws<ArgumentNullException>(() => new ImportedTextureBinding(null!, 0, sampler, ImportedTextureRole.BaseColor));
        Assert.Throws<ArgumentNullException>(() => new ImportedTextureBinding(source, 0, null!, ImportedTextureRole.BaseColor));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImportedTextureBinding(source, 2, sampler, ImportedTextureRole.BaseColor));
    }

    [Fact]
    public void PbrPayload_HasConstructorDefaults_And_RejectsRoleMismatches()
    {
        var payload = new ImportedPbrMetallicRoughness();

        Assert.Equal(Vector4.One, payload.BaseColorFactor);
        Assert.Null(payload.BaseColorTexture);
        Assert.Equal(1.0f, payload.MetallicFactor);
        Assert.Equal(1.0f, payload.RoughnessFactor);
        Assert.Null(payload.MetallicRoughnessTexture);
        Assert.Null(payload.NormalTexture);
        Assert.Equal(1.0f, payload.NormalScale);
        Assert.Null(payload.OcclusionTexture);
        Assert.Equal(1.0f, payload.OcclusionStrength);
        Assert.Equal(Vector3.Zero, payload.EmissiveFactor);
        Assert.Null(payload.EmissiveTexture);
        Assert.Equal(ImportedAlphaMode.Opaque, payload.AlphaMode);
        Assert.Equal(0.5f, payload.AlphaCutoff);
        Assert.False(payload.DoubleSided);

        var wrongRole = new ImportedTextureBinding(
            ImportedImageSource.CreateVirtualPath("textures/rough.png"),
            0,
            new ImportedSampler(),
            ImportedTextureRole.MetallicRoughness);

        Assert.Throws<ArgumentException>(() => new ImportedPbrMetallicRoughness(baseColorTexture: wrongRole));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImportedPbrMetallicRoughness(alphaCutoff: 1.5f));
    }

    [Fact]
    public void PbrMaterial_DerivesRenderStateFromPayload_And_FamilyAndPayloadAreInseparable()
    {
        var opaque = ImportedMaterial.CreatePbrMetallicRoughness(
            new ImportedPbrMetallicRoughness(doubleSided: false));
        Assert.Equal(ImportedMaterial.PbrMetallicRoughnessFamily, opaque.ShaderFamily);
        Assert.Equal(ImportedCullMode.Back, opaque.CullMode);
        Assert.True(opaque.DepthWriteEnabled);
        Assert.False(opaque.BlendEnabled);
        Assert.Null(opaque.FixedFunctionShading);
        Assert.NotNull(opaque.PbrMetallicRoughness);

        var blended = ImportedMaterial.CreatePbrMetallicRoughness(
            new ImportedPbrMetallicRoughness(alphaMode: ImportedAlphaMode.Blend, doubleSided: true));
        Assert.Equal(ImportedCullMode.None, blended.CullMode);
        Assert.False(blended.DepthWriteEnabled);
        Assert.True(blended.BlendEnabled);
        Assert.Equal(ImportedBlendFactor.SourceAlpha, blended.SourceBlendFactor);
        Assert.Equal(ImportedBlendFactor.InverseSourceAlpha, blended.DestinationColorBlendFactor);
        Assert.Equal(ImportedBlendFactor.InverseSourceAlpha, blended.DestinationAlphaBlendFactor);

        var masked = ImportedMaterial.CreatePbrMetallicRoughness(
            new ImportedPbrMetallicRoughness(alphaMode: ImportedAlphaMode.Mask, alphaCutoff: 0.25f));
        Assert.True(masked.DepthWriteEnabled);
        Assert.False(masked.BlendEnabled);

        Assert.Throws<ArgumentException>(() => new ImportedMaterial(
            ImportedMaterial.PbrMetallicRoughnessFamily,
            ImportedCullMode.Back,
            depthWriteEnabled: true,
            depthComparison: ImportedDepthComparison.LessEqual,
            blendEnabled: false,
            sourceBlendFactor: ImportedBlendFactor.One,
            destinationColorBlendFactor: ImportedBlendFactor.Zero,
            destinationAlphaBlendFactor: ImportedBlendFactor.Zero,
            fixedFunctionShading: null,
            properties: []));
    }

    [Fact]
    public void Material_RejectsPbrCombinedWithFixedFunction()
    {
        var ffShading = new ImportedFixedFunctionShading(
            ImportedDiffuseLightingType.Modulate,
            specularEnabled: false,
            texturingEnabled: true,
            secondaryTextureColorBlend: ImportedSecondaryTextureBlend.Disable,
            secondaryTextureAlphaBlend: ImportedSecondaryTextureBlend.Disable,
            alphaTest: false,
            ambient: Vector3.One,
            diffuse: Vector3.One,
            specular: Vector3.Zero,
            emissive: Vector3.Zero,
            shininess: 0.0f,
            opacity: 1.0f,
            textureStageCount: 1,
            CreateDefaultTextureMapping(),
            CreateDefaultTextureMapping());

        var payload = new ImportedPbrMetallicRoughness();
        var pbr = ImportedMaterial.CreatePbrMetallicRoughness(payload);
        Assert.Null(pbr.FixedFunctionShading);
        Assert.Same(payload, pbr.PbrMetallicRoughness);

        Assert.Throws<ArgumentException>(() => new ImportedMaterial(
            ImportedMaterial.PbrMetallicRoughnessFamily,
            ImportedCullMode.Back,
            depthWriteEnabled: true,
            depthComparison: ImportedDepthComparison.LessEqual,
            blendEnabled: false,
            sourceBlendFactor: ImportedBlendFactor.One,
            destinationColorBlendFactor: ImportedBlendFactor.Zero,
            destinationAlphaBlendFactor: ImportedBlendFactor.Zero,
            fixedFunctionShading: ffShading,
            properties: [],
            pbrMetallicRoughness: payload));

        Assert.Throws<ArgumentException>(() => new ImportedMaterial(
            "SomeOtherFamily",
            ImportedCullMode.Back,
            depthWriteEnabled: true,
            depthComparison: ImportedDepthComparison.LessEqual,
            blendEnabled: false,
            sourceBlendFactor: ImportedBlendFactor.One,
            destinationColorBlendFactor: ImportedBlendFactor.Zero,
            destinationAlphaBlendFactor: ImportedBlendFactor.Zero,
            fixedFunctionShading: null,
            properties: [],
            pbrMetallicRoughness: payload));
    }

    [Fact]
    public void AnimationChannel_DefaultsToLinearInterpolation()
    {
        var channel = new ImportedAnimationChannel(
            0,
            ImportedAnimationChannelType.TranslationX,
            new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 0) });

        Assert.Equal(ImportedAnimationInterpolation.Linear, channel.Interpolation);
    }

    [Fact]
    public void AnimationChannel_RetainsExplicitInterpolation()
    {
        var channel = new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateDirectIndex(0),
            ImportedAnimationChannelType.Quaternion,
            new[] { ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity) },
            ImportedAnimationInterpolation.Step);

        Assert.Equal(ImportedAnimationInterpolation.Step, channel.Interpolation);
    }

    [Fact]
    public void AnimationChannel_RejectsUndefinedInterpolation()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateDirectIndex(0),
            ImportedAnimationChannelType.TranslationX,
            new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 0) },
            (ImportedAnimationInterpolation)99));

        Assert.Equal("interpolation", exception.ParamName);
    }

    [Fact]
    public void MorphTarget_RejectsAllAbsentAndUnequalDeltaArrays()
    {
        Assert.Throws<ArgumentException>(() => new ImportedMorphTarget(null, null, null));

        Assert.Throws<ArgumentException>(() => new ImportedMorphTarget(
            new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero },
            new[] { Vector3.Zero }));

        var target = new ImportedMorphTarget(new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero });
        Assert.Null(target.NormalDeltas);
        Assert.Equal(3, target.PositionDeltas!.Count);
    }

    private static ImportedMorphTargetSet CreateMorphSet(int targetCount = 1, int vertexCount = 3)
    {
        var names = new string[targetCount];
        var weights = new float[targetCount];
        var ranges = new ImportedMorphWeightRange[targetCount];
        var targets = new ImportedMorphTarget[targetCount];
        for (var i = 0; i < targetCount; i++)
        {
            names[i] = $"Target{i + 1}";
            weights[i] = 0.0f;
            ranges[i] = new ImportedMorphWeightRange(0.0f, 1.0f);
            targets[i] = new ImportedMorphTarget(new Vector3[vertexCount]);
        }

        return new ImportedMorphTargetSet("TestModel.Mesh", names, weights, ranges, targets);
    }

    [Fact]
    public void MorphTargetSet_ValidatesTargets()
    {
        var set = CreateMorphSet(targetCount: 2);
        Assert.Equal("TestModel.Mesh", set.PrimitiveId);
        Assert.Equal(2, set.TargetNames.Count);
        Assert.Equal(0.0f, set.DefaultWeights[1]);
        Assert.Equal(1.0f, set.AllowedWeightRanges[0].Maximum);

        Assert.Throws<NotSupportedException>(() => CreateMorphSet(targetCount: ImportedMorphTargetSet.MaximumTargetCount + 1));

        var exception = Assert.Throws<ArgumentException>(() => new ImportedMorphTargetSet(
            "TestModel.Mesh",
            new[] { "Target1" },
            new float[] { 0.0f, 0.0f },
            new[] { new ImportedMorphWeightRange(0.0f, 1.0f) },
            new[] { new ImportedMorphTarget(new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero }) }));
        Assert.Equal("defaultWeights", exception.ParamName);

        Assert.Throws<ArgumentException>(() => new ImportedMorphTargetSet(
            "TestModel.Mesh",
            new[] { "Target1", "Target1" },
            new float[] { 0.0f, 0.0f },
            new[] { new ImportedMorphWeightRange(0.0f, 1.0f), new ImportedMorphWeightRange(0.0f, 1.0f) },
            new[]
            {
                new ImportedMorphTarget(new Vector3[3]),
                new ImportedMorphTarget(new Vector3[3]),
            }));

        Assert.Throws<ArgumentException>(() => new ImportedMorphTargetSet(
            "TestModel.Mesh",
            new[] { "Target1" },
            new[] { 1.5f },
            new[] { new ImportedMorphWeightRange(0.0f, 1.0f) },
            new[] { new ImportedMorphTarget(new Vector3[3]) }));

        Assert.Throws<ArgumentException>(() => new ImportedMorphTargetSet(
            "TestModel.Mesh",
            new[] { "Target1" },
            new[] { 0.5f },
            new[] { new ImportedMorphWeightRange(1.0f, 0.0f) },
            new[] { new ImportedMorphTarget(new Vector3[3]) }));
    }

    [Fact]
    public void Mesh_AttachesMorphTargetSet_AndRejectsMismatchedDeltasAndSecondSet()
    {
        var mesh = CreateMesh(
            new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) },
            new ushort[] { 0, 1, 2 },
            primitiveId: "TestModel.Mesh",
            morphTargetSets: new[] { CreateMorphSet() });
        Assert.Equal("TestModel.Mesh", mesh.PrimitiveId);
        Assert.Single(mesh.MorphTargetSets);

        Assert.Equal(string.Empty, CreateMesh(
            new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) },
            new ushort[] { 0, 1, 2 }).PrimitiveId);

        Assert.Throws<ArgumentException>(() => CreateMesh(
            new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) },
            new ushort[] { 0, 1, 2 },
            primitiveId: "TestModel.Mesh",
            morphTargetSets: new[]
            {
                new ImportedMorphTargetSet(
                    "TestModel.Mesh",
                    new[] { "Target1" },
                    new[] { 0.0f },
                    new[] { new ImportedMorphWeightRange(0.0f, 1.0f) },
                    new[] { new ImportedMorphTarget(new[] { Vector3.Zero, Vector3.Zero }) }),
            }));

        Assert.Throws<ArgumentException>(() => CreateMesh(
            new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) },
            new ushort[] { 0, 1, 2 },
            primitiveId: "TestModel.Mesh",
            morphTargetSets: new[] { CreateMorphSet(), CreateMorphSet() }));
    }

    [Fact]
    public void MorphAnimationChannel_ValidatesTimesVectorsAndWeights()
    {
        var channel = new ImportedMorphAnimationChannel(
            "TestModel.Mesh",
            new[] { "Target1", "Target2" },
            new[] { 0.0f, 1.0f },
            new[] { new[] { 0.0f, 0.0f }, new[] { 1.0f, 0.5f } });
        Assert.Equal(2, channel.TargetNames.Count);
        Assert.Equal(1.0f, channel.WeightVectors[1][0]);
        Assert.Equal(0.5f, channel.WeightVectors[1][1]);

        Assert.Throws<ArgumentException>(() => new ImportedMorphAnimationChannel(
            "TestModel.Mesh",
            new[] { "Target1" },
            new[] { 1.0f, 0.0f },
            new[] { new[] { 0.0f }, new[] { 1.0f } }));

        Assert.Throws<ArgumentException>(() => new ImportedMorphAnimationChannel(
            "TestModel.Mesh",
            new[] { "Target1" },
            new[] { 0.0f },
            new[] { new[] { 0.0f, 0.0f } }));

        Assert.Throws<ArgumentException>(() => new ImportedMorphAnimationChannel(
            "TestModel.Mesh",
            new[] { "Target1" },
            new[] { 0.0f },
            new[] { new[] { float.NaN } }));

        Assert.Throws<ArgumentException>(() => new ImportedMorphAnimationChannel(
            "TestModel.Mesh",
            new[] { "Target1", "Target1" },
            new[] { 0.0f },
            new[] { new[] { 0.0f, 0.0f } }));
    }

    [Fact]
    public void Animation_DefaultsToNoMorphChannels_AndAcceptsMorphChannels()
    {
        var animation = new ImportedAnimation(
            "Idle",
            TimeSpan.FromSeconds(1),
            new[]
            {
                new ImportedAnimationChannel(
                    0,
                    ImportedAnimationChannelType.TranslationX,
                    new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 0) }),
            });
        Assert.Empty(animation.MorphChannels);

        var morphChannel = new ImportedMorphAnimationChannel(
            "TestModel.Mesh",
            new[] { "Target1" },
            new[] { 0.0f, 1.0f },
            new[] { new[] { 0.0f }, new[] { 1.0f } });
        var withMorph = new ImportedAnimation(
            "Idle",
            TimeSpan.FromSeconds(1),
            Array.Empty<ImportedAnimationChannel>(),
            new[] { morphChannel });
        Assert.Same(morphChannel, withMorph.MorphChannels[0]);

        Assert.Throws<ArgumentException>(() => new ImportedAnimation(
            "Idle",
            TimeSpan.FromSeconds(1),
            Array.Empty<ImportedAnimationChannel>(),
            new ImportedMorphAnimationChannel?[] { null }));
    }

    [Fact]
    public void AssetImportContext_EnablesFeatureGatesByRequest()
    {
        using var fileSystem = new MemoryFileSystem();
        var plain = new AssetImportContext(fileSystem, null);
        Assert.False(plain.IsFeatureGateEnabled(ImportedFeatureGates.MorphTargets));

        var gated = new AssetImportContext(fileSystem, null, new[] { ImportedFeatureGates.MorphTargets });
        Assert.True(gated.IsFeatureGateEnabled(ImportedFeatureGates.MorphTargets));
        Assert.False(gated.IsFeatureGateEnabled("SomethingElse"));
    }

    [Fact]
    public void Materializer_RefusesMorphTargetSets_NamingTheGate()
    {
        if (!Panopticon.Graphics.GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var graphics = new TestGraphicsContext();
        var skeleton = new ImportedSkeleton(
            "TestSkeleton",
            new[] { new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity) });
        var model = new ImportedModel(
            "TestModel",
            skeleton,
            new[]
            {
                new ImportedSubObject(
                    "TestModel.Mesh",
                    "Mesh",
                    parentBoneIndex: 0,
                    renderObject: CreateMesh(
                        new[] { StaticVertex(0, 0, 0), StaticVertex(1, 0, 0), StaticVertex(0, 1, 0) },
                        new ushort[] { 0, 1, 2 },
                        primitiveId: "TestModel.Mesh",
                        morphTargetSets: new[] { CreateMorphSet() })),
            },
            ImportedSkeletonOrigin.Embedded);

        var exception = Assert.Throws<NotSupportedException>(() =>
            Panopticon.Content.Loaders.ImportedModelMaterializer.MaterializeModel(model, graphics.Context));
        Assert.Contains(ImportedFeatureGates.MorphTargets, exception.Message);
    }

}
