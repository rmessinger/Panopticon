using System;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class ImportedAnimationTests
{
    private static ImportedKeyframe ColorKey(float seconds, float x, float y, float z)
    {
        return ImportedKeyframe.FromVector3(TimeSpan.FromSeconds(seconds), new Vector3(x, y, z));
    }

    private static ImportedKeyframe FloatKey(float seconds, float value)
    {
        return ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(seconds), value);
    }

    private static ImportedKeyframe BoolKey(float seconds, bool value)
    {
        return ImportedKeyframe.FromBool(TimeSpan.FromSeconds(seconds), value);
    }

    [Fact]
    public void MaterialSlotColorChannel_MaterializesWithVector3Keys()
    {
        var channel = new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateMaterialSlot("test_model.0.hull", 0),
            ImportedAnimationChannelType.MaterialAmbientColor,
            new[] { ColorKey(0, 0.1f, 0.2f, 0.3f), ColorKey(1, 0.4f, 0.5f, 0.6f) });

        var animation = new ImportedAnimation("ambient", TimeSpan.FromSeconds(1), new[] { channel });
        var runtime = ImportedAnimationMaterializer.Materialize(animation);

        var runtimeChannel = Assert.Single(runtime.Channels);
        Assert.Equal(ModelAnimationTargetKind.MaterialSlot, runtimeChannel.Target.Kind);
        Assert.Equal("test_model.0.hull", runtimeChannel.Target.SubObjectName);
        Assert.Equal(0, runtimeChannel.Target.MaterialIndex);
        Assert.Equal(AnimationClipType.MaterialAmbientColor, runtimeChannel.ClipType);
        Assert.Equal(2, runtimeChannel.Keyframes.Length);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), runtimeChannel.Keyframes[0].Value.Vector3Value);
        Assert.Equal(new Vector3(0.4f, 0.5f, 0.6f), runtimeChannel.Keyframes[1].Value.Vector3Value);
    }

    [Fact]
    public void SubObjectVisibilityChannel_MaterializesWithStepInterpolation()
    {
        var channel = new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateSubObjectName("market_open_1"),
            ImportedAnimationChannelType.Visibility,
            new[] { BoolKey(0, false), BoolKey(0.033333f, true) },
            ImportedAnimationInterpolation.Step);

        var animation = new ImportedAnimation("open", TimeSpan.FromSeconds(1), new[] { channel });
        var runtime = ImportedAnimationMaterializer.Materialize(animation);

        var runtimeChannel = Assert.Single(runtime.Channels);
        Assert.Equal(ModelAnimationTargetKind.SubObjectName, runtimeChannel.Target.Kind);
        Assert.Equal("market_open_1", runtimeChannel.Target.SubObjectName);
        Assert.Equal(AnimationClipType.Visibility, runtimeChannel.ClipType);
        Assert.Equal(AnimationInterpolation.Step, runtimeChannel.Interpolation);
        Assert.False(runtimeChannel.Keyframes[0].Value.BoolValue);
        Assert.True(runtimeChannel.Keyframes[1].Value.BoolValue);
    }

    [Theory]
    [InlineData(ImportedAnimationChannelType.TextureTranslateU)]
    [InlineData(ImportedAnimationChannelType.TextureTranslateV)]
    [InlineData(ImportedAnimationChannelType.TextureScaleU)]
    [InlineData(ImportedAnimationChannelType.TextureScaleV)]
    [InlineData(ImportedAnimationChannelType.TextureRotate)]
    [InlineData(ImportedAnimationChannelType.MaterialAlpha)]
    public void UVAndAlphaChannels_RequireFloatKeys(ImportedAnimationChannelType channelType)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateMaterialSlot("test_model.0.hull", 0),
            channelType,
            new[] { BoolKey(0, true) }));

        Assert.Contains($"channel type {channelType} requires Float", ex.Message);
    }

    [Theory]
    [InlineData(ImportedAnimationChannelType.MaterialAmbientColor)]
    [InlineData(ImportedAnimationChannelType.MaterialDiffuseColor)]
    [InlineData(ImportedAnimationChannelType.MaterialSpecularColor)]
    [InlineData(ImportedAnimationChannelType.MaterialEmissiveColor)]
    public void MaterialColorChannels_RequireVector3Keys(ImportedAnimationChannelType channelType)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateMaterialSlot("test_model.0.hull", 0),
            channelType,
            new[] { FloatKey(0, 1) }));

        Assert.Contains($"channel type {channelType} requires Vector3", ex.Message);
    }

    [Fact]
    public void DirectIndexIsRejectedForPropertyChannels()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ImportedAnimationChannel(
            boneIndex: 0,
            ImportedAnimationChannelType.MaterialAlpha,
            new[] { FloatKey(0, 1) }));

        Assert.Contains("Direct-index targets address bones", ex.Message);
    }

    [Fact]
    public void MaterialSlotIsRejectedForBoneChannels()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateMaterialSlot("test_model.0.hull", 0),
            ImportedAnimationChannelType.TranslationX,
            new[] { FloatKey(0, 1) }));

        Assert.Contains("Material-slot targets address UV and material color channels", ex.Message);
    }

    [Fact]
    public void SubObjectNameIsRejectedForMaterialChannels()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateSubObjectName("hull"),
            ImportedAnimationChannelType.MaterialAlpha,
            new[] { FloatKey(0, 1) }));

        Assert.Contains("Sub-object targets address visibility", ex.Message);
    }

    [Fact]
    public void MixedAnimation_MaterializesBoneChannelsUnchanged()
    {
        var boneChannel = new ImportedAnimationChannel(
            boneIndex: 0,
            ImportedAnimationChannelType.Quaternion,
            new[]
            {
                ImportedKeyframe.FromQuaternion(TimeSpan.Zero, Quaternion.Identity),
                ImportedKeyframe.FromQuaternion(
                    TimeSpan.FromSeconds(1),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f)),
            });
        var materialChannel = new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateMaterialSlot("test_model.0.hull", 0),
            ImportedAnimationChannelType.MaterialAlpha,
            new[] { FloatKey(0, 1), FloatKey(1, 0.25f) });
        var animation = new ImportedAnimation(
            "mixed", TimeSpan.FromSeconds(1), new ImportedAnimationChannel[] { boneChannel, materialChannel });

        var runtime = ImportedAnimationMaterializer.Materialize(animation);
        var boneOnly = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "bone_only", TimeSpan.FromSeconds(1), new[] { boneChannel }));

        Assert.Equal(2, runtime.Channels.Length);
        Assert.Equal(boneOnly.Channels[0].Target.Kind, runtime.Channels[0].Target.Kind);
        Assert.Equal(boneOnly.Channels[0].Target.DirectIndex, runtime.Channels[0].Target.DirectIndex);
        Assert.Equal(boneOnly.Channels[0].ClipType, runtime.Channels[0].ClipType);
        Assert.Equal(boneOnly.Channels[0].Keyframes.Length, runtime.Channels[0].Keyframes.Length);
        for (var i = 0; i < boneOnly.Channels[0].Keyframes.Length; i++)
        {
            Assert.Equal(boneOnly.Channels[0].Keyframes[i].Time, runtime.Channels[0].Keyframes[i].Time);
            Assert.Equal(
                boneOnly.Channels[0].Keyframes[i].Value.Quaternion,
                runtime.Channels[0].Keyframes[i].Value.Quaternion);
        }
    }

    [Fact]
    public void HierarchyBinding_RejectsPropertyChannels()
    {
        var channel = new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateSubObjectName("market_open_1"),
            ImportedAnimationChannelType.Visibility,
            new[] { BoolKey(0, false), BoolKey(1, true) });
        var animation = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "open", TimeSpan.FromSeconds(1), new[] { channel }));
        var hierarchy = new ModelBoneHierarchy(new ImportedSkeleton(
            "test_model",
            new[] { new ImportedBone("root", -1, Vector3.Zero, Quaternion.Identity) }));

        var ex = Assert.Throws<NotSupportedException>(() => animation.Bind(hierarchy));

        Assert.Contains("must bind against a model", ex.Message);
    }

    [Fact]
    public void MaterialSlotTarget_ValidatesArguments()
    {
        Assert.Throws<ArgumentException>(() => ImportedAnimationTarget.CreateMaterialSlot("", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImportedAnimationTarget.CreateMaterialSlot("test_model.0.hull", -1));
        Assert.Throws<ArgumentException>(() => ImportedAnimationTarget.CreateSubObjectName(""));
    }

    [Fact]
    public void ScaleChannel_MaterializesWithFloatKeys()
    {
        var channel = new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateBoneName("gear"),
            ImportedAnimationChannelType.Scale,
            new[] { ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(0), 0.5f),
                ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(1), 1.0f) });

        var runtime = ImportedAnimationMaterializer.Materialize(new ImportedAnimation(
            "extend", TimeSpan.FromSeconds(1), new[] { channel }));

        var runtimeChannel = Assert.Single(runtime.Channels);
        Assert.Equal(AnimationClipType.Scale, runtimeChannel.ClipType);
        Assert.Equal(ModelAnimationTargetKind.BoneName, runtimeChannel.Target.Kind);
        Assert.Equal(0.5f, runtimeChannel.Keyframes[0].Value.FloatValue);
        Assert.Equal(1.0f, runtimeChannel.Keyframes[1].Value.FloatValue);
    }

    [Fact]
    public void ScaleChannel_RejectsQuaternionKeys()
    {
        Assert.Throws<ArgumentException>(() => new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateBoneName("gear"),
            ImportedAnimationChannelType.Scale,
            new[] { ImportedKeyframe.FromQuaternion(TimeSpan.FromSeconds(0), Quaternion.Identity) }));
    }
}
