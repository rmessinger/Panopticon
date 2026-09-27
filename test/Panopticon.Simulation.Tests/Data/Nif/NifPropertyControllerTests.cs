using System;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.Nif;
using Xunit;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifPropertyControllerTests
{
    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    private static NifTestDataBuilder VisClipBuilder()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiVisController");
        var dataType = builder.AddBlockType("NiVisData");

        builder.AddBlock(controllerType, NifTestPropertyAnimation.VisController(
            data: 1, flags: 0x25, stopTime: 0.033333f));
        builder.AddBlock(dataType, NifTestPropertyAnimation.VisData(
            (0.0f, 0), (0.033333f, 1)));
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder;
    }

    [Fact]
    public void VisControllerAndVisDataRoundTripExactly()
    {
        var file = Read(VisClipBuilder().Build());

        Assert.Equal(2, file.Blocks.Count);
        var controller = (NifVisController)file.Blocks[0];
        Assert.Equal(-1, controller.NextControllerReference);
        Assert.Equal(0x25, controller.Flags);
        Assert.Equal(1.0f, controller.Frequency);
        Assert.Equal(0.0f, controller.Phase);
        Assert.Equal(0.0f, controller.StartTime);
        Assert.Equal(0.033333f, controller.StopTime);
        Assert.Equal(-1, controller.TargetReference);
        Assert.Equal(1, controller.DataReference);

        var data = (NifVisData)file.Blocks[1];
        Assert.Equal(2, data.Keys.Count);
        Assert.Equal(0.0f, data.Keys[0].Time);
        Assert.Equal(0, data.Keys[0].Visible);
        Assert.Equal(0.033333f, data.Keys[1].Time);
        Assert.Equal(1, data.Keys[1].Visible);
    }

    [Fact]
    public void VisControllerAcceptsBoolDataReplacementForm()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiVisController");
        var dataType = builder.AddBlockType("NiBoolData");
        builder.AddBlock(controllerType, NifTestPropertyAnimation.VisController(data: 1));
        builder.AddBlock(dataType, NifTestPropertyAnimation.BoolData([(0.0f, 0), (1.0f, 1)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var file = Read(builder.Build());
        var data = (NifBoolData)file.Blocks[1];
        Assert.NotNull(data.Keys);
        Assert.Equal(NifKeyType.Linear, data.Keys.Interpolation);
        Assert.Equal(2, data.Keys.Keys.Count);
        Assert.Equal(1, data.Keys.Keys[1].Value);
    }

    [Fact]
    public void BoolDataPreservesConstantKeys()
    {
        var builder = new NifTestDataBuilder();
        var dataType = builder.AddBlockType("NiBoolData");
        builder.AddBlock(dataType, NifTestPropertyAnimation.BoolData(
            [(0.0f, 1)], keyTypeOverride: 5u));
        builder.AddTrailing(NifTestBlocks.Footer());

        var data = (NifBoolData)Read(builder.Build()).Blocks[0];
        Assert.NotNull(data.Keys);
        Assert.Equal(NifKeyType.Constant, data.Keys.Interpolation);
        Assert.Equal(1, data.Keys.Keys[0].Value);
    }

    [Fact]
    public void BoolDataRejectsTbcKeys()
    {
        var builder = new NifTestDataBuilder();
        var dataType = builder.AddBlockType("NiBoolData");
        builder.AddBlock(dataType, NifTestPropertyAnimation.BoolData(
            [(0.0f, 1)], keyTypeOverride: 3u));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));
        Assert.Contains("NiBoolData", ex.Message);
        Assert.Contains("unsupported key type Tbc (3)", ex.Message);
    }

    [Fact]
    public void UnknownByteKeyTypeIsRejected()
    {
        var builder = new NifTestDataBuilder();
        var dataType = builder.AddBlockType("NiBoolData");
        builder.AddBlock(dataType, NifTestPropertyAnimation.BoolData(
            [(0.0f, 1)], keyTypeOverride: 99u));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));
        Assert.Contains("unknown key type 99", ex.Message);
    }

    [Fact]
    public void FloatDataRoundTripsQuadraticKeys()
    {
        var builder = new NifTestDataBuilder();
        var dataType = builder.AddBlockType("NiFloatData");
        builder.AddBlock(dataType, NifTestPropertyAnimation.FloatData(
            [(0.0f, 0.5f, 0.25f, -0.25f), (1.0f, 1.5f, null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var data = (NifFloatData)Read(builder.Build()).Blocks[0];
        Assert.NotNull(data.Keys);
        Assert.Equal(NifKeyType.Quadratic, data.Keys.Interpolation);
        Assert.Equal(2, data.Keys.Keys.Count);
        Assert.Equal(0.5f, data.Keys.Keys[0].Value);
        Assert.Equal(0.25f, data.Keys.Keys[0].Forward);
        Assert.Equal(-0.25f, data.Keys.Keys[0].Backward);
        Assert.Equal(1.5f, data.Keys.Keys[1].Value);
    }

    [Fact]
    public void FloatDataRejectsTbcKeys()
    {
        var builder = new NifTestDataBuilder();
        var dataType = builder.AddBlockType("NiFloatData");
        builder.AddBlock(dataType, NifTestPropertyAnimation.FloatData(
            [(0.0f, 0.5f, null, null)], keyTypeOverride: 3u));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<NotSupportedException>(() => Read(builder.Build()));
        Assert.Contains("unsupported key type Tbc (3)", ex.Message);
    }

    [Fact]
    public void PosDataRoundTripsQuadraticColorsWithoutClamping()
    {
        var builder = new NifTestDataBuilder();
        var dataType = builder.AddBlockType("NiPosData");
        builder.AddBlock(dataType, NifTestPropertyAnimation.PosData(
            [(0.0f, (0.25f, 0.5f, 0.75f), (0.1f, 0.1f, 0.1f), (0.2f, 0.2f, 0.2f)),
             (1.0f, (1.5f, 0.0f, 0.0f), null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var data = (NifPosData)Read(builder.Build()).Blocks[0];
        Assert.NotNull(data.Keys);
        Assert.Equal(NifKeyType.Quadratic, data.Keys.Interpolation);
        Assert.Equal(2, data.Keys.Keys.Count);
        Assert.Equal(new Vector3(0.25f, 0.5f, 0.75f), data.Keys.Keys[0].Value);
        Assert.Equal(new Vector3(0.1f, 0.1f, 0.1f), data.Keys.Keys[0].Forward);
        Assert.Equal(new Vector3(1.5f, 0.0f, 0.0f), data.Keys.Keys[1].Value);
    }

    [Fact]
    public void UVDataReadsFourFloatGroups()
    {
        var builder = new NifTestDataBuilder();
        var dataType = builder.AddBlockType("NiUVData");
        builder.AddBlock(dataType, NifTestPropertyAnimation.UVData(
            uTranslationKeys: [(0.0f, 0.0f, null, null), (4.0f, 1.5f, null, null)],
            uScaleKeys: [(0.0f, 1.0f, null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var data = (NifUVData)Read(builder.Build()).Blocks[0];
        Assert.NotNull(data.UTranslationKeys);
        Assert.Equal(2, data.UTranslationKeys.Keys.Count);
        Assert.Equal(1.5f, data.UTranslationKeys.Keys[1].Value);
        Assert.Null(data.VTranslationKeys);
        Assert.NotNull(data.UScaleKeys);
        Assert.Equal(1.0f, data.UScaleKeys.Keys[0].Value);
        Assert.Null(data.VScaleKeys);
    }

    [Fact]
    public void TextureTransformControllerCoversAllOperationValues()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiTextureTransformController");
        var dataType = builder.AddBlockType("NiFloatData");

        var operations = new[]
        {
            NifTextureTransformOp.TranslateU,
            NifTextureTransformOp.TranslateV,
            NifTextureTransformOp.Rotate,
            NifTextureTransformOp.ScaleU,
            NifTextureTransformOp.ScaleV,
        };

        for (var i = 0; i < operations.Length; i++)
        {
            builder.AddBlock(controllerType, NifTestPropertyAnimation.TextureTransformController(
                data: i * 2 + 1,
                operation: (uint)operations[i],
                flags: i == 3 ? (ushort)0x28 : (ushort)0x2C));
            builder.AddBlock(dataType, NifTestPropertyAnimation.FloatData(
                [(0.0f, i * 0.1f, null, null), (1.0f, i * 0.2f, null, null)]));
        }

        builder.AddTrailing(NifTestBlocks.Footer());
        var file = Read(builder.Build());
        Assert.Equal(10, file.Blocks.Count);

        for (var i = 0; i < operations.Length; i++)
        {
            var controller = (NifTextureTransformController)file.Blocks[i * 2];
            Assert.Equal(operations[i], controller.Operation);
            Assert.False(controller.ShaderMap);
            Assert.Equal(0u, controller.TextureSlot);
            Assert.Equal(i * 2 + 1, controller.DataReference);
        }
    }

    [Fact]
    public void MaterialColorControllerCoversAllTargetValues()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiMaterialColorController");
        var dataType = builder.AddBlockType("NiPosData");

        var targets = new[]
        {
            NifMaterialColorTarget.Ambient,
            NifMaterialColorTarget.Diffuse,
            NifMaterialColorTarget.Specular,
            NifMaterialColorTarget.Emissive,
        };

        for (var i = 0; i < targets.Length; i++)
        {
            builder.AddBlock(controllerType, NifTestPropertyAnimation.MaterialColorController(
                data: i * 2 + 1,
                targetColor: (ushort)targets[i]));
            builder.AddBlock(dataType, NifTestPropertyAnimation.PosData(
                [(0.0f, (i * 0.1f, 0.2f, 0.3f), null, null)]));
        }

        builder.AddTrailing(NifTestBlocks.Footer());
        var file = Read(builder.Build());
        Assert.Equal(8, file.Blocks.Count);

        for (var i = 0; i < targets.Length; i++)
        {
            var controller = (NifMaterialColorController)file.Blocks[i * 2];
            Assert.Equal(targets[i], controller.TargetColor);
            Assert.Equal(i * 2 + 1, controller.DataReference);
        }
    }

    [Fact]
    public void AlphaControllerPreservesPrefixVerbatim()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiAlphaController");
        var dataType = builder.AddBlockType("NiFloatData");
        builder.AddBlock(controllerType, NifTestPropertyAnimation.AlphaController(
            data: 1, flags: 0x28, stopTime: 4.0f));
        builder.AddBlock(dataType, NifTestPropertyAnimation.FloatData(
            [(0.0f, 1.0f, 0.0f, 0.0f), (4.0f, 0.0f, -0.25f, 0.25f)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var controller = (NifAlphaController)Read(builder.Build()).Blocks[0];
        Assert.Equal(0x28, controller.Flags);
        Assert.Equal(1.0f, controller.Frequency);
        Assert.Equal(4.0f, controller.StopTime);
        Assert.Equal(-1, controller.NextControllerReference);
        Assert.Equal(1, controller.DataReference);
    }

    [Fact]
    public void VisControllerDataMustTargetVisibilityData()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiVisController");
        var dataType = builder.AddBlockType("NiFloatData");
        builder.AddBlock(controllerType, NifTestPropertyAnimation.VisController(data: 1));
        builder.AddBlock(dataType, NifTestPropertyAnimation.FloatData([(0.0f, 1.0f, null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("as visibility data", ex.Message);
    }

    [Fact]
    public void AlphaControllerDataMustTargetFloatData()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiAlphaController");
        var dataType = builder.AddBlockType("NiPosData");
        builder.AddBlock(controllerType, NifTestPropertyAnimation.AlphaController(data: 1));
        builder.AddBlock(dataType, NifTestPropertyAnimation.PosData([(0.0f, (1, 1, 1), null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("as float data", ex.Message);
    }

    [Fact]
    public void TextureTransformControllerDataMustTargetFloatData()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiTextureTransformController");
        var dataType = builder.AddBlockType("NiPosData");
        builder.AddBlock(controllerType, NifTestPropertyAnimation.TextureTransformController(data: 1));
        builder.AddBlock(dataType, NifTestPropertyAnimation.PosData([(0.0f, (1, 1, 1), null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("as float data", ex.Message);
    }

    [Fact]
    public void MaterialColorControllerDataMustTargetPosData()
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiMaterialColorController");
        var dataType = builder.AddBlockType("NiFloatData");
        builder.AddBlock(controllerType, NifTestPropertyAnimation.MaterialColorController(data: 1));
        builder.AddBlock(dataType, NifTestPropertyAnimation.FloatData([(0.0f, 1.0f, null, null)]));
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("as position data", ex.Message);
    }

    [Fact]
    public void ControllerSequenceAcceptsPropertyControllers()
    {
        var builder = new NifTestDataBuilder();
        var sequenceType = builder.AddBlockType("NiControllerSequence");
        var textKeyType = builder.AddBlockType("NiTextKeyExtraData");
        var controllerType = builder.AddBlockType("NiVisController");
        var dataType = builder.AddBlockType("NiVisData");

        builder.AddBlock(sequenceType, NifTestKeyframe.ControllerSequence(
            "bld_market8_open",
            "Scene Root",
            1,
            ("Object04", 2)));
        builder.AddBlock(textKeyType, NifTestKeyframe.TextKeyExtraData(
            "", (0.0f, "start"), (0.033333f, "end")));
        builder.AddBlock(controllerType, NifTestPropertyAnimation.VisController(data: 3));
        builder.AddBlock(dataType, NifTestPropertyAnimation.VisData((0.0f, 0), (0.033333f, 1)));
        builder.AddTrailing(NifTestBlocks.Footer());

        var file = Read(builder.Build());
        var sequence = (NifControllerSequence)file.Blocks[0];
        Assert.Equal("Object04", sequence.ControlledBlocks[0].TargetName);
        Assert.Equal(2, sequence.ControlledBlocks[0].ControllerReference);
        Assert.IsType<NifVisController>(file.Blocks[2]);
    }
}
