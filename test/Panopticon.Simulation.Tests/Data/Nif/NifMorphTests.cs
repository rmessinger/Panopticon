using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.FileFormats.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;
using NifTestKeyframe = Panopticon.Tests.Data.Nif.NifTestKeyframe;
using NifTestPropertyAnimation = Panopticon.Tests.Data.Nif.NifTestPropertyAnimation;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifMorphTests
{
    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    private static byte[] MorphFileBytes(
        byte[]? controller = null,
        byte[]? morphData = null)
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var controllerType = builder.AddBlockType("NiGeomMorpherController");
        var morphDataType = builder.AddBlockType("NiMorphData");

        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", children: [1]));
        builder.AddBlock(controllerType, controller ?? NifTestPropertyAnimation.MorphController(
            data: 2, stopTime: 0.9666667f));
        builder.AddBlock(morphDataType, morphData ?? FarmYamShapedMorphData());
        builder.AddTrailing(NifTestBlocks.Footer());
        return builder.Build();
    }

    private static byte[] FarmYamShapedMorphData()
    {
        List<(float X, float Y, float Z)> baseVectors = [];
        List<(float X, float Y, float Z)> deltaVectors = [];
        for (var i = 0; i < 720; i++)
        {
            baseVectors.Add((i * 0.01f, i * 0.02f, i * 0.03f));
            deltaVectors.Add(i < 600 ? (i * 0.005f + 0.001f, 0, 0) : (0, 0, 0));
        }

        return NifTestPropertyAnimation.MorphData(
            720,
            [
                ([(0.0f, 0.0f, null, null), (0.4833333f, 0.0f, null, null), (0.9666667f, 0.0f, null, null)], baseVectors),
                ([(0.0f, 0.0f, null, null), (0.9666667f, 1.0f, null, null)], deltaVectors),
            ]);
    }

    [Fact]
    public void FarmYamShapedMorphBlocksRoundTrip()
    {
        var file = Read(MorphFileBytes());

        var controller = Assert.IsType<NifGeomMorpherController>(file.Blocks[1]);
        Assert.Equal((ushort)0x2C, controller.Flags);
        Assert.Equal(1.0f, controller.Frequency);
        Assert.Equal(0.0f, controller.Phase);
        Assert.Equal(0.0f, controller.StartTime);
        Assert.Equal(0.9666667f, controller.StopTime, 5);
        Assert.Equal(-1, controller.TargetReference);
        Assert.Equal(-1, controller.NextControllerReference);
        Assert.Equal((ushort)0, controller.MorpherFlags);
        Assert.Equal(2, controller.DataReference);
        Assert.Equal((byte)0, controller.AlwaysUpdate);

        var morphData = Assert.IsType<NifMorphData>(file.Blocks[2]);
        Assert.Equal(2u, morphData.NumMorphs);
        Assert.Equal(720u, morphData.NumVertices);
        Assert.True(morphData.RelativeTargets);
        Assert.Equal(2, morphData.Morphs.Count);

        var baseMorph = morphData.Morphs[0];
        Assert.NotNull(baseMorph.WeightKeys);
        Assert.Equal(NifKeyType.Linear, baseMorph.WeightKeys!.Interpolation);
        Assert.Equal(3, baseMorph.WeightKeys.Keys.Count);
        Assert.All(baseMorph.WeightKeys.Keys, key => Assert.Equal(0.0f, key.Value));
        Assert.Equal(7.19f, baseMorph.Vectors[719].X, 3);

        var deltaMorph = morphData.Morphs[1];
        Assert.NotNull(deltaMorph.WeightKeys);
        Assert.Equal(2, deltaMorph.WeightKeys!.Keys.Count);
        Assert.Equal(0.0f, deltaMorph.WeightKeys.Keys[0].Value);
        Assert.Equal(1.0f, deltaMorph.WeightKeys.Keys[1].Value);
        Assert.Equal(0.9666667f, deltaMorph.WeightKeys.Keys[1].Time, 5);
        Assert.Equal(600, CountNonzero(deltaMorph.Vectors));
    }

    [Fact]
    public void ControllerDataRefMustTargetMorphData()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read(MorphFileBytes(
            controller: NifTestPropertyAnimation.MorphController(data: 0, stopTime: 0.9666667f))));

        Assert.Contains("as morph data", ex.Message);
    }

    [Fact]
    public void TbcWeightKeysParseWithNullTangents()
    {
        var file = Read(MorphFileBytes(
            morphData: NifTestPropertyAnimation.MorphData(
                1,
                [([(0.0f, 0.0f, null, null), (1.0f, 0.5f, null, null)], null)],
                keyTypeOverride: 3)));

        var morphData = Assert.IsType<NifMorphData>(file.Blocks[2]);
        var morph = Assert.Single(morphData.Morphs);
        Assert.NotNull(morph.WeightKeys);
        Assert.Equal(NifKeyType.Tbc, morph.WeightKeys!.Interpolation);
        Assert.Equal(2, morph.WeightKeys.Keys.Count);
        Assert.Equal(0.5f, morph.WeightKeys.Keys[1].Value);
        Assert.Null(morph.WeightKeys.Keys[1].Forward);
    }

    [Fact]
    public void TruncatedVectorsFailNamingMorph()
    {
        // The builder pads vectors to the declared count, so a short body is
        // written by hand: 720 declared, only 3 present.
        var stream = new MemoryStream();
        var body = new NifBodyWriter(stream);
        body.WriteUInt32(0);
        body.WriteUInt32(1);
        body.WriteUInt32(720);
        body.WriteByte(1);
        NifTestKeyframe.WriteFloatGroup(body, [(0.0f, 0.0f, null, null)]);
        NifTestBlocks.WriteVector3(body, (1, 2, 3));
        NifTestBlocks.WriteVector3(body, (4, 5, 6));
        NifTestBlocks.WriteVector3(body, (7, 8, 9));

        var ex = Assert.Throws<InvalidDataException>(() => Read(MorphFileBytes(
            morphData: stream.ToArray())));

        Assert.Contains("morph 0 declares 720 vertex vectors", ex.Message);
    }

    [Fact]
    public void RelativeTargetsAndAlwaysUpdateDecodeVerbatim()
    {
        var file = Read(MorphFileBytes(
            controller: NifTestPropertyAnimation.MorphController(data: 2, alwaysUpdate: 1),
            morphData: NifTestPropertyAnimation.MorphData(
                1,
                [([(0.0f, 0.5f, null, null)], [(1, 2, 3)])],
                relativeTargets: 0)));

        var controller = Assert.IsType<NifGeomMorpherController>(file.Blocks[1]);
        Assert.Equal((byte)1, controller.AlwaysUpdate);
        var morphData = Assert.IsType<NifMorphData>(file.Blocks[2]);
        Assert.False(morphData.RelativeTargets);
        Assert.Equal(0.5f, morphData.Morphs[0].WeightKeys!.Keys[0].Value);
    }

    private static int CountNonzero(IReadOnlyList<System.Numerics.Vector3> vectors)
    {
        var count = 0;
        foreach (var vector in vectors)
        {
            if (vector != System.Numerics.Vector3.Zero)
            {
                count++;
            }
        }

        return count;
    }
}
