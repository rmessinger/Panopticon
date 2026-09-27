using System;
using System.IO;
using Panopticon.FileFormats.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifPropertyTests
{
    private static NifFile Read(params (string TypeName, byte[] Body)[] blocks)
    {
        var builder = new NifTestDataBuilder();
        foreach (var (typeName, body) in blocks)
        {
            var type = builder.AddBlockType(typeName);
            builder.AddBlock(type, body);
        }

        builder.AddTrailing(NifTestBlocks.Footer());
        using var stream = new MemoryStream(builder.Build());
        return NifFile.Read(stream);
    }

    [Fact]
    public void MaterialPropertyRoundTrips()
    {
        var file = Read(("NiMaterialProperty", NifTestProperties.Material(
            ambient: (0.1f, 0.2f, 0.3f),
            diffuse: (0.4f, 0.5f, 0.6f),
            specular: (0.15f, 0.15f, 0.15f),
            emissive: (0, 0, 0),
            glossiness: 10.0f,
            alpha: 1.0f,
            name: "20 - Default")));

        var material = (NifMaterialProperty)file.Blocks[0];
        Assert.Equal("20 - Default", material.Name);
        Assert.Equal(new System.Numerics.Vector3(0.1f, 0.2f, 0.3f), material.AmbientColor);
        Assert.Equal(new System.Numerics.Vector3(0.4f, 0.5f, 0.6f), material.DiffuseColor);
        Assert.Equal(new System.Numerics.Vector3(0.15f, 0.15f, 0.15f), material.SpecularColor);
        Assert.Equal(10.0f, material.Glossiness);
        Assert.Equal(1.0f, material.Alpha);
    }

    [Fact]
    public void TexturingPropertyRoundTripsWithTargetShape()
    {
        // Target shape from C4a-01: count 7, only base enabled.
        var file = Read(
            ("NiSourceTexture", NifTestProperties.SourceTexture(fileName: "ab11_TwinEngineBomber.tga", alphaFormat: 2)),
            ("NiTexturingProperty", NifTestProperties.Texturing(
                baseSlot: (0, 3, 2, 0, 0, -115))));

        var texturing = (NifTexturingProperty)file.Blocks[1];
        Assert.Equal(4u, texturing.ApplyMode);
        Assert.Equal(7u, texturing.TextureCount);
        var slot = texturing.Slots["Base"];
        Assert.Equal(0, slot.SourceReference);
        Assert.Equal(3u, slot.ClampMode);
        Assert.Equal(2u, slot.FilterMode);
        Assert.Equal(0u, slot.UvSet);
        Assert.Equal((short)0, slot.Ps2L);
        Assert.Equal((short)-115, slot.Ps2K);
        Assert.Single(texturing.Slots);
    }

    [Fact]
    public void TexturingSlotPresenceFollowsTextureCount()
    {
        // Count 5: slots base..glow are present (disabled); bump is not read.
        var file = Read(("NiTexturingProperty", NifTestProperties.Texturing(textureCount: 5)));
        var texturing = (NifTexturingProperty)file.Blocks[0];
        Assert.Empty(texturing.Slots);

        // Count 8: decal0 is present in the stream.
        var file2 = Read(("NiTexturingProperty", NifTestProperties.Texturing(textureCount: 8)));
        Assert.Empty(((NifTexturingProperty)file2.Blocks[0]).Slots);
    }

    [Fact]
    public void SourceTextureRoundTrips()
    {
        var file = Read(("NiSourceTexture", NifTestProperties.SourceTexture(
            fileName: "nwg1_galley.tga", pixelLayout: 3, useMipmaps: 1, alphaFormat: 1)));
        var texture = (NifSourceTexture)file.Blocks[0];
        Assert.True(texture.UseExternal);
        Assert.Equal("nwg1_galley.tga", texture.FileName);
        Assert.Equal(-1, texture.PixelDataReference);
        Assert.Equal(3u, texture.PixelLayout);
        Assert.Equal(1u, texture.UseMipmaps);
        Assert.Equal(1u, texture.AlphaFormat);
        Assert.Equal(1, texture.IsStatic);
    }

    [Fact]
    public void SimplePropertiesRoundTrip()
    {
        var file = Read(
            ("NiAlphaProperty", NifTestProperties.Alpha(flags: 0x12ED, threshold: 100)),
            ("NiSpecularProperty", NifTestProperties.Specular(flags: 1)),
            ("NiZBufferProperty", NifTestProperties.ZBuffer(flags: 3, function: 3)),
            ("NiVertexColorProperty", NifTestProperties.VertexColor(flags: 0xCDCD, vertexMode: 0, lightingMode: 1)));

        Assert.Equal(0x12ED, ((NifAlphaProperty)file.Blocks[0]).Flags);
        Assert.Equal(100, ((NifAlphaProperty)file.Blocks[0]).Threshold);
        Assert.Equal(1, ((NifSpecularProperty)file.Blocks[1]).Flags);
        Assert.Equal(3, ((NifZBufferProperty)file.Blocks[2]).Flags);
        Assert.Equal(3u, ((NifZBufferProperty)file.Blocks[2]).Function);
        Assert.Equal(0xCDCD, ((NifVertexColorProperty)file.Blocks[3]).Flags);
        Assert.Equal(0u, ((NifVertexColorProperty)file.Blocks[3]).VertexMode);
        Assert.Equal(1u, ((NifVertexColorProperty)file.Blocks[3]).LightingMode);
    }

    [Fact]
    public void OutOfRangeEnumValuesFail()
    {
        Assert.Throws<InvalidDataException>(
            () => Read(("NiTexturingProperty", NifTestProperties.Texturing(applyMode: 5))));
        Assert.Throws<InvalidDataException>(
            () => Read(("NiTexturingProperty", NifTestProperties.Texturing(
                baseSlot: (0, 4, 2, 0, 0, 0)))));
        Assert.Throws<InvalidDataException>(
            () => Read(("NiTexturingProperty", NifTestProperties.Texturing(
                baseSlot: (0, 3, 7, 0, 0, 0)))));
        Assert.Throws<InvalidDataException>(
            () => Read(("NiZBufferProperty", NifTestProperties.ZBuffer(function: 8))));
        Assert.Throws<InvalidDataException>(
            () => Read(("NiSourceTexture", NifTestProperties.SourceTexture(pixelLayout: 8))));
        Assert.Throws<InvalidDataException>(
            () => Read(("NiVertexColorProperty", NifTestProperties.VertexColor(vertexMode: 3))));
    }

    [Fact]
    public void TextureSourceReferenceToWrongTypeFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        var texType = builder.AddBlockType("NiTexturingProperty");
        builder.AddBlock(texType, NifTestProperties.Texturing(baseSlot: (1, 3, 2, 0, 0, 0)));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root"));
        builder.AddTrailing(NifTestBlocks.Footer());
        using var stream = new MemoryStream(builder.Build());
        var ex = Assert.Throws<InvalidDataException>(() => NifFile.Read(stream));
        Assert.Contains("texture source", ex.Message);
    }

    [Fact]
    public void PropertyReferenceToNonPropertyFails()
    {
        var builder = new NifTestDataBuilder();
        var nodeType = builder.AddBlockType("NiNode");
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "root", properties: [1]));
        builder.AddBlock(nodeType, NifTestBlocks.Node(name: "child"));
        builder.AddTrailing(NifTestBlocks.Footer());
        using var stream = new MemoryStream(builder.Build());
        var ex = Assert.Throws<InvalidDataException>(() => NifFile.Read(stream));
        Assert.Contains("as a property", ex.Message);
    }

    [Fact]
    public void PixelDataReferenceFailsWithNotSupported()
    {
        Assert.Throws<NotSupportedException>(
            () => Read(("NiSourceTexture", NifTestProperties.SourceTexture(pixelData: 0))));
    }

    [Fact]
    public void TextureTransformDecodesAllFields()
    {
        var file = Read(
            ("NiTexturingProperty", NifTestProperties.Texturing(
                baseSlot: (1, 3, 2, 0, 0, 0),
                baseTransform: (0.25f, -0.5f, 2f, 3f, 1.5f, 1u, 0.25f, 0.75f))),
            ("NiSourceTexture", NifTestProperties.SourceTexture()));

        var texturing = (NifTexturingProperty)file.Blocks[0];
        var transform = texturing.Slots["Base"].Transform;
        Assert.NotNull(transform);
        Assert.Equal(0.25f, transform!.Value.TranslationU);
        Assert.Equal(-0.5f, transform.Value.TranslationV);
        Assert.Equal(2f, transform.Value.TilingU);
        Assert.Equal(3f, transform.Value.TilingV);
        Assert.Equal(1.5f, transform.Value.Rotation);
        Assert.Equal(1u, transform.Value.TransformType);
        Assert.Equal(0.25f, transform.Value.CenterU);
        Assert.Equal(0.75f, transform.Value.CenterV);
    }

    [Fact]
    public void ShaderTexturesFailWithNotSupported()
    {
        Assert.Throws<NotSupportedException>(
            () => Read(("NiTexturingProperty", NifTestProperties.Texturing(numShaderTextures: 2))));
    }
}
