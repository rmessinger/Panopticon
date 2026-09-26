using System;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.M3;
using Panopticon.Tests.Data.M3;
using Xunit;

namespace Panopticon.Tests.Data.M3;

public sealed class M3FileTests
{
    [Fact]
    public void ReadsSyntheticModelFields()
    {
        var file = Read(new M3TestDataBuilder().Build());

        Assert.Equal("SyntheticModel", file.Model.Name);
        Assert.Equal((uint)0x0186007D, file.Model.VertexFlags);
        Assert.Equal(36, file.Model.VertexFormat.Stride);
        Assert.True(file.Model.VertexFormat.HasUv1);
        Assert.Equal(36, file.Model.VertexBytes.Count);
        Assert.Equal(new ushort[] { 0 }, file.Model.BoneLookup);

        var bone = Assert.Single(file.Model.Bones);
        Assert.Equal("Root", bone.Name);
        Assert.Equal((uint)0x12345, bone.Flags);
        Assert.Equal(-1, bone.Parent);
        Assert.Equal(new M3AnimationHeader(1, 2, 100), bone.Location.Header);
        Assert.Equal(new Vector3(1, 2, 3), bone.Location.DefaultValue);
        Assert.Equal(new M3AnimationHeader(1, 4, 101), bone.Rotation.Header);
        Assert.Equal(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), bone.Rotation.DefaultValue);
        Assert.Equal(new M3AnimationHeader(1, 8, 102), bone.Scale.Header);
        Assert.Equal(new Vector3(4, 5, 6), bone.Scale.DefaultValue);

        var division = Assert.Single(file.Model.Divisions);
        Assert.Equal(new ushort[] { 0, 1, 2 }, division.Faces);
        var region = Assert.Single(division.Regions);
        Assert.Equal((uint)7, region.Id);
        Assert.Equal((uint)8, region.Unknown);
        Assert.Equal((uint)0, region.FirstVertex);
        Assert.Equal((uint)1, region.VertexCount);
        Assert.Equal((uint)0, region.FirstFaceIndex);
        Assert.Equal((uint)3, region.FaceIndexCount);
        Assert.Equal((ushort)1, region.BoneCount);
        Assert.Equal((ushort)0, region.FirstBoneLookup);
        Assert.Equal((ushort)1, region.BoneLookupCount);
        Assert.Equal((ushort)9, region.Unknown2);
        Assert.Equal((byte)4, region.VertexLookupsUsed);
        Assert.Equal((byte)10, region.Unknown3);
        Assert.Equal((ushort)0, region.RootBone);

        var batch = Assert.Single(division.Batches);
        Assert.Equal((ushort)0x11, batch.Flags);
        Assert.Equal((ushort)0x22, batch.Priority);
        Assert.Equal((ushort)0, batch.Region);
        Assert.Equal((ushort)0x33, batch.Bounds);
        Assert.Equal((ushort)0x44, batch.Color);
        Assert.Equal((ushort)0, batch.MaterialReference);
        Assert.Equal((short)-1, batch.Bone);

        var materialReference = Assert.Single(file.Model.MaterialReferences);
        Assert.Equal((uint)1, materialReference.Type);
        Assert.Equal((uint)0, materialReference.Index);

        var material = Assert.Single(file.Model.Materials);
        Assert.Equal("SyntheticMaterial", material.Name);
        Assert.Equal((uint)0x66, material.Flags);
        Assert.Equal((uint)0, material.BlendMode);
        Assert.Equal(80.0f, material.Specularity);
        Assert.Equal((uint)17, material.AlphaTestThreshold);
        Assert.Equal(13, material.Layers.Count);
        var layer = material.GetLayer(M3MaterialLayerSlot.Diffuse);
        Assert.NotNull(layer);
        layer = layer!;
        Assert.Equal("Assets/Textures/Synthetic_Diffuse.dds", layer.Path);
        Assert.Equal((uint)0x55, layer.Flags);
        Assert.Equal((uint)1, layer.UvSource);
        Assert.Equal((uint)2, layer.ColorChannelSelector);
    }

    [Fact]
    public void UnsupportedModelVersionNamesTagVersionAndStream()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Read(new M3TestDataBuilder { ModelVersion = 24 }.Build()));

        Assert.Contains("MODL", ex.Message);
        Assert.Contains("24", ex.Message);
        Assert.Contains("synthetic.m3", ex.Message);
    }

    [Fact]
    public void UnsupportedRegionVersionNamesTagVersionAndStream()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Read(new M3TestDataBuilder { RegionVersion = 4 }.Build()));

        Assert.Contains("REGN", ex.Message);
        Assert.Contains("4", ex.Message);
        Assert.Contains("synthetic.m3", ex.Message);
    }

    [Fact]
    public void UnsupportedVertexFlagsNameValue()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Read(new M3TestDataBuilder { VertexFlags = 0x0186007F }.Build()));

        Assert.Contains("0x0186007F", ex.Message);
    }

    [Fact]
    public void UnreferencedTagsAreIgnored()
    {
        var file = Read(new M3TestDataBuilder { AddUnreferencedTags = true }.Build());

        Assert.Equal("SyntheticModel", file.Model.Name);
        Assert.Contains(file.Index, entry => entry.Tag == "PAR_");
        Assert.Contains(file.Index, entry => entry.Tag == "RIB_");
        Assert.Contains(file.Index, entry => entry.Tag == "LITE");
    }

    [Fact]
    public void BadHeaderFails()
    {
        var bytes = new M3TestDataBuilder().Build();
        bytes[0] = 0;

        var ex = Assert.Throws<InvalidDataException>(() => Read(bytes));

        Assert.Contains("expected 43DM", ex.Message);
    }

    private static M3File Read(byte[] bytes)
    {
        return M3File.Read(new MemoryStream(bytes), "synthetic.m3");
    }
}
