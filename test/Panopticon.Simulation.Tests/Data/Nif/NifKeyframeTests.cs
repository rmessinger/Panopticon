using System;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.Nif;
using Xunit;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifKeyframeTests
{
    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    private static NifTestDataBuilder FullClipBuilder()
    {
        var builder = new NifTestDataBuilder();
        var sequenceType = builder.AddBlockType("NiControllerSequence");
        var textKeyType = builder.AddBlockType("NiTextKeyExtraData");
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");

        builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
            "EMPTY_SEQUENCE_AT_0f",
            "ROOT",
            1,
            ("R_OAR02", 2), ("ROOT NonAccum", 4)));
        builder.AddBlock(textKeyType, NifTestKeyframe.TextKeyExtraData(
            "",
            (0.0f, "start"), (2.1667f, "end")));
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(
            data: 3, stopTime: 2.1667f));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 0.7071f, 0.0f, 0.7071f, 0.0f), (1.0f, 0.0f, 0.0f, 1.0f, 0.0f)],
            translationKeys: [(0.0f, (1, 2, 3), null, null), (1.0f, (4, 5, 6), null, null)],
            scaleKeys: [(0.0f, 1.0f, null, null)]));
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(
            data: 5, stopTime: 2.1667f));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            xyzRotationOrder: 0.0f,
            xRotationKeys: [(0.0f, 2.3865f, 0.5f, -0.25f), (1.0f, 2.0f, 0.1f, -0.1f)],
            yRotationKeys: [(0.0f, 0.5080f, 0.1f, -0.05f)],
            zRotationKeys: [(0.0f, -1.1135f, 0.2f, -0.1f), (1.0f, -1.0f, 0.05f, -0.05f)]));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder;
    }

    [Fact]
    public void KfClipParsesToExactEndOfFile()
    {
        var file = Read(FullClipBuilder().Build());

        Assert.Equal(6, file.Blocks.Count);
        // The root-list footer points at the controller sequence.
        Assert.Equal([0], file.Roots);

        var sequence = (NifControllerSequence)file.Blocks[0];
        Assert.Equal("EMPTY_SEQUENCE_AT_0f", sequence.Name);
        Assert.Equal("ROOT", sequence.AccumulationRootName);
        Assert.Equal(1, sequence.TextKeysReference);
        Assert.Equal(2, sequence.ControlledBlocks.Count);
        Assert.Equal("R_OAR02", sequence.ControlledBlocks[0].TargetName);
        Assert.Equal(2, sequence.ControlledBlocks[0].ControllerReference);
        Assert.Equal("ROOT NonAccum", sequence.ControlledBlocks[1].TargetName);
        Assert.Equal(4, sequence.ControlledBlocks[1].ControllerReference);

        var textKeys = (NifTextKeyExtraData)file.Blocks[1];
        Assert.Equal("", textKeys.Name);
        Assert.Equal(2, textKeys.Keys.Count);
        Assert.Equal(0.0f, textKeys.Keys[0].Time);
        Assert.Equal("start", textKeys.Keys[0].Value);
        Assert.Equal(2.1667f, textKeys.Keys[1].Time);
        Assert.Equal("end", textKeys.Keys[1].Value);

        var controller = (NifKeyframeController)file.Blocks[2];
        Assert.Equal(-1, controller.NextControllerReference);
        Assert.Equal(0x2C, controller.Flags);
        Assert.Equal(1.0f, controller.Frequency);
        Assert.Equal(0.0f, controller.Phase);
        Assert.Equal(0.0f, controller.StartTime);
        Assert.Equal(2.1667f, controller.StopTime);
        Assert.Equal(-1, controller.TargetReference);
        Assert.Equal(3, controller.DataReference);
    }

    [Fact]
    public void LinearQuaternionRotationAndGroupsRoundTrip()
    {
        var file = Read(FullClipBuilder().Build());
        var data = (NifKeyframeData)file.Blocks[3];

        Assert.NotNull(data.QuaternionRotationKeys);
        Assert.Equal(NifKeyType.Linear, data.QuaternionRotationKeys.Interpolation);
        Assert.Equal(2, data.QuaternionRotationKeys.Keys.Count);
        Assert.Equal(0.0f, data.QuaternionRotationKeys.Keys[0].Time);
        Assert.Equal(
            new Quaternion(0.0f, 0.7071f, 0.0f, 0.7071f),
            data.QuaternionRotationKeys.Keys[0].Value);
        Assert.Equal(1.0f, data.QuaternionRotationKeys.Keys[1].Time);
        Assert.Equal(
            new Quaternion(0.0f, 1.0f, 0.0f, 0.0f),
            data.QuaternionRotationKeys.Keys[1].Value);
        Assert.Null(data.XyzRotationOrder);
        Assert.Null(data.XRotationKeys);

        Assert.NotNull(data.TranslationKeys);
        Assert.Equal(NifKeyType.Linear, data.TranslationKeys.Interpolation);
        Assert.Equal(2, data.TranslationKeys.Keys.Count);
        Assert.Equal(new Vector3(1, 2, 3), data.TranslationKeys.Keys[0].Value);
        Assert.Null(data.TranslationKeys.Keys[0].Forward);
        Assert.Null(data.TranslationKeys.Keys[0].Backward);
        Assert.Equal(new Vector3(4, 5, 6), data.TranslationKeys.Keys[1].Value);

        Assert.NotNull(data.ScaleKeys);
        Assert.Equal(NifKeyType.Linear, data.ScaleKeys.Interpolation);
        Assert.Equal(1.0f, data.ScaleKeys.Keys[0].Value);
    }

    [Fact]
    public void XyzRotationKeepsQuadraticTangentsExactly()
    {
        var file = Read(FullClipBuilder().Build());
        var data = (NifKeyframeData)file.Blocks[5];

        Assert.Null(data.QuaternionRotationKeys);
        Assert.Equal(0.0f, data.XyzRotationOrder);

        Assert.NotNull(data.XRotationKeys);
        Assert.Equal(NifKeyType.Quadratic, data.XRotationKeys.Interpolation);
        Assert.Equal(2, data.XRotationKeys.Keys.Count);
        var first = data.XRotationKeys.Keys[0];
        Assert.Equal(0.0f, first.Time);
        Assert.Equal(2.3865f, first.Value);
        Assert.Equal(0.5f, first.Forward);
        Assert.Equal(-0.25f, first.Backward);
        var second = data.XRotationKeys.Keys[1];
        Assert.Equal(2.0f, second.Value);
        Assert.Equal(0.1f, second.Forward);
        Assert.Equal(-0.1f, second.Backward);

        Assert.NotNull(data.YRotationKeys);
        Assert.Single(data.YRotationKeys.Keys);
        Assert.Equal(0.5080f, data.YRotationKeys.Keys[0].Value);

        Assert.NotNull(data.ZRotationKeys);
        Assert.Equal(2, data.ZRotationKeys.Keys.Count);
        Assert.Equal(-1.1135f, data.ZRotationKeys.Keys[0].Value);

        // Translation and scale groups are empty for this block.
        Assert.Null(data.TranslationKeys);
        Assert.Null(data.ScaleKeys);
    }

    [Fact]
    public void EmptyKeyframeDataYieldsNoGroups()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(data: 1));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData());
        builder.AddTrailing(NifTestBlocks.Footer());

        var file = Read(builder.Build());
        var data = (NifKeyframeData)file.Blocks[1];
        Assert.Null(data.QuaternionRotationKeys);
        Assert.Null(data.XyzRotationOrder);
        Assert.Null(data.XRotationKeys);
        Assert.Null(data.TranslationKeys);
        Assert.Null(data.ScaleKeys);
    }

    [Fact]
    public void BadTextKeysReferenceThrows()
    {
        var builder = FullClipBuilder();
        // Point the sequence's text keys at a keyframe controller.
        builder.AddBlock(0, NifTestKeyframe.ControllerSequence(
            "EMPTY_SEQUENCE_AT_0f",
            "ROOT",
            2,
            ("R_OAR02", 2)));

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("as text keys", ex.Message);
    }

    [Fact]
    public void BadControllerReferenceThrows()
    {
        var builder = FullClipBuilder();
        // Point a controlled block at the text-key block instead of a controller.
        builder.AddBlock(0, NifTestKeyframe.ControllerSequence(
            "EMPTY_SEQUENCE_AT_0f",
            "ROOT",
            1,
            ("R_OAR02", 1)));

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("as a keyframe controller", ex.Message);
    }

    [Fact]
    public void BadDataReferenceThrows()
    {
        var builder = FullClipBuilder();
        // Point the first controller at the sequence instead of keyframe data.
        builder.AddBlock(2, NifTestKeyframe.KeyframeController(data: 0, stopTime: 2.1667f));

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("as data", ex.Message);
    }

    [Fact]
    public void TbcKeysParse()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(data: 1));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            translationKeys: [(0.0f, (1, 2, 3), null, null), (1.0f, (4, 5, 6), null, null)],
            translationKeyTypeOverride: 3));
        builder.AddTrailing(NifTestBlocks.Footer());

        var file = Read(builder.Build());
        var data = Assert.IsType<NifKeyframeData>(file.Blocks[1]);
        Assert.NotNull(data.TranslationKeys);
        Assert.Equal(NifKeyType.Tbc, data.TranslationKeys!.Interpolation);
        Assert.Equal(2, data.TranslationKeys.Keys.Count);
        Assert.Equal(new Vector3(1, 2, 3), data.TranslationKeys.Keys[0].Value);
        Assert.Null(data.TranslationKeys.Keys[0].Forward);
    }

    [Fact]
    public void ConstantKeysAreRejected()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(data: 1));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            translationKeys: [(0.0f, (1, 2, 3), null, null)],
            translationKeyTypeOverride: 5));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));
        Assert.Contains("Constant", ex.Message);
    }

    [Fact]
    public void UnknownKeyTypeIsRejected()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(data: 1));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            translationKeys: [(0.0f, (1, 2, 3), null, null)],
            translationKeyTypeOverride: 9));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));
        Assert.Contains("9", ex.Message);
    }

    [Fact]
    public void QuadraticQuaternionRotationIsRejected()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(data: 1));
        builder.AddBlock(dataType, NifTestKeyframe.KeyframeData(
            quaternionRotationKeys: [(0.0f, 1.0f, 0.0f, 0.0f, 0.0f)],
            rotationKeyTypeOverride: 2));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));
        Assert.Contains("Linear (1), Tbc (3), and XyzRotation (4)", ex.Message);
    }
}
