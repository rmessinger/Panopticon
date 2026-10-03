using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.IO;
using Panopticon.Tests.Data.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;

namespace Panopticon.Tests.Content.Importing;

public sealed class NifAnimationImporterTests
{
    private const string ModelName = "test_model";
    private const string KfPath = "anims/clip.kf";
    private const string AnimationKey = ModelName + "#kf=" + KfPath;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(ModelName)]
    [InlineData(ModelName + "#kf=")]
    [InlineData("#kf=" + KfPath)]
    [InlineData(ModelName + "#anim=" + KfPath)]
    [InlineData(ModelName + "#kf=a#kf=b")]
    [InlineData(ModelName + "#kF=" + KfPath)]
    [InlineData(ModelName + "#kf=" + KfPath + "?x")]
    public void MalformedKeysReturnNull(string key)
    {
        Assert.Null(Import(QuadraticFreeKf(), key: key));
    }

    [Fact]
    public void MissingModelEntryReturnsNull()
    {
        Assert.Null(Import(QuadraticFreeKf(), omitModel: true));
    }

    [Fact]
    public void MissingKfEntryReturnsNull()
    {
        Assert.Null(Import(null, omitKf: true));
    }

    [Fact]
    public void BlockZeroNotASequenceThrowsWithAssetContext()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root"));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(
            () => Import(builder.Build()));

        Assert.Contains(AnimationKey, ex.Message);
        Assert.Contains("block 0 is not a controller sequence", ex.Message);
    }

    [Fact]
    public void QuaternionChannelOffsetsReconstructAuthoredSamples()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys:
        [
            (0.0f, 1.0f, 0.0f, 0.0f, 0.0f),
            (1.0f, 0.70710678f, 0.0f, 0.70710678f, 0.0f),
        ]);

        var animation = Import(SingleChannelKf(data, duration: 2.1667f))!;

        Assert.Equal("clip", animation.Name);
        Assert.Equal(TimeSpan.FromSeconds(2.1667f), animation.Duration);

        var channel = Assert.Single(animation.Channels);
        Assert.Equal(ImportedAnimationChannelType.Quaternion, channel.ChannelType);
        Assert.Equal(ImportedAnimationInterpolation.Linear, channel.Interpolation);
        Assert.Equal("child", channel.Target.BoneName);
        Assert.Equal(2, channel.Keyframes.Count);

        var bone = GetBone("child");
        var samples = new[]
        {
            Quaternion.Identity,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
        };
        for (var i = 0; i < samples.Length; i++)
        {
            var expected = NifTransformConverter.ConjugateSourceRotationToEngine(samples[i]);
            var reconstructed = Quaternion.Concatenate(
                channel.Keyframes[i].QuaternionValue,
                bone.BindRotation);
            AssertEqual(expected, reconstructed, $"rotation key {i}");
            Assert.Equal(i == 0 ? 0.0 : 1.0, channel.Keyframes[i].Time.TotalSeconds, 5);
        }
    }

    [Fact]
    public void QuaternionKeyEqualToNonSymmetricRestProducesIdentityOffset()
    {
        // The deep-dive section 9 regression proof: a quaternion key equal
        // to a non-symmetric stored rest yields an identity offset at time
        // 0. Under the pre-C4d positional copy the key and the rest lived
        // in transposed spaces and the offset carried twice the rest
        // rotation.
        var file = FileFormats.Nif.NifFile.Read(new MemoryStream(ModelBytes()));
        var local = NifTransformConverter.CreateLocalMatrix((FileFormats.Nif.NifAvObject)file.Blocks[1]);
        var rest = Quaternion.CreateFromRotationMatrix(local);

        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys:
            [(0.0f, rest.W, rest.X, rest.Y, rest.Z)]);

        var animation = Import(SingleChannelKf(data, duration: 2.0f))!;

        var channel = Assert.Single(animation.Channels);
        var offset = channel.Keyframes[0].QuaternionValue;
        Assert.True(
            MathF.Abs(offset.X) < 1e-4f &&
            MathF.Abs(offset.Y) < 1e-4f &&
            MathF.Abs(offset.Z) < 1e-4f,
            $"Expected an identity rotation offset, got {offset}.");
    }

    [Fact]
    public void XyzRotationChannelUsesSamplerAndReconstructsSamples()
    {
        // A 15-degree Y spin over half a second: the k/30 grid plus the
        // two key times collapse to 16 samples at 1/30 s steps.
        var data = NifTestKeyframe.KeyframeData(
            xyzRotationOrder: 0.0f,
            yRotationKeys:
            [
                (0.0f, 0.0f, null, null),
                (0.5f, MathF.PI / 12f, null, null),
            ]);

        var animation = Import(SingleChannelKf(data, duration: 0.5f))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal(ImportedAnimationChannelType.Quaternion, channel.ChannelType);
        Assert.Equal(16, channel.Keyframes.Count);

        var bone = GetBone("child");
        for (var i = 0; i < channel.Keyframes.Count; i++)
        {
            Assert.Equal(i / 30.0, channel.Keyframes[i].Time.TotalSeconds, 6);

            // The stored source matrix Rz*Ry*Rx is the column-vector map,
            // transposed into the engine's row-vector layout, so +y Euler
            // keys spin by +angle about the engine Y axis.
            var angle = MathF.PI / 12f * (i / 30f) / 0.5f;
            var expected = NifTransformConverter.ConjugateSourceRotationToEngine(
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
            var reconstructed = Quaternion.Concatenate(
                channel.Keyframes[i].QuaternionValue,
                bone.BindRotation);
            AssertEqual(expected, reconstructed, $"xyz sample {i}");
        }
    }

    [Fact]
    public void TranslationChannelsReconstructAuthoredSamples()
    {
        var data = NifTestKeyframe.KeyframeData(translationKeys:
        [
            (0.0f, (0.0f, 0.0f, 0.0f), null, null),
            (2.0f, (1.0f, 2.0f, 3.0f), null, null),
        ]);

        var animation = Import(SingleChannelKf(data, duration: 2.0f))!;

        Assert.Equal(3, animation.Channels.Count);
        var bone = GetBone("child");
        var translations = new[] { Vector3.Zero, new Vector3(1, 2, 3) };
        for (var i = 0; i < translations.Length; i++)
        {
            var expected = NifTransformConverter.ConvertSourceTranslationToEngine(translations[i]);
            var offset = new Vector3(
                animation.Channels[0].Keyframes[i].FloatValue,
                animation.Channels[1].Keyframes[i].FloatValue,
                animation.Channels[2].Keyframes[i].FloatValue);
            var reconstructed = bone.BindTranslation + Vector3.Transform(offset, bone.BindRotation);
            AssertEqual(expected, reconstructed, $"translation key {i}");
        }

        Assert.All(animation.Channels, channel =>
        {
            Assert.Contains(channel.ChannelType, new[]
            {
                ImportedAnimationChannelType.TranslationX,
                ImportedAnimationChannelType.TranslationY,
                ImportedAnimationChannelType.TranslationZ,
            });
            Assert.Equal("child", channel.Target.BoneName);
        });
    }

    [Theory]
    [InlineData(new float[] { 0.5f, 2.0f }, new string[] { "start", "end" })]
    [InlineData(new float[] { 0.0f }, new string[] { "start" })]
    [InlineData(new float[] { 0.0f, 1.0f, 2.0f }, new string[] { "start", "loop", "end" })]
    public void InvalidTextKeysThrow(float[] times, string[] labels)
    {
        var keys = new List<(float Time, string Value)>();
        for (var i = 0; i < times.Length; i++)
        {
            keys.Add((times[i], labels[i]));
        }

        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1, 0, 0, 0)]);
        var ex = Assert.Throws<InvalidDataException>(
            () => Import(SingleChannelKf(data, duration: 2.0f, textKeys: keys)));

        Assert.Contains(AnimationKey, ex.Message);
    }

    [Fact]
    public void ConstantAccumulationRootAppliesAsOrdinaryChannels()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            translationKeys:
            [
                (0.0f, (1.0f, 1.0f, 1.0f), null, null),
                (2.0f, (1.0f, 1.0f, 1.0f), null, null),
            ]);

        var animation = Import(SingleChannelKf(
            data,
            duration: 2.0f,
            accumulationRoot: "child"))!;

        Assert.Equal(4, animation.Channels.Count);
        Assert.Equal(ImportedAnimationChannelType.Quaternion, animation.Channels[0].ChannelType);
        var bone = GetBone("child");
        var expected = NifTransformConverter.ConvertSourceTranslationToEngine(new Vector3(1, 1, 1));
        for (var i = 1; i < animation.Channels.Count; i++)
        {
            Assert.Equal(ImportedAnimationChannelType.TranslationX + i - 1, animation.Channels[i].ChannelType);

            // The root channel is constant relative to the world, so every
            // key in a translation channel carries the same (non-zero)
            // rest-relative offset.
            Assert.Equal(2, animation.Channels[i].Keyframes.Count);
            var first = animation.Channels[i].Keyframes[0].FloatValue;
            Assert.Equal(first, animation.Channels[i].Keyframes[1].FloatValue, 5);
        }

        var offset = new Vector3(
            animation.Channels[1].Keyframes[0].FloatValue,
            animation.Channels[2].Keyframes[0].FloatValue,
            animation.Channels[3].Keyframes[0].FloatValue);
        AssertEqual(expected, bone.BindTranslation + Vector3.Transform(offset, bone.BindRotation), "accumulation translation");
    }

    [Fact]
    public void VaryingAccumulationRootTranslationThrows()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            translationKeys:
            [
                (0.0f, (0.0f, 0.0f, 0.0f), null, null),
                (2.0f, (1.0f, 0.0f, 0.0f), null, null),
            ]);

        var ex = Assert.Throws<NotSupportedException>(
            () => Import(SingleChannelKf(data, duration: 2.0f, accumulationRoot: "child")));

        Assert.Contains("accumulation root", ex.Message);
    }

    [Fact]
    public void VaryingAccumulationRootRotationThrows()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys:
        [
            (0.0f, 1.0f, 0.0f, 0.0f, 0.0f),
            (2.0f, 0.70710678f, 0.0f, 0.70710678f, 0.0f),
        ]);

        var ex = Assert.Throws<NotSupportedException>(
            () => Import(SingleChannelKf(data, duration: 2.0f, accumulationRoot: "child")));

        Assert.Contains("accumulation root", ex.Message);
    }

    [Fact]
    public void UnitScaleKeysAreDropped()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys:
            [
                (0.0f, 1.0f, null, null),
                (2.0f, 1.0f, null, null),
            ]);

        var animation = Import(SingleChannelKf(data, duration: 2.0f))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal(ImportedAnimationChannelType.Quaternion, channel.ChannelType);
    }

    [Fact]
    public void LinearScaleKeysConvertToRestRelativeChannelAtSourceTimes()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys: [(0.0f, 1.0f, null, null), (2.0f, 2.0f, null, null)]);

        var animation = Import(SingleChannelKf(data, duration: 2.0f))!;

        Assert.Equal(2, animation.Channels.Count);
        var scaleChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.Scale);
        Assert.Equal(ImportedAnimationInterpolation.Linear, scaleChannel.Interpolation);
        Assert.Equal("child", scaleChannel.Target.BoneName);
        Assert.Equal(2, scaleChannel.Keyframes.Count);
        Assert.Equal(0.0, scaleChannel.Keyframes[0].Time.TotalSeconds, 5);
        Assert.Equal(2.0, scaleChannel.Keyframes[1].Time.TotalSeconds, 5);

        var bone = GetBone("child");
        Assert.Equal(1.0f * bone.BindScale, scaleChannel.Keyframes[0].FloatValue, 5);
        Assert.Equal(2.0f * bone.BindScale, scaleChannel.Keyframes[1].FloatValue, 5);
    }

    [Fact]
    public void ConstantNonUnitScaleKeysEmitAChannel()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys: [(0.0f, 1.5f, null, null), (2.0f, 1.5f, null, null)]);

        var animation = Import(SingleChannelKf(data, duration: 2.0f))!;

        var scaleChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.Scale);
        Assert.All(scaleChannel.Keyframes, keyframe =>
            Assert.Equal(1.5f, keyframe.FloatValue, 5));
    }

    [Fact]
    public void ZeroAnimatedScaleSampleIsAccepted()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys:
            [
                (0.0f, 1.0f, null, null),
                (1.0f, 0.0f, null, null),
                (2.0f, 1.0f, null, null),
            ]);

        var animation = Import(SingleChannelKf(data, duration: 2.0f))!;

        var scaleChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.Scale);
        Assert.Contains(scaleChannel.Keyframes, keyframe => keyframe.FloatValue == 0f);
    }

    [Fact]
    public void NegativeScaleKeyIsRejected()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys: [(0.0f, 1.0f, null, null), (2.0f, -0.5f, null, null)]);

        var ex = Assert.Throws<InvalidDataException>(
            () => Import(SingleChannelKf(data, duration: 2.0f)));

        Assert.Contains("scale key", ex.Message);
        Assert.Contains("non-negative", ex.Message);
    }

    [Fact]
    public void NonFiniteScaleKeyIsRejected()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys: [(0.0f, 1.0f, null, null), (2.0f, float.NaN, null, null)]);

        var ex = Assert.Throws<InvalidDataException>(
            () => Import(SingleChannelKf(data, duration: 2.0f)));

        Assert.Contains("scale key", ex.Message);
        Assert.Contains("finite", ex.Message);
    }

    [Fact]
    public void TbcScaleKeysImportWithZeroTangentsAsLinearValues()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys: [(0.0f, 1.0f, null, null), (2.0f, 1.5f, null, null)],
            scaleKeyTypeOverride: 3);

        var animation = Import(SingleChannelKf(data, duration: 2.0f))!;

        var scaleChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.Scale);
        Assert.Equal(1.0, scaleChannel.Keyframes[0].FloatValue, 5);
        Assert.Equal(1.5, scaleChannel.Keyframes[^1].FloatValue, 5);
    }

    [Fact]
    public void QuadraticScaleKeysResampleAtThirtyHertzWithHermiteMidpoint()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys:
            [
                (0.0f, 1.0f, Forward: 0.0f, Backward: 6.0f),
                (1.0f, 2.0f, Forward: 0.0f, Backward: 0.0f),
            ]);

        var animation = Import(SingleChannelKf(data, duration: 1.0f))!;

        var scaleChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.Scale);
        // 30 Hz steps over 1 s plus the duration and source key times.
        Assert.Equal(31, scaleChannel.Keyframes.Count);
        Assert.Equal(1.0f, scaleChannel.Keyframes[0].FloatValue, 5);
        Assert.Equal(2.0f, scaleChannel.Keyframes[^1].FloatValue, 5);

        // Segment-normalized Hermite: the outgoing tangent is the first
        // key's backward field (6), so the 0.5 s sample is
        // 0.5 * 1 + 0.5 * 2 + h10(0.5) * 6 = 2.25.
        var midpoint = scaleChannel.Keyframes.Single(
            keyframe => MathF.Abs((float)keyframe.Time.TotalSeconds - 0.5f) < 1e-3f);
        Assert.Equal(2.25f, midpoint.FloatValue, 4);
    }

    [Fact]
    public void ScaledRestDividesOffsetsForScaleAndTranslation()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            translationKeys:
            [
                (0.0f, (0.0f, -2.0f, 0.0f), null, null),
                (2.0f, (0.0f, -3.0f, 0.0f), null, null),
            ],
            scaleKeys: [(0.0f, 2.0f, null, null), (2.0f, 4.0f, null, null)]);

        var animation = Import(
            SingleChannelKf(data, duration: 2.0f),
            modelBytes: ScaledModelBytes(childScale: 2.0f))!;

        var bone = GetBoneFromModel(ScaledModelBytes(childScale: 2.0f), "child");
        Assert.Equal(2f, bone.BindScale, 5);

        var scaleChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.Scale);
        Assert.Equal(1.0f, scaleChannel.Keyframes[0].FloatValue, 5);
        Assert.Equal(2.0f, scaleChannel.Keyframes[1].FloatValue, 5);
        // offsetScale * restScale reconstructs the authored samples.
        Assert.Equal(2.0f, scaleChannel.Keyframes[0].FloatValue * bone.BindScale, 4);
        Assert.Equal(4.0f, scaleChannel.Keyframes[1].FloatValue * bone.BindScale, 4);

        // Translation offsets reconstruct authored samples through the
        // scaled rest: bindTranslation + offset * restScale * restRotation.
        var samples = new[] { new Vector3(0, -2, 0), new Vector3(0, -3, 0) };
        for (var i = 0; i < 2; i++)
        {
            var xChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationX);
            var yChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationY);
            var zChannel = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationZ);
            var offset = new Vector3(
                xChannel.Keyframes[i].FloatValue,
                yChannel.Keyframes[i].FloatValue,
                zChannel.Keyframes[i].FloatValue);
            var expected = NifTransformConverter.ConvertSourceTranslationToEngine(samples[i]);
            var reconstructed = bone.BindTranslation +
                Vector3.Transform(offset * bone.BindScale, bone.BindRotation);
            AssertEqual(expected, reconstructed, $"translation key {i}");
        }
    }

    [Fact]
    public void IdentityScaleOffsetsOnScaledRestAreDropped()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys: [(0.0f, 2.0f, null, null), (2.0f, 2.0f, null, null)]);

        var animation = Import(
            SingleChannelKf(data, duration: 2.0f),
            modelBytes: ScaledModelBytes(childScale: 2.0f))!;

        // Keys equal to the rest scale produce identity offsets and the
        // group is dropped, matching the C4c identity-group rule.
        var channel = Assert.Single(animation.Channels);
        Assert.Equal(ImportedAnimationChannelType.Quaternion, channel.ChannelType);
    }

    [Fact]
    public void SingularRestTargetIsRejectedNamingNodeAndScale()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);

        var ex = Assert.Throws<NotSupportedException>(
            () => Import(
                SingleChannelKf(data, duration: 2.0f, targetName: "boom"),
                modelBytes: CollapsedModelBytes()));

        Assert.Contains("'boom'", ex.Message);
        Assert.Contains("singular rest scale 0", ex.Message);
    }

    [Fact]
    public void MissingTargetThrows()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);

        var ex = Assert.Throws<InvalidDataException>(
            () => Import(SingleChannelKf(data, duration: 2.0f, targetName: "nonexistent")));

        Assert.Contains("no matching bone", ex.Message);
        Assert.Contains(AnimationKey, ex.Message);
    }

    [Fact]
    public void DuplicateTargetThrows()
    {
        var builder = new NifTestDataBuilder();
        var sequenceType = builder.AddBlockType("NiControllerSequence");
        var textKeyType = builder.AddBlockType("NiTextKeyExtraData");
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");

        builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
            "clip", "ROOT", 1, ("child", 2), ("child", 4)));
        builder.AddBlock(textKeyType, NifTestKeyframe.TextKeyExtraData(
            "", (0.0f, "start"), (2.0f, "end")));
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(
            data: 3, stopTime: 2.0f));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]));
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(
            data: 5, stopTime: 2.0f));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Import(builder.Build()));

        Assert.Contains("more than once", ex.Message);
    }

    [Fact]
    public void WrongControllerFlagsAreRejected()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);

        var ex = Assert.Throws<NotSupportedException>(
            () => Import(SingleChannelKf(data, duration: 2.0f, flags: 0x30)));

        Assert.Contains("flags", ex.Message);
    }

    [Theory]
    [InlineData(0x0C)]
    [InlineData(0x05)]
    public void LegacyProofControllerFlagsImport(ushort flags)
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);

        var animation = Import(SingleChannelKf(data, duration: 2.0f, flags: flags, legacy: true));

        Assert.NotNull(animation);
        Assert.Equal(TimeSpan.FromSeconds(2.0f), animation!.Duration);
    }

    [Fact]
    public void LegacyTrailingSpaceEndTextKeyImports()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);

        var animation = Import(SingleChannelKf(
            data,
            duration: 2.0f,
            textKeys: [(0.0f, "start"), (2.0f, "end ")],
            flags: 0x0C,
            legacy: true));

        Assert.NotNull(animation);
        Assert.Equal(TimeSpan.FromSeconds(2.0f), animation!.Duration);
    }

    [Fact]
    public void UnknownLegacyFlagIsRejectedNamingFlagAndAsset()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);

        var ex = Assert.Throws<NotSupportedException>(
            () => Import(SingleChannelKf(data, duration: 2.0f, flags: 0x18, legacy: true)));

        Assert.Contains(AnimationKey, ex.Message);
        Assert.Contains("0x0018", ex.Message);
        Assert.Contains("0x0C and 0x05", ex.Message);
    }

    [Fact]
    public void LegacyFlagsAreRejectedForModernClips()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);

        var ex = Assert.Throws<NotSupportedException>(
            () => Import(SingleChannelKf(data, duration: 2.0f, flags: 0x05)));

        Assert.Contains("0x2C", ex.Message);
    }

    [Fact]
    public void UppercaseKfExtensionResolvesThroughZipLookup()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);
        var kfBytes = SingleChannelKf(data, duration: 2.0f, flags: 0x0C, legacy: true);

        using var archive = new MemoryStream();
        using (var writer = new System.IO.Compression.ZipArchive(archive, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var model = writer.CreateEntry($"cache/nifcache/{ModelName}.nifcache");
            using (var modelStream = model.Open())
            {
                modelStream.Write(ModelBytes(), 0, ModelBytes().Length);
            }

            // The DDF stack references legacy clips with uppercase .KF
            // extensions; the archive entry keeps that exact casing.
            var clip = writer.CreateEntry("anims/clip.KF");
            using (var clipStream = clip.Open())
            {
                clipStream.Write(kfBytes, 0, kfBytes.Length);
            }
        }

        archive.Position = 0;
        var animation = new NifAnimationImporter().ImportAnimation(
            ModelName + "#kf=anims/clip.KF",
            new AssetImportContext(new ZipFileSystem(archive), null));

        Assert.NotNull(animation);
        Assert.Equal(TimeSpan.FromSeconds(2.0f), animation!.Duration);
    }

    [Fact]
    public void LegacyMaterialColorControllerIsRejectedNamingC4e()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiMaterialColorController",
            NifTestPropertyAnimation.MaterialColorController(data: 3, targetColor: 0, stopTime: 2.0f),
            "NiPosData",
            NifTestPropertyAnimation.PosData([(0.0f, (0, 0, 0), null, null)]),
            targetName: "hull\nPROP\nNiMaterialProperty",
            legacy: true)));

        Assert.Contains("NifMaterialColorController", ex.Message);
        Assert.Contains("C4e", ex.Message);
    }

    [Fact]
    public void LegacyVisControllerIsRejectedNamingC4e()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiVisController",
            NifTestPropertyAnimation.VisController(data: 3, stopTime: 2.0f),
            "NiVisData",
            NifTestPropertyAnimation.VisData((0.0f, 0), (2.0f, 1)),
            targetName: "child",
            legacy: true)));

        Assert.Contains("NifVisController", ex.Message);
        Assert.Contains("C4e", ex.Message);
    }

    [Fact]
    public void LegacyUVControllerIsRejectedNamingC4e()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiUVController",
            NifTestPropertyAnimation.UVController(data: 3, stopTime: 2.0f),
            "NiUVData",
            NifTestPropertyAnimation.UVData(
                uTranslationKeys: [(0.0f, 0.0f, null, null), (2.0f, 1.0f, null, null)]),
            targetName: "hull\nPROP\nNiTexturingProperty",
            legacy: true)));

        Assert.Contains("NifUVController", ex.Message);
        Assert.Contains("C4e", ex.Message);
    }

    [Fact]
    public void LegacyMorphControllerIsRejectedNamingC4g()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiGeomMorpherController",
            NifTestPropertyAnimation.MorphController(data: 3, stopTime: 2.0f),
            "NiMorphData",
            NifTestPropertyAnimation.MorphData(4, [
                (null, null),
                ([(0.0f, 0.0f, null, null), (2.0f, 1.0f, null, null)], null)]),
            targetName: "hull",
            legacy: true)));

        Assert.Contains("NifGeomMorpherController", ex.Message);
        Assert.Contains("C4g", ex.Message);
    }

    [Fact]
    public void ChainedControllerIsRejected()
    {
        var builder = new NifTestDataBuilder();
        var sequenceType = builder.AddBlockType("NiControllerSequence");
        var textKeyType = builder.AddBlockType("NiTextKeyExtraData");
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");

        builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
            "clip", "ROOT", 1, ("child", 2)));
        builder.AddBlock(textKeyType, NifTestKeyframe.TextKeyExtraData(
            "", (0.0f, "start"), (2.0f, "end")));
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(
            data: 4, stopTime: 2.0f, next: 3));
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(
            data: 4, stopTime: 2.0f));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<NotSupportedException>(() => Import(builder.Build()));

        Assert.Contains("chained", ex.Message);
    }

    [Fact]
    public void ControllerStopBeyondDurationThrows()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);

        var ex = Assert.Throws<InvalidDataException>(
            () => Import(SingleChannelKf(data, duration: 2.0f, stopTime: 2.01f)));

        Assert.Contains("outside the clip duration", ex.Message);
    }

    [Fact]
    public void KeyTimeBeyondDurationThrows()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys:
        [
            (0.0f, 1.0f, 0.0f, 0.0f, 0.0f),
            (2.01f, 1.0f, 0.0f, 0.0f, 0.0f),
        ]);

        var ex = Assert.Throws<InvalidDataException>(
            () => Import(SingleChannelKf(data, duration: 2.0f)));

        Assert.Contains("outside the clip range", ex.Message);
    }

    [Fact]
    public void VisControllerEmitsStepBoneVisibilityChannel()
    {
        var animation = ImportProperty(PropertyKf(
            "NiVisController",
            NifTestPropertyAnimation.VisController(data: 3, stopTime: 2.0f),
            "NiVisData",
            NifTestPropertyAnimation.VisData((0.0f, 0), (0.5f, 1), (2.0f, 1)),
            targetName: "child"))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal(ImportedAnimationChannelType.Visibility, channel.ChannelType);
        Assert.Equal(ImportedAnimationInterpolation.Step, channel.Interpolation);
        Assert.Equal("child", channel.Target.BoneName);
        Assert.Equal(3, channel.Keyframes.Count);
        Assert.False(channel.Keyframes[0].BoolValue);
        Assert.True(channel.Keyframes[1].BoolValue);
        Assert.True(channel.Keyframes[2].BoolValue);
    }

    [Fact]
    public void VisControllerAcceptsBoolDataReplacementFormWithConstantKeys()
    {
        var animation = ImportProperty(PropertyKf(
            "NiVisController",
            NifTestPropertyAnimation.VisController(data: 3, stopTime: 2.0f),
            "NiBoolData",
            NifTestPropertyAnimation.BoolData(
                [(0.0f, 0), (2.0f, 1)],
                keyTypeOverride: 5u),
            targetName: "child"))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal(2, channel.Keyframes.Count);
        Assert.False(channel.Keyframes[0].BoolValue);
        Assert.True(channel.Keyframes[1].BoolValue);
    }

    [Fact]
    public void VisTargetShapeNameResolvesToSubObjectChannel()
    {
        var animation = ImportProperty(PropertyKf(
            "NiVisController",
            NifTestPropertyAnimation.VisController(data: 3, stopTime: 2.0f),
            "NiVisData",
            NifTestPropertyAnimation.VisData((0.0f, 0), (2.0f, 1)),
            targetName: "HULL"))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal("test_model.0.hull", channel.Target.SubObjectName);
    }

    [Fact]
    public void VisTargetMatchingNothingKeepsVerbatimName()
    {
        var animation = ImportProperty(PropertyKf(
            "NiVisController",
            NifTestPropertyAnimation.VisController(data: 3, stopTime: 2.0f),
            "NiVisData",
            NifTestPropertyAnimation.VisData((0.0f, 0), (2.0f, 1)),
            targetName: "market_open_1"))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal("market_open_1", channel.Target.SubObjectName);
    }

    [Fact]
    public void VisControllerWrongFlagsRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiVisController",
            NifTestPropertyAnimation.VisController(data: 3, flags: 0x28, stopTime: 2.0f),
            "NiVisData",
            NifTestPropertyAnimation.VisData((0.0f, 0), (2.0f, 1)),
            targetName: "child")));

        Assert.Contains("visibility controller flags", ex.Message);
    }

    [Fact]
    public void PropertyControllerFrequencyMustBeOne()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiVisController",
            NifTestPropertyAnimation.VisController(data: 3, frequency: 2.0f, stopTime: 2.0f),
            "NiVisData",
            NifTestPropertyAnimation.VisData((0.0f, 0), (2.0f, 1)),
            targetName: "child")));

        Assert.Contains("frequency", ex.Message);
    }

    [Fact]
    public void AlphaControllerEmitsMaterialAlphaChannel()
    {
        var animation = ImportProperty(PropertyKf(
            "NiAlphaController",
            NifTestPropertyAnimation.AlphaController(data: 3, stopTime: 2.0f),
            "NiFloatData",
            NifTestPropertyAnimation.FloatData(
                [(0.0f, 1.0f, null, null), (2.0f, 0.25f, null, null)]),
            targetName: "hull\nPROP\nNiMaterialProperty"))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal(ImportedAnimationChannelType.MaterialAlpha, channel.ChannelType);
        Assert.Equal("test_model.0.hull", channel.Target.SubObjectName);
        Assert.Equal(0, channel.Target.MaterialIndex);
        Assert.Equal(1.0f, channel.Keyframes[0].FloatValue);
        Assert.Equal(0.25f, channel.Keyframes[1].FloatValue);
    }

    [Fact]
    public void AlphaKeyOutsideUnitIntervalRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ImportProperty(PropertyKf(
            "NiAlphaController",
            NifTestPropertyAnimation.AlphaController(data: 3, stopTime: 2.0f),
            "NiFloatData",
            NifTestPropertyAnimation.FloatData([(0.0f, 1.5f, null, null)]),
            targetName: "hull\nPROP\nNiMaterialProperty")));

        Assert.Contains("outside [0, 1]", ex.Message);
    }

    [Fact]
    public void AlphaControllerWrongFlagsRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiAlphaController",
            NifTestPropertyAnimation.AlphaController(data: 3, flags: 0x2C, stopTime: 2.0f),
            "NiFloatData",
            NifTestPropertyAnimation.FloatData([(0.0f, 1.0f, null, null)]),
            targetName: "hull\nPROP\nNiMaterialProperty")));

        Assert.Contains("alpha controller flags", ex.Message);
    }

    [Fact]
    public void TextureTransformTranslateUEmitsMaterialSlotChannel()
    {
        var animation = ImportProperty(PropertyKf(
            "NiTextureTransformController",
            NifTestPropertyAnimation.TextureTransformController(data: 3, operation: 0, stopTime: 2.0f),
            "NiFloatData",
            NifTestPropertyAnimation.FloatData([(0.0f, 0.0f, null, null), (2.0f, 1.5f, null, null)]),
            targetName: "hull\nPROP\nNiTexturingProperty"))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal(ImportedAnimationChannelType.TextureTranslateU, channel.ChannelType);
        Assert.Equal("test_model.0.hull", channel.Target.SubObjectName);
        Assert.Equal(1.5f, channel.Keyframes[1].FloatValue);
    }

    [Fact]
    public void TextureTransformRotateOperationRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiTextureTransformController",
            NifTestPropertyAnimation.TextureTransformController(data: 3, operation: 2, stopTime: 2.0f),
            "NiFloatData",
            NifTestPropertyAnimation.FloatData([(0.0f, 0.0f, null, null)]),
            targetName: "hull\nPROP\nNiTexturingProperty")));

        Assert.Contains("operation", ex.Message);
    }

    [Fact]
    public void TextureTransformShaderMapRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiTextureTransformController",
            NifTestPropertyAnimation.TextureTransformController(data: 3, shaderMap: true, stopTime: 2.0f),
            "NiFloatData",
            NifTestPropertyAnimation.FloatData([(0.0f, 0.0f, null, null)]),
            targetName: "hull\nPROP\nNiTexturingProperty")));

        Assert.Contains("shader map", ex.Message);
    }

    [Fact]
    public void TextureTransformNonZeroSlotRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiTextureTransformController",
            NifTestPropertyAnimation.TextureTransformController(data: 3, textureSlot: 1, stopTime: 2.0f),
            "NiFloatData",
            NifTestPropertyAnimation.FloatData([(0.0f, 0.0f, null, null)]),
            targetName: "hull\nPROP\nNiTexturingProperty")));

        Assert.Contains("texture slot 1", ex.Message);
    }

    [Fact]
    public void MaterialColorEmitsVector3Channel()
    {
        var animation = ImportProperty(PropertyKf(
            "NiMaterialColorController",
            NifTestPropertyAnimation.MaterialColorController(data: 3, targetColor: 0, stopTime: 2.0f),
            "NiPosData",
            NifTestPropertyAnimation.PosData(
                [(0.0f, (0, 0, 0), null, null), (2.0f, (1, 1, 1), null, null)]),
            targetName: "hull\nPROP\nNiMaterialProperty"))!;

        var channel = Assert.Single(animation.Channels);
        Assert.Equal(ImportedAnimationChannelType.MaterialAmbientColor, channel.ChannelType);
        Assert.Equal("test_model.0.hull", channel.Target.SubObjectName);
        Assert.Equal(new Vector3(1, 1, 1), channel.Keyframes[1].Vector3Value);
    }

    [Fact]
    public void MaterialColorUndefinedTargetRejected()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ImportProperty(PropertyKf(
            "NiMaterialColorController",
            NifTestPropertyAnimation.MaterialColorController(data: 3, targetColor: 9, stopTime: 2.0f),
            "NiPosData",
            NifTestPropertyAnimation.PosData([(0.0f, (0, 0, 0), null, null)]),
            targetName: "hull\nPROP\nNiMaterialProperty")));

        Assert.Contains("color 9", ex.Message);
    }

    [Fact]
    public void PropertyTargetWithUnknownShapeRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ImportProperty(PropertyKf(
            "NiAlphaController",
            NifTestPropertyAnimation.AlphaController(data: 3, stopTime: 2.0f),
            "NiFloatData",
            NifTestPropertyAnimation.FloatData([(0.0f, 1.0f, null, null)]),
            targetName: "nope\nPROP\nNiMaterialProperty")));

        Assert.Contains("no converted shape named 'nope'", ex.Message);
    }

    [Fact]
    public void LinearMorphWeightKeysKeepSourceTimesAndValues()
    {
        var animation = ImportProperty(PropertyKf(
            "NiGeomMorpherController",
            NifTestPropertyAnimation.MorphController(data: 3, stopTime: 2.0f),
            "NiMorphData",
            NifTestPropertyAnimation.MorphData(4, [
                (null, null),
                ([(0.0f, 0.0f, null, null), (2.0f, 1.0f, null, null)], null)]),
            targetName: "hull"));

        Assert.NotNull(animation);
        var channel = Assert.Single(animation!.MorphChannels);
        Assert.Equal("test_model.0.hull", channel.PrimitiveId);
        var targetName = Assert.Single(channel.TargetNames);
        Assert.Equal("Target1", targetName);
        Assert.Equal(2, channel.Times.Count);
        Assert.Equal(0.0f, channel.Times[0]);
        Assert.Equal(2.0f, channel.Times[1]);
        Assert.Equal(0.0f, channel.WeightVectors[0][0]);
        Assert.Equal(1.0f, channel.WeightVectors[1][0]);
    }

    [Fact]
    public void QuadraticMorphWeightKeysResampleAtThirtyHertz()
    {
        var animation = ImportProperty(PropertyKf(
            "NiGeomMorpherController",
            NifTestPropertyAnimation.MorphController(data: 3, stopTime: 1.0f),
            "NiMorphData",
            NifTestPropertyAnimation.MorphData(4, [
                (null, null),
                ([(0.0f, 0.0f, 0.0f, 2.0f), (1.0f, 1.0f, 0.0f, 0.0f)], null)]),
            targetName: "hull",
            duration: 1.0f));

        Assert.NotNull(animation);
        var channel = Assert.Single(animation!.MorphChannels);
        Assert.Equal(31, channel.Times.Count);
        Assert.Equal(0.5f, channel.Times[15]);
        // Segment-normalized Hermite: outgoing tangent is the first key's
        // backward field, so the midpoint lands at 0.5 + 0.125 * 2 = 0.75.
        Assert.Equal(0.75f, channel.WeightVectors[15][0], 5);
        Assert.Equal(1.0f, channel.WeightVectors[^1][0]);
    }

    [Fact]
    public void MorphWeightOutsideUnitIntervalRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ImportProperty(PropertyKf(
            "NiGeomMorpherController",
            NifTestPropertyAnimation.MorphController(data: 3, stopTime: 2.0f),
            "NiMorphData",
            NifTestPropertyAnimation.MorphData(4, [
                (null, null),
                ([(0.0f, 1.5f, null, null), (2.0f, 1.0f, null, null)], null)]),
            targetName: "hull")));

        Assert.Contains("outside [0, 1]", ex.Message);
    }

    [Fact]
    public void MorphWeightTargetWithoutShapeFails()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ImportProperty(PropertyKf(
            "NiGeomMorpherController",
            NifTestPropertyAnimation.MorphController(data: 3, stopTime: 2.0f),
            "NiMorphData",
            NifTestPropertyAnimation.MorphData(4, [
                (null, null),
                ([(0.0f, 0.0f, null, null), (2.0f, 1.0f, null, null)], null)]),
            targetName: "nope")));

        Assert.Contains("does not match any converted shape", ex.Message);
    }

    [Fact]
    public void MorpherWithoutTargetsFails()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ImportProperty(PropertyKf(
            "NiGeomMorpherController",
            NifTestPropertyAnimation.MorphController(data: 3, stopTime: 2.0f),
            "NiMorphData",
            NifTestPropertyAnimation.MorphData(0, [([(0.0f, 0.0f, null, null)], null)]),
            targetName: "hull")));

        Assert.Contains("no morphable targets", ex.Message);
    }

    [Fact]
    public void MultiSequenceKfRejected()
    {
        // The root-list footer parses cleanly for two sequences, so the
        // importer's own multi-sequence guard fires during import.
        var builder = new NifTestDataBuilder();
        var sequenceType = builder.AddBlockType("NiControllerSequence");
        var textKeyType = builder.AddBlockType("NiTextKeyExtraData");
        var controllerType = builder.AddBlockType("NiVisController");
        var dataType = builder.AddBlockType("NiVisData");

        builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
            "clip", "ROOT", 1, ("child", 2)));
        builder.AddBlock(textKeyType, NifTestKeyframe.TextKeyExtraData(
            "", (0.0f, "start"), (2.0f, "end")));
        builder.AddBlock(controllerType, NifTestPropertyAnimation.VisController(
            data: 3, stopTime: 2.0f));
        builder.AddBlock(dataType, NifTestPropertyAnimation.VisData((0.0f, 1), (2.0f, 1)));

        builder.AddTrailing(NifTestBlocks.RootsFooter(0, 1));

        var ex = Assert.Throws<NotSupportedException>(
            () => ImportProperty(builder.Build()));

        Assert.Contains("2 root blocks", ex.Message);
        Assert.Contains("multi-sequence", ex.Message);
    }

    [Fact]
    public void TexturingTransformDecodesAtImport()
    {
        var file = FileFormats.Nif.NifFile.Read(new MemoryStream(PropertyModelBytes()));
        var texturing = Assert.IsType<FileFormats.Nif.NifTexturingProperty>(file.Blocks[5]);
        var transform = texturing.Slots["Base"].Transform;
        Assert.NotNull(transform);
        Assert.Equal(0.25f, transform!.Value.TranslationU);
        Assert.Equal(-0.5f, transform.Value.TranslationV);
        Assert.Equal(2.0f, transform.Value.TilingU);
        Assert.Equal(3.0f, transform.Value.TilingV);
        Assert.Equal(1.5f, transform.Value.Rotation);
        Assert.Equal(1u, transform.Value.TransformType);
        Assert.Equal(0.25f, transform.Value.CenterU);
        Assert.Equal(0.75f, transform.Value.CenterV);
    }

    private static byte[] QuadraticFreeKf()
    {
        var data = NifTestKeyframe.KeyframeData(quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)]);
        return SingleChannelKf(data, duration: 2.0f);
    }

    private static byte[] SingleChannelKf(
        byte[] dataBytes,
        float duration,
        string targetName = "child",
        string accumulationRoot = "ROOT",
        IReadOnlyList<(float Time, string Value)>? textKeys = null,
        ushort flags = 0x2C,
        float stopTime = float.NaN,
        bool legacy = false)
    {
        var builder = new NifTestDataBuilder();
        if (legacy)
        {
            builder.HeaderString = FileFormats.Nif.NifHeader.LegacyHeaderString;
            builder.Version = FileFormats.Nif.NifHeader.LegacyVersion;
            builder.HasUserVersion = false;
        }

        var sequenceType = builder.AddBlockType("NiControllerSequence");
        var textKeyType = builder.AddBlockType("NiTextKeyExtraData");
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");

        builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
            "clip", accumulationRoot, 1, (targetName, 2)));
        builder.AddBlock(textKeyType, NifTestKeyframe.TextKeyExtraData(
            "", (textKeys ?? [(0.0f, "start"), (duration, "end")]).ToArray()));
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(
            data: 3,
            flags: flags,
            stopTime: float.IsNaN(stopTime) ? duration : stopTime));
        builder.AddBlock(dataType, dataBytes);
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static byte[] PropertyKf(
        string controllerTypeName,
        byte[] controllerBody,
        string dataTypeName,
        byte[] dataBody,
        string targetName,
        float duration = 2.0f,
        bool legacy = false)
    {
        var builder = new NifTestDataBuilder();
        if (legacy)
        {
            builder.HeaderString = FileFormats.Nif.NifHeader.LegacyHeaderString;
            builder.Version = FileFormats.Nif.NifHeader.LegacyVersion;
            builder.HasUserVersion = false;
        }

        var sequenceType = builder.AddBlockType("NiControllerSequence");
        var textKeyType = builder.AddBlockType("NiTextKeyExtraData");
        var controllerType = builder.AddBlockType(controllerTypeName);
        var dataType = builder.AddBlockType(dataTypeName);

        builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
            "clip", "ROOT", 1, (targetName, 2)));
        builder.AddBlock(textKeyType, NifTestKeyframe.TextKeyExtraData(
            "", (0.0f, "start"), (duration, "end")));
        builder.AddBlock(controllerType, controllerBody);
        builder.AddBlock(dataType, dataBody);
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static byte[] PropertyModelBytes()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var materialType = builder.AddBlockType("NiMaterialProperty");
        var texturingType = builder.AddBlockType("NiTexturingProperty");
        var sourceTextureType = builder.AddBlockType("NiSourceTexture");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1, 2]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "child", translation: (0, -2, 0), rotation: OarRotation));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(
            name: "hull", data: 4, properties: [3, 5]));
        builder.AddBlock(materialType, NifTestProperties.Material());
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)],
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddBlock(texturingType, NifTestProperties.Texturing(
            baseSlot: (6, 3, 2, 0, 0, -100),
            baseTransform: (0.25f, -0.5f, 2.0f, 3.0f, 1.5f, 1u, 0.25f, 0.75f)));
        builder.AddBlock(sourceTextureType, NifTestProperties.SourceTexture("hull.tga"));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static ImportedAnimation? ImportProperty(byte[]? kfBytes)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile($"cache/nifcache/{ModelName}.nifcache", PropertyModelBytes());
        fileSystem.AddFile(KfPath, kfBytes!);

        return new NifAnimationImporter().ImportAnimation(
            AnimationKey,
            new AssetImportContext(fileSystem, null));
    }

    private static byte[] ModelBytes()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var zBufferType = builder.AddBlockType("NiZBufferProperty");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "child", children: [3], translation: (0, -2, 0), rotation: OarRotation));
        builder.AddBlock(zBufferType, NifTestProperties.ZBuffer(flags: 3, function: 3));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 4, properties: [2]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)],
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static ImportedAnimation? Import(
        byte[]? kfBytes,
        string key = AnimationKey,
        bool omitModel = false,
        bool omitKf = false,
        byte[]? modelBytes = null)
    {
        var fileSystem = new MemoryFileSystem();
        if (!omitModel)
        {
            fileSystem.AddFile($"cache/nifcache/{ModelName}.nifcache", modelBytes ?? ModelBytes());
        }

        if (!omitKf && kfBytes is not null)
        {
            fileSystem.AddFile(KfPath, kfBytes);
        }

        return new NifAnimationImporter().ImportAnimation(
            key,
            new AssetImportContext(fileSystem, null));
    }

    private static byte[] ScaledModelBytes(float childScale)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var zBufferType = builder.AddBlockType("NiZBufferProperty");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(
            name: "child", children: [3], translation: (0, -2, 0), rotation: OarRotation,
            scale: childScale));
        builder.AddBlock(zBufferType, NifTestProperties.ZBuffer(flags: 3, function: 3));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 4, properties: [2]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)],
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static byte[] CollapsedModelBytes()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var shapeType = builder.AddBlockType("NiTriShape");
        var dataType = builder.AddBlockType("NiTriShapeData");
        var zBufferType = builder.AddBlockType("NiZBufferProperty");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1, 3]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "boom", scale: 0.0f));
        builder.AddBlock(zBufferType, NifTestProperties.ZBuffer(flags: 3, function: 3));
        builder.AddBlock(shapeType, NifTestGeometry.Shape(name: "hull", data: 4, properties: [2]));
        builder.AddBlock(dataType, NifTestGeometry.GeometryData(
            numVertices: 4,
            vertices: [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)],
            normals: [(0, 0, 1), (0, 0, 1), (0, 0, 1), (0, 0, 1)],
            boundCenter: (0.5f, 0.5f, 0.5f),
            boundRadius: 1.0f,
            triangles: [(0, 1, 2), (0, 2, 3)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static ImportedBone GetBone(string name)
    {
        return GetBoneFromModel(ModelBytes(), name);
    }

    private static ImportedBone GetBoneFromModel(byte[] modelBytes, string name)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile($"cache/nifcache/{ModelName}.nifcache", modelBytes);
        var model = new NifModelImporter().ImportModel(
            ModelName,
            new AssetImportContext(fileSystem, null))!;
        return Assert.Single(model.Skeleton.Bones, bone => bone.Name == name);
    }

    private static MemoryFileSystem CreateFileSystem()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile($"cache/nifcache/{ModelName}.nifcache", ModelBytes());
        return fileSystem;
    }

    // The real R_OAR14 stored rest rotation from nwg1_galley.nifcache.
    private static readonly FileFormats.Nif.NifMatrix33 OarRotation = new(
        0.38575035f, -0.5062548f, -0.7712994f,
        -0.7839769f, -0.6206029f, 0.015251815f,
        -0.48639184f, 0.5987975f, -0.63628995f);

    private static void AssertEqual(Quaternion expected, Quaternion actual, string context)
    {
        const float epsilon = 1e-4f;
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= epsilon &&
            MathF.Abs(expected.Y - actual.Y) <= epsilon &&
            MathF.Abs(expected.Z - actual.Z) <= epsilon &&
            MathF.Abs(expected.W - actual.W) <= epsilon,
            $"{context}: expected {expected}, got {actual}.");
    }

    private static void AssertEqual(Vector3 expected, Vector3 actual, string context)
    {
        const float epsilon = 1e-4f;
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= epsilon &&
            MathF.Abs(expected.Y - actual.Y) <= epsilon &&
            MathF.Abs(expected.Z - actual.Z) <= epsilon,
            $"{context}: expected {expected}, got {actual}.");
    }
}
