using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Tests.Content.Importing;
using Panopticon.Utilities;
using Xunit;

namespace Panopticon.Tests.Graphics.Animation;

public sealed class ModelPropertyAnimationTests
{
    [Fact]
    public void SubObjectVisibilityChannel_StepsIntoStateAndResetsOnReplay()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var instance = CreateModelInstance(context);

        var animation = Materialize(new ImportedAnimation(
            "market",
            TimeSpan.FromSeconds(1),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateSubObjectName("Mesh"),
                    ImportedAnimationChannelType.Visibility,
                    new[]
                    {
                        ImportedKeyframe.FromBool(TimeSpan.Zero, true),
                        ImportedKeyframe.FromBool(TimeSpan.FromSeconds(0.5), false),
                    },
                    ImportedAnimationInterpolation.Step),
            }));

        PlayTo(instance, animation, 0.25f);
        Assert.True(instance.PropertyState.IsSubObjectVisible(0));

        // A second animation instance on the same model resets the state, then steps to hidden.
        PlayTo(instance, animation, 0.75f);
        Assert.False(instance.PropertyState.IsSubObjectVisible(0));
    }

    [Fact]
    public void MaterialSlotChannels_SampleLinearFloatAndVector3Values()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var instance = CreateModelInstance(context);

        var animation = Materialize(new ImportedAnimation(
            "props",
            TimeSpan.FromSeconds(1),
            new ImportedAnimationChannel[]
            {
                new(
                    ImportedAnimationTarget.CreateMaterialSlot("Mesh", 0),
                    ImportedAnimationChannelType.MaterialAlpha,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.Zero, 1.0f),
                        ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(1), 0.25f),
                    }),
                new(
                    ImportedAnimationTarget.CreateMaterialSlot("Mesh", 0),
                    ImportedAnimationChannelType.MaterialAmbientColor,
                    new[]
                    {
                        ImportedKeyframe.FromVector3(TimeSpan.Zero, Vector3.One),
                        ImportedKeyframe.FromVector3(TimeSpan.FromSeconds(1), Vector3.Zero),
                    }),
                new(
                    ImportedAnimationTarget.CreateMaterialSlot("TestModel.Mesh", 0),
                    ImportedAnimationChannelType.TextureTranslateU,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.Zero, 0f),
                        ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(1), 1.5f),
                    },
                    ImportedAnimationInterpolation.Step),
            }));

        Assert.False(instance.PropertyState.TryGetAlpha(0, 0, out var defaultAlpha));
        Assert.Equal(1.0f, defaultAlpha);

        PlayTo(instance, animation, 0.5f);

        Assert.True(instance.PropertyState.TryGetAlpha(0, 0, out var alpha));
        Assert.Equal(0.625f, alpha, 5);

        ref var slot = ref instance.PropertyState.GetSlot(0, 0);
        Assert.Equal(new Vector3(0.5f, 0.5f, 0.5f), slot.Ambient);
        Assert.True((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.Ambient) != 0);

        // Step-interpolated UV holds the previous key: at t=0.5 the translate is still 0,
        // though the channel is marked active.
        Assert.Equal(0f, slot.UvTranslateU);
        Assert.True((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.UvTranslate) != 0);
    }

    [Fact]
    public void PlayOfBoneOnlyAnimation_ResetsPropertyStateToDefaults()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var instance = CreateModelInstance(context);

        var propertyAnimation = Materialize(new ImportedAnimation(
            "alpha",
            TimeSpan.FromSeconds(1),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateMaterialSlot("Mesh", 0),
                    ImportedAnimationChannelType.MaterialAlpha,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.Zero, 0.5f),
                        ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(1), 0.5f),
                    }),
            }));
        PlayTo(instance, propertyAnimation, 0.5f);
        Assert.True(instance.PropertyState.TryGetAlpha(0, 0, out var sampled));
        Assert.Equal(0.5f, sampled, 5);

        var boneAnimation = Materialize(new ImportedAnimation(
            "idle",
            TimeSpan.FromSeconds(1),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 0,
                    ImportedAnimationChannelType.Quaternion,
                    new[] { ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity) }),
            }));
        PlayTo(instance, boneAnimation, 0.5f);

        Assert.False(instance.PropertyState.TryGetAlpha(0, 0, out var resetAlpha));
        Assert.Equal(1.0f, resetAlpha);
        Assert.True(instance.PropertyState.IsSubObjectVisible(0));
    }

    [Fact]
    public void TwoInstancesOfTheSameModel_HoldIndependentSamples()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var model = ImportedModelMaterializer.MaterializeModel(CreateImportedModel(), context.Context);
        var first = new ModelInstance(model, context.Context);
        var second = new ModelInstance(model, context.Context);
        first.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));
        second.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

        var animation = Materialize(new ImportedAnimation(
            "alpha",
            TimeSpan.FromSeconds(1),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateMaterialSlot("Mesh", 0),
                    ImportedAnimationChannelType.MaterialAlpha,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.Zero, 0.25f),
                        ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(1), 0.75f),
                    }),
            }));

        PlayTo(first, animation, 0.0f);
        PlayTo(second, animation, 1.0f);

        Assert.True(first.PropertyState.TryGetAlpha(0, 0, out var firstAlpha));
        Assert.True(second.PropertyState.TryGetAlpha(0, 0, out var secondAlpha));
        Assert.Equal(0.25f, firstAlpha, 5);
        Assert.Equal(0.75f, secondAlpha, 5);
    }

    [Fact]
    public void MissingSubObjectTarget_FailsModelBinding()
    {
        var model = CreateStubModel("TestModel", "TestModel.Mesh");
        var animation = CreateSubObjectAnimation("nope");

        var ex = Assert.Throws<InvalidDataException>(() => animation.Bind(model));

        Assert.Contains("targets sub-object 'nope'", ex.Message);
        Assert.Contains("does not exist in model 'TestModel'", ex.Message);
    }

    [Fact]
    public void AmbiguousSubObjectShortName_FailsModelBinding()
    {
        var model = CreateStubModel("Alpha.wing", "Beta.wing");
        var animation = CreateSubObjectAnimation("wing");

        var ex = Assert.Throws<InvalidDataException>(() => animation.Bind(model));

        Assert.Contains("is ambiguous", ex.Message);
        Assert.Contains("'Alpha.wing'", ex.Message);
        Assert.Contains("'Beta.wing'", ex.Message);
    }

    [Fact]
    public void MaterialSlotWithoutMesh_FailsModelBinding()
    {
        var model = CreateStubModel("TestModel", "TestModel.Mesh");
        var animation = Materialize(new ImportedAnimation(
            "anim",
            TimeSpan.FromSeconds(1),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateMaterialSlot("Mesh", 0),
                    ImportedAnimationChannelType.MaterialAlpha,
                    new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 1.0f) }),
            }));

        var ex = Assert.Throws<InvalidDataException>(() => animation.Bind(model));

        Assert.Contains("material slot 0", ex.Message);
        Assert.Contains("has 0 materials", ex.Message);
    }

    [Fact]
    public void BoneOnlyAnimation_BindsIdenticallyThroughModelAndHierarchy()
    {
        var model = CreateStubModel("TestModel", "TestModel.Mesh");
        var animation = Materialize(new ImportedAnimation(
            "idle",
            TimeSpan.FromSeconds(1),
            new ImportedAnimationChannel[]
            {
                new(
                    boneIndex: 0,
                    ImportedAnimationChannelType.Quaternion,
                    new[] { ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity) }),
                new(
                    ImportedAnimationTarget.CreateBoneName("Root"),
                    ImportedAnimationChannelType.TranslationX,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.Zero, 0f),
                        ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(1), 1f),
                    }),
            }));

        var byHierarchy = animation.Bind(model.BoneHierarchy);
        var byModel = animation.Bind(model);

        Assert.Equal(byHierarchy.Clips.Length, byModel.Clips.Length);
        for (var i = 0; i < byHierarchy.Clips.Length; i++)
        {
            Assert.Equal(byHierarchy.Clips[i].ClipType, byModel.Clips[i].ClipType);
            Assert.Equal(byHierarchy.Clips[i].Bone, byModel.Clips[i].Bone);
            Assert.Equal(byHierarchy.Clips[i].Keyframes.Length, byModel.Clips[i].Keyframes.Length);
        }
    }

    private static ModelAnimation Materialize(ImportedAnimation imported)
    {
        return ImportedAnimationMaterializer.Materialize(imported);
    }

    private static ModelAnimation CreateSubObjectAnimation(string name)
    {
        return Materialize(new ImportedAnimation(
            "anim",
            TimeSpan.FromSeconds(1),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateSubObjectName(name),
                    ImportedAnimationChannelType.Visibility,
                    new[] { ImportedKeyframe.FromBool(TimeSpan.Zero, false) },
                    ImportedAnimationInterpolation.Step),
            }));
    }

    private static ModelInstance CreateModelInstance(TestGraphicsContext context)
    {
        var model = ImportedModelMaterializer.MaterializeModel(CreateImportedModel(), context.Context);
        var instance = new ModelInstance(model, context.Context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));
        return instance;
    }

    private static ImportedModel CreateImportedModel()
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
                    renderObject: CreateMesh()),
            },
            ImportedSkeletonOrigin.Embedded);
    }

    private static ImportedMesh CreateMesh()
    {
        var material = new ImportedMaterial(
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
                texturingEnabled: false,
                secondaryTextureColorBlend: ImportedSecondaryTextureBlend.Disable,
                secondaryTextureAlphaBlend: ImportedSecondaryTextureBlend.Disable,
                alphaTest: false,
                ambient: Vector3.One,
                diffuse: Vector3.One,
                specular: Vector3.Zero,
                emissive: Vector3.Zero,
                shininess: 0.0f,
                opacity: 1.0f,
                textureStageCount: 0,
                CreateDefaultTextureMapping(),
                CreateDefaultTextureMapping()),
            properties: Array.Empty<ImportedMaterialProperty>());

        return new ImportedMesh(
            new[]
            {
                new ImportedVertex(new Vector3(0, 0, 0), Vector3.UnitZ),
                new ImportedVertex(new Vector3(1, 0, 0), Vector3.UnitZ),
                new ImportedVertex(new Vector3(0, 1, 0), Vector3.UnitZ),
            },
            new ushort[] { 0, 1, 2 },
            new AxisAlignedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)),
            new BoundingSphere(Vector3.Zero, 1.0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: new[] { material },
            meshParts: new[]
            {
                new ImportedMeshPart(
                    new Vector2[] { new(0, 0), new(1, 0), new(0, 1) },
                    null,
                    startIndex: 0,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 0),
            });
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

    private static Model CreateStubModel(params string[] subObjectNames)
    {
        var hierarchy = new ModelBoneHierarchy(
            new ImportedSkeleton(
                "TestSkeleton",
                new[] { new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity) }));

        var subObjects = new ModelSubObject[subObjectNames.Length];
        for (var i = 0; i < subObjectNames.Length; i++)
        {
            var fullName = subObjectNames[i];
            var shortName = fullName.Substring(fullName.IndexOf('.') + 1);
            subObjects[i] = new ModelSubObject(fullName, hierarchy.Bones[0], new StubRenderObject(shortName));
        }

        return new Model("TestModel", hierarchy, subObjects);
    }

    private static void PlayTo(ModelInstance instance, ModelAnimation animation, float seconds)
    {
        var animationInstance = instance.CreateAnimationInstance(
            animation,
            AnimationMode.Once,
            AnimationFlags.StartFrameFirst,
            gameObject: null!,
            random: new ConstantRandom());

        animationInstance.Play();

        const int steps = 8;
        var delta = TimeSpan.FromSeconds(seconds / steps);
        for (var i = 0; i < steps; i++)
        {
            animationInstance.Update(new TimeInterval(TimeSpan.Zero, delta));
        }

        instance.Update(new TimeInterval(TimeSpan.FromSeconds(seconds), delta));
    }

    private sealed class ConstantRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int minValue, int maxValue)
        {
            return minValue;
        }

        public float NextSingle(float minValue, float maxValue)
        {
            return minValue;
        }
    }

    private sealed class StubRenderObject : ModelRenderObject
    {
        private readonly AxisAlignedBoundingBox _boundingBox = new(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        private readonly BoundingSphere _boundingSphere = new(Vector3.Zero, 1.0f);

        public StubRenderObject(string name)
        {
            SetNameAndInstanceId("StubRenderObject", name);
        }

        public override ref readonly AxisAlignedBoundingBox BoundingBox => ref _boundingBox;

        public override ref readonly BoundingSphere BoundingSphere => ref _boundingSphere;

        public override bool Hidden => false;

        internal override void BuildRenderList(
            RenderList renderList,
            Camera camera,
            ModelInstance modelInstance,
            ModelMeshInstance modelMeshInstance,
            ModelBone parentBone,
            in Matrix4x4 modelTransform,
            bool castsShadow,
            MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS)
        {
        }

        internal override void BuildRenderListWithWorldMatrix(
            RenderList renderList,
            Camera camera,
            ModelInstance modelInstance,
            ModelMeshInstance modelMeshInstance,
            ModelBone parentBone,
            in Matrix4x4 meshWorldMatrix,
            bool castsShadow,
            MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS = null)
        {
        }
    }
}
