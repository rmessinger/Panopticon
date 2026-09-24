using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Nif;
using OpenSage.IO;
using OpenSage.Tests.Data.Nif;
using Xunit;
using NifTestBlocks = OpenSage.Tests.Data.Nif.NifTestBlocks;

namespace OpenSage.Tests.Content.Importing;

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
    public void NonUnitScaleKeysAreRejected()
    {
        var data = NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            scaleKeys: [(0.0f, 1.0f, null, null), (2.0f, 1.5f, null, null)]);

        var ex = Assert.Throws<NotSupportedException>(
            () => Import(SingleChannelKf(data, duration: 2.0f)));

        Assert.Contains("scale key", ex.Message);
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
        float stopTime = float.NaN)
    {
        var builder = new NifTestDataBuilder();
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
        bool omitKf = false)
    {
        var fileSystem = new MemoryFileSystem();
        if (!omitModel)
        {
            fileSystem.AddFile($"cache/nifcache/{ModelName}.nifcache", ModelBytes());
        }

        if (!omitKf && kfBytes is not null)
        {
            fileSystem.AddFile(KfPath, kfBytes);
        }

        return new NifAnimationImporter().ImportAnimation(
            key,
            new AssetImportContext(fileSystem, null));
    }

    private static ImportedBone GetBone(string name)
    {
        var model = new NifModelImporter().ImportModel(
            ModelName,
            new AssetImportContext(CreateFileSystem(), null))!;
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
