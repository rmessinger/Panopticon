using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using OpenSage.Content;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.W3d;
using OpenSage.FileFormats.W3d;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public sealed class W3dAnimationImporterTests
{
    private static W3dFile CreateFile(params W3dChunk[] chunks)
    {
        return new W3dFile(
            "art/w3d/test.w3d",
            [.. chunks],
            [],
            new Dictionary<string, W3dChunk>(),
            Hierarchy: null,
            HLod: null);
    }

    private static W3dAnimationHeader CreateUncompressedHeader(uint numFrames = 10, uint frameRate = 5)
    {
        return new W3dAnimationHeader(0, "Run", "HV", numFrames, frameRate);
    }

    [Fact]
    public void UncompressedQuaternionChannel_Converts()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        var channel = new W3dAnimationChannel(
            FirstFrame: 0,
            LastFrame: 2,
            VectorLength: 4,
            ChannelType: W3dAnimationChannelType.Quaternion,
            Pivot: 3,
            Unknown: 0,
            Data: new[] { new W3dAnimationChannelDatum(Quaternion.Identity), new W3dAnimationChannelDatum(rotation) },
            NumPadBytes: 0);

        var imported = W3dAnimationImporter.Convert(CreateFile(new W3dAnimation(CreateUncompressedHeader(), [channel])));

        Assert.NotNull(imported);
        Assert.Equal("HV.Run", imported!.Name);
        Assert.Equal(TimeSpan.FromSeconds(2.0), imported.Duration);

        var clip = Assert.Single(imported.Channels);
        Assert.Equal(3, clip.BoneIndex);
        Assert.Equal(ImportedAnimationChannelType.Quaternion, clip.ChannelType);
        Assert.Equal(2, clip.Keyframes.Count);
        Assert.Equal(TimeSpan.Zero, clip.Keyframes[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(0.2), clip.Keyframes[1].Time);
        Assert.Equal(Quaternion.Identity, clip.Keyframes[0].QuaternionValue);
        Assert.Equal(rotation, clip.Keyframes[1].QuaternionValue);
    }

    [Fact]
    public void UncompressedTranslationChannel_ConvertsWithFirstFrameOffset()
    {
        var channel = new W3dAnimationChannel(
            FirstFrame: 2,
            LastFrame: 3,
            VectorLength: 1,
            ChannelType: W3dAnimationChannelType.TranslationX,
            Pivot: 0,
            Unknown: 0,
            Data: new[] { new W3dAnimationChannelDatum(1.0f), new W3dAnimationChannelDatum(2.0f) },
            NumPadBytes: 0);

        var imported = W3dAnimationImporter.Convert(CreateFile(new W3dAnimation(CreateUncompressedHeader(), [channel])));

        var clip = Assert.Single(imported!.Channels);
        Assert.Equal(ImportedAnimationChannelType.TranslationX, clip.ChannelType);
        Assert.Equal(TimeSpan.FromSeconds(0.4), clip.Keyframes[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(0.6), clip.Keyframes[1].Time);
        Assert.Equal(1.0f, clip.Keyframes[0].FloatValue);
        Assert.Equal(2.0f, clip.Keyframes[1].FloatValue);
    }

    [Fact]
    public void VisibilityBitChannel_PadsWithDefaultValues()
    {
        var channel = new W3dBitChannel(
            FirstFrame: 1,
            LastFrame: 3,
            ChannelType: W3dBitChannelType.Visibility,
            Pivot: 2,
            DefaultValue: true,
            Data: new[] { false });

        var imported = W3dAnimationImporter.Convert(
            CreateFile(new W3dAnimation(CreateUncompressedHeader(numFrames: 5), [channel])));

        var clip = Assert.Single(imported!.Channels);
        Assert.Equal(ImportedAnimationChannelType.Visibility, clip.ChannelType);
        Assert.Equal(3, clip.Keyframes.Count);
        Assert.Equal(TimeSpan.Zero, clip.Keyframes[0].Time);
        Assert.True(clip.Keyframes[0].BoolValue);
        Assert.Equal(TimeSpan.FromSeconds(0.2), clip.Keyframes[1].Time);
        Assert.False(clip.Keyframes[1].BoolValue);
        Assert.Equal(TimeSpan.FromSeconds(0.8), clip.Keyframes[2].Time);
        Assert.True(clip.Keyframes[2].BoolValue);
    }

    [Fact]
    public void TimeCodedChannel_Converts()
    {
        var channel = new W3dTimeCodedAnimationChannel(
            NumTimeCodes: 2,
            Pivot: 1,
            VectorLength: 1,
            ChannelType: W3dAnimationChannelType.TranslationY,
            Data: new[]
            {
                new W3dTimeCodedDatum(TimeCode: 0, NonInterpolatedMovement: false, Value: new W3dAnimationChannelDatum(1.0f)),
                new W3dTimeCodedDatum(TimeCode: 10, NonInterpolatedMovement: false, Value: new W3dAnimationChannelDatum(2.0f)),
            });

        var imported = W3dAnimationImporter.Convert(CreateFile(new W3dCompressedAnimation(
            new W3dCompressedAnimationHeader(0, "Walk", "HV", NumFrames: 10, FrameRate: 5, W3dCompressedAnimationFlavor.TimeCoded),
            [channel],
            [],
            [],
            [])));

        Assert.Equal("HV.Walk", imported!.Name);
        var clip = Assert.Single(imported.Channels);
        Assert.Equal(ImportedAnimationChannelType.TranslationY, clip.ChannelType);
        Assert.Equal(2, clip.Keyframes.Count);
        Assert.Equal(TimeSpan.Zero, clip.Keyframes[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(2.0), clip.Keyframes[1].Time);
        Assert.Equal(2.0f, clip.Keyframes[1].FloatValue);
    }

    [Fact]
    public void AdaptiveDeltaChannel_ConvertsDecodedValues()
    {
        var data = new W3dAdaptiveDeltaData(
            BitCount: W3dAdaptiveDeltaBitCount.EightBits,
            VectorLength: 1,
            InitialDatum: new W3dAnimationChannelDatum(1.5f),
            DeltaBlocks: new[]
            {
                new W3dAdaptiveDeltaBlock(VectorIndex: 0, BlockIndex: 16, DeltaBytes: new sbyte[16]),
            });
        var channel = new W3dAdaptiveDeltaAnimationChannel(
            NumTimeCodes: 3,
            Pivot: 4,
            VectorLength: 1,
            ChannelType: W3dAnimationChannelType.TranslationZ,
            Scale: 16.0f,
            Data: data);

        var imported = W3dAnimationImporter.Convert(CreateFile(new W3dCompressedAnimation(
            new W3dCompressedAnimationHeader(0, "Run", "HV", NumFrames: 3, FrameRate: 5, W3dCompressedAnimationFlavor.AdaptiveDelta8),
            [],
            [channel],
            [],
            [])));

        var clip = Assert.Single(imported!.Channels);
        Assert.Equal(4, clip.BoneIndex);
        Assert.Equal(ImportedAnimationChannelType.TranslationZ, clip.ChannelType);
        Assert.Equal(3, clip.Keyframes.Count);

        var expected = W3dAdaptiveDeltaCodec.Decode(data, 3, 16.0f);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(TimeSpan.FromSeconds(i / 5.0), clip.Keyframes[i].Time);
            Assert.Equal(expected[i].FloatValue, clip.Keyframes[i].FloatValue);
        }
    }

    [Fact]
    public void MotionChannel_Converts()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 8);
        var channel = new W3dMotionChannel(
            Pivot: 5,
            DeltaType: W3dMotionChannelDeltaType.TimeCoded,
            VectorLength: 4,
            ChannelType: W3dAnimationChannelType.Quaternion,
            NumTimeCodes: 2,
            Data: new W3dMotionChannelTimeCodedData(
                TimeCodes: new ushort[] { 0, 5 },
                Values: new[] { new W3dAnimationChannelDatum(Quaternion.Identity), new W3dAnimationChannelDatum(rotation) }));

        var imported = W3dAnimationImporter.Convert(CreateFile(new W3dCompressedAnimation(
            new W3dCompressedAnimationHeader(0, "Run", "HV", NumFrames: 10, FrameRate: 5, W3dCompressedAnimationFlavor.TimeCoded),
            [],
            [],
            [],
            [channel])));

        var clip = Assert.Single(imported!.Channels);
        Assert.Equal(5, clip.BoneIndex);
        Assert.Equal(ImportedAnimationChannelType.Quaternion, clip.ChannelType);
        Assert.Equal(2, clip.Keyframes.Count);
        Assert.Equal(TimeSpan.Zero, clip.Keyframes[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(1.0), clip.Keyframes[1].Time);
        Assert.Equal(rotation, clip.Keyframes[1].QuaternionValue);
    }

    [Fact]
    public void UnknownBfmeChannels_AreSkipped()
    {
        var channel = new W3dAnimationChannel(
            FirstFrame: 0,
            LastFrame: 0,
            VectorLength: 1,
            ChannelType: W3dAnimationChannelType.UnknownBfme,
            Pivot: 0,
            Unknown: 0,
            Data: new[] { new W3dAnimationChannelDatum(1.0f) },
            NumPadBytes: 0);

        var imported = W3dAnimationImporter.Convert(CreateFile(new W3dAnimation(CreateUncompressedHeader(), [channel])));

        Assert.Empty(imported!.Channels);
    }

    [Fact]
    public void NoAnimationChunk_ReturnsNull_And_MultipleChunks_Throw()
    {
        Assert.Null(W3dAnimationImporter.Convert(CreateFile()));

        var animation = new W3dAnimation(CreateUncompressedHeader(), []);
        Assert.Throws<NotSupportedException>(() =>
            W3dAnimationImporter.Convert(CreateFile(animation, animation)));
    }

    [Fact]
    public void ImportAnimation_ResolvesFileThroughPathResolver()
    {
        var channel = new W3dAnimationChannel(
            FirstFrame: 0,
            LastFrame: 0,
            VectorLength: 1,
            ChannelType: W3dAnimationChannelType.TranslationX,
            Pivot: 0,
            Unknown: 0,
            Data: new[] { new W3dAnimationChannelDatum(1.0f) },
            NumPadBytes: 0);
        var w3dFile = CreateFile(new W3dAnimation(CreateUncompressedHeader(), [channel]));

        using var stream = new MemoryStream();
        w3dFile.WriteTo(stream);
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("art/w3d/Run.w3d", stream.ToArray());

        var importer = new W3dAnimationImporter(PathResolvers.W3d);
        var context = new AssetImportContext(fileSystem, "english");

        var imported = importer.ImportAnimation("HV.Run", context);

        Assert.NotNull(imported);
        Assert.Equal("HV.Run", imported!.Name);
    }

    [Fact]
    public void ImportAnimation_ReturnsNullForDotlessKeyAndMissingFile()
    {
        var importer = new W3dAnimationImporter(PathResolvers.W3d);
        var context = new AssetImportContext(new MemoryFileSystem(), "english");

        Assert.Null(importer.ImportAnimation("NoDotKey", context));
        Assert.Null(importer.ImportAnimation("HV.Missing", context));
    }
}
