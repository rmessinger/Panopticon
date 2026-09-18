using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Loaders;
using OpenSage.Graphics;
using OpenSage.Graphics.Animation;
using Xunit;

namespace OpenSage.Tests.Graphics.Animation;

public sealed class ModelAnimationTests
{
    [Fact]
    public void Materialize_PreservesNameDurationAndClips()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        var imported = new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(2.0),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 3,
                    ImportedAnimationChannelType.Quaternion,
                    new[]
                    {
                        ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity),
                        ImportedKeyframe.FromQuaternion(TimeSpan.FromSeconds(0.2), rotation),
                    }),
                new ImportedAnimationChannel(
                    boneIndex: 1,
                    ImportedAnimationChannelType.TranslationX,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.Zero, 1.0f),
                    }),
                new ImportedAnimationChannel(
                    boneIndex: 2,
                    ImportedAnimationChannelType.Visibility,
                    new[]
                    {
                        ImportedKeyframe.FromBool(TimeSpan.Zero, true),
                    }),
            });

        var animation = ImportedAnimationMaterializer.Materialize(imported);

        Assert.Equal("HV.Run", animation.Name);
        Assert.Equal(TimeSpan.FromSeconds(2.0), animation.Duration);
        Assert.Equal(3, animation.Clips.Length);

        Assert.Equal(AnimationClipType.Quaternion, animation.Clips[0].ClipType);
        Assert.Equal(3, animation.Clips[0].Bone);
        Assert.Equal(rotation, animation.Clips[0].Keyframes[1].Value.Quaternion);

        Assert.Equal(AnimationClipType.TranslationX, animation.Clips[1].ClipType);
        Assert.Equal(1.0f, animation.Clips[1].Keyframes[0].Value.FloatValue);

        Assert.Equal(AnimationClipType.Visibility, animation.Clips[2].ClipType);
        Assert.True(animation.Clips[2].Keyframes[0].Value.BoolValue);
    }

    [Fact]
    public void Materialize_ProducesUnboundTargetIndexedChannels()
    {
        var imported = new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(2.0),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 3,
                    ImportedAnimationChannelType.Quaternion,
                    new[] { ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity) }),
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("Bip01"),
                    ImportedAnimationChannelType.TranslationX,
                    new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 1.0f) }),
            });

        var animation = ImportedAnimationMaterializer.Materialize(imported);

        Assert.Equal(2, animation.Channels.Length);
        Assert.Equal(ModelAnimationTargetKind.DirectIndex, animation.Channels[0].Target.Kind);
        Assert.Equal(3, animation.Channels[0].Target.DirectIndex);
        Assert.Equal(ModelAnimationTargetKind.BoneName, animation.Channels[1].Target.Kind);
        Assert.Equal("Bip01", animation.Channels[1].Target.BoneName);
    }

    [Fact]
    public void Bind_PreservesChannelOrderClipTypeTimingAndValues()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        var imported = new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(2.0),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 2,
                    ImportedAnimationChannelType.Quaternion,
                    new[]
                    {
                        ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity),
                        ImportedKeyframe.FromQuaternion(TimeSpan.FromSeconds(0.2), rotation),
                    }),
                new ImportedAnimationChannel(
                    boneIndex: 0,
                    ImportedAnimationChannelType.TranslationX,
                    new[]
                    {
                        ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(0.5), 1.0f),
                    }),
            });

        var animation = ImportedAnimationMaterializer.Materialize(imported);
        var hierarchy = new ModelBoneHierarchy(CreateSkeleton(3));

        var bound = animation.Bind(hierarchy);

        Assert.Equal("HV.Run", bound.Name);
        Assert.Equal(TimeSpan.FromSeconds(2.0), bound.Duration);
        Assert.Equal(2, bound.Clips.Length);

        Assert.Equal(AnimationClipType.Quaternion, bound.Clips[0].ClipType);
        Assert.Equal(2, bound.Clips[0].Bone);
        Assert.Equal(TimeSpan.Zero, bound.Clips[0].Keyframes[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(0.2), bound.Clips[0].Keyframes[1].Time);
        Assert.Equal(rotation, bound.Clips[0].Keyframes[1].Value.Quaternion);

        Assert.Equal(AnimationClipType.TranslationX, bound.Clips[1].ClipType);
        Assert.Equal(0, bound.Clips[1].Bone);
        Assert.Equal(TimeSpan.FromSeconds(0.5), bound.Clips[1].Keyframes[0].Time);
        Assert.Equal(1.0f, bound.Clips[1].Keyframes[0].Value.FloatValue);
    }

    [Fact]
    public void Bind_OmitsOutOfRangeDirectChannels_And_WarnsOncePerHierarchy()
    {
        var imported = new ImportedAnimation(
            "HV.Omit",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 0,
                    ImportedAnimationChannelType.TranslationX,
                    new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 1.0f) }),
                new ImportedAnimationChannel(
                    boneIndex: 99,
                    ImportedAnimationChannelType.Visibility,
                    new[] { ImportedKeyframe.FromBool(TimeSpan.Zero, true) }),
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("Bone_Arm"),
                    ImportedAnimationChannelType.TranslationY,
                    new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 2.0f) }),
            });

        var animation = ImportedAnimationMaterializer.Materialize(imported);
        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton("TargetSkeleton", new[]
        {
            new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity),
            new ImportedBone("Bone_Arm", 0, Vector3.Zero, Quaternion.Identity),
        }));

        using var logCapture = new NLogCapture();
        var bound = animation.Bind(hierarchy);
        animation.Bind(hierarchy);

        Assert.Equal(2, bound.Clips.Length);
        Assert.Equal(0, bound.Clips[0].Bone);
        Assert.Equal(AnimationClipType.TranslationX, bound.Clips[0].ClipType);
        Assert.Equal(1, bound.Clips[1].Bone);
        Assert.Equal(AnimationClipType.TranslationY, bound.Clips[1].ClipType);
        Assert.Equal(1, logCapture.CountWarningsContaining("HV.Omit"));
        Assert.Equal(1, logCapture.CountWarningsContaining("99"));
    }

    [Fact]
    public void Bind_CachesPerHierarchyObjectIdentity()
    {
        var animation = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 0,
                    ImportedAnimationChannelType.Visibility,
                    new[] { ImportedKeyframe.FromBool(TimeSpan.Zero, true) }),
            }));

        var firstHierarchy = CreateHierarchy("TargetSkeleton");
        var secondHierarchy = CreateHierarchy("TargetSkeleton");

        var firstBound = animation.Bind(firstHierarchy);
        var secondBound = animation.Bind(secondHierarchy);

        Assert.Same(firstBound, animation.Bind(firstHierarchy));
        Assert.Same(secondBound, animation.Bind(secondHierarchy));
        Assert.NotSame(firstBound, secondBound);
    }

    [Fact]
    public void Bind_CacheStorageIsWeak_And_BoundResultHoldsNoHierarchyReference()
    {
        var cacheField = typeof(ModelAnimation)
            .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => f.FieldType.IsGenericType &&
                f.FieldType.GetGenericTypeDefinition() == typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>));
        Assert.Equal(typeof(ModelBoneHierarchy), cacheField.FieldType.GetGenericArguments()[0]);
        Assert.Equal(typeof(BoundModelAnimation), cacheField.FieldType.GetGenericArguments()[1]);

        Assert.DoesNotContain(
            typeof(ModelAnimation).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
            f => f.FieldType == typeof(ModelBoneHierarchy));

        var boundFields = typeof(BoundModelAnimation)
            .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Select(f => f.FieldType);
        Assert.DoesNotContain(boundFields, t => t == typeof(ModelBoneHierarchy) || t == typeof(ModelBone));
    }

    private sealed class NLogCapture : IDisposable
    {
        private readonly NLog.Config.LoggingConfiguration? _previousConfiguration;
        private readonly NLog.Targets.MemoryTarget _memoryTarget = new("ModelAnimationTests");

        public NLogCapture()
        {
            _previousConfiguration = NLog.LogManager.Configuration;
            var configuration = new NLog.Config.LoggingConfiguration();
            configuration.AddRule(NLog.LogLevel.Warn, NLog.LogLevel.Fatal, _memoryTarget);
            NLog.LogManager.Configuration = configuration;
        }

        public int CountWarningsContaining(string substring)
        {
            return _memoryTarget.Logs.Count(l => l.Contains(substring));
        }

        public void Dispose()
        {
            NLog.LogManager.Configuration = _previousConfiguration;
        }
    }

    [Fact]
    public void Bind_RejectsNullHierarchy()
    {
        var animation = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    boneIndex: 0,
                    ImportedAnimationChannelType.Visibility,
                    new[] { ImportedKeyframe.FromBool(TimeSpan.Zero, true) }),
            }));

        Assert.Throws<ArgumentNullException>(() => animation.Bind(null!));
    }

    [Fact]
    public void Bind_NamedTargets_ResolveAgainstReorderedHierarchy_WithCaseInsensitiveMatch()
    {
        var animation = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("BONE_Leg"),
                    ImportedAnimationChannelType.TranslationX,
                    new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 1.0f) }),
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("root"),
                    ImportedAnimationChannelType.Quaternion,
                    new[] { ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity) }),
            }));

        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton("TargetSkeleton", new[]
        {
            new ImportedBone("ROOT", -1, Vector3.Zero, Quaternion.Identity),
            new ImportedBone("Bone_Arm", 0, Vector3.Zero, Quaternion.Identity),
            new ImportedBone("Bone_Leg", 0, Vector3.Zero, Quaternion.Identity),
        }));

        var bound = animation.Bind(hierarchy);

        Assert.Equal(2, bound.Clips.Length);
        Assert.Equal(2, bound.Clips[0].Bone);
        Assert.Equal(AnimationClipType.TranslationX, bound.Clips[0].ClipType);
        Assert.Equal(1.0f, bound.Clips[0].Keyframes[0].Value.FloatValue);
        Assert.Equal(0, bound.Clips[1].Bone);
        Assert.Equal(AnimationClipType.Quaternion, bound.Clips[1].ClipType);
    }

    [Fact]
    public void Bind_NamedTargets_MissingNameFailsWithContext()
    {
        var animation = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("Bone_Tail"),
                    ImportedAnimationChannelType.Visibility,
                    new[] { ImportedKeyframe.FromBool(TimeSpan.Zero, true) }),
            }));

        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton("TargetSkeleton", new[]
        {
            new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity),
        }));

        var exception = Assert.Throws<InvalidDataException>(() => animation.Bind(hierarchy));
        Assert.Contains("HV.Run", exception.Message);
        Assert.Contains("Bone_Tail", exception.Message);
        Assert.Contains("TargetSkeleton", exception.Message);
    }

    [Fact]
    public void Bind_NamedTargets_DuplicateHierarchyNamesFailBeforeResolution()
    {
        var animation = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("Bone_Leg"),
                    ImportedAnimationChannelType.Visibility,
                    new[] { ImportedKeyframe.FromBool(TimeSpan.Zero, true) }),
            }));

        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton("TargetSkeleton", new[]
        {
            new ImportedBone("Bone_Leg", -1, Vector3.Zero, Quaternion.Identity),
            new ImportedBone("bone_leg", 0, Vector3.Zero, Quaternion.Identity),
        }));

        var exception = Assert.Throws<InvalidDataException>(() => animation.Bind(hierarchy));
        Assert.Contains("TargetSkeleton", exception.Message);
        Assert.Contains("Bone_Leg", exception.Message);
    }

    [Fact]
    public void Bind_NamedTargets_UnrelatedAndUnnamedBonesRequireNoMapping()
    {
        var animation = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("Bone_Leg"),
                    ImportedAnimationChannelType.Visibility,
                    new[] { ImportedKeyframe.FromBool(TimeSpan.Zero, true) }),
            }));

        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton("TargetSkeleton", new[]
        {
            new ImportedBone("Bone_Leg", -1, Vector3.Zero, Quaternion.Identity),
            new ImportedBone("Bone_Unrelated", 0, Vector3.Zero, Quaternion.Identity),
        }));

        var bound = animation.Bind(hierarchy);

        Assert.Single(bound.Clips);
        Assert.Equal(0, bound.Clips[0].Bone);
    }

    [Fact]
    public void Bind_MixedTargets_PreservesChannelOrder()
    {
        var animation = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "HV.Run",
            TimeSpan.FromSeconds(1.0),
            new[]
            {
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("Bone_Arm"),
                    ImportedAnimationChannelType.TranslationY,
                    new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 2.0f) }),
                new ImportedAnimationChannel(
                    boneIndex: 1,
                    ImportedAnimationChannelType.Visibility,
                    new[] { ImportedKeyframe.FromBool(TimeSpan.Zero, true) }),
                new ImportedAnimationChannel(
                    ImportedAnimationTarget.CreateBoneName("Bone_Leg"),
                    ImportedAnimationChannelType.TranslationZ,
                    new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 3.0f) }),
            }));

        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton("TargetSkeleton", new[]
        {
            new ImportedBone("Bone_Leg", -1, Vector3.Zero, Quaternion.Identity),
            new ImportedBone("Bone_Arm", 0, Vector3.Zero, Quaternion.Identity),
            new ImportedBone("Bone_Torso", 0, Vector3.Zero, Quaternion.Identity),
        }));

        var bound = animation.Bind(hierarchy);

        Assert.Equal(3, bound.Clips.Length);
        Assert.Equal(1, bound.Clips[0].Bone);
        Assert.Equal(AnimationClipType.TranslationY, bound.Clips[0].ClipType);
        Assert.Equal(1, bound.Clips[1].Bone);
        Assert.Equal(AnimationClipType.Visibility, bound.Clips[1].ClipType);
        Assert.Equal(0, bound.Clips[2].Bone);
        Assert.Equal(AnimationClipType.TranslationZ, bound.Clips[2].ClipType);
    }
    private static ImportedSkeleton CreateSkeleton(int boneCount)
    {
        var bones = new ImportedBone[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            var parentIndex = i == 0 ? -1 : i - 1;
            bones[i] = new ImportedBone($"Bone{i}", parentIndex, Vector3.Zero, Quaternion.Identity);
        }

        return new ImportedSkeleton("TestSkeleton", bones);
    }

    private static ModelBoneHierarchy CreateHierarchy(string name)
    {
        return new ModelBoneHierarchy(new ImportedSkeleton(name, new[]
        {
            new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity),
        }));
    }
}
