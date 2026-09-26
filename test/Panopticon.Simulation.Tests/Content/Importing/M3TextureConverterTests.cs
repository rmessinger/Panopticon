using System;
using System.IO;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.M3;
using Panopticon.Content.Loaders;
using Panopticon.Tests.Content;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class M3TextureConverterTests
{
    [Fact]
    public void BaseColorForcesAlphaToOpaque()
    {
        var converter = new M3TextureConverter();

        var pixel = ConvertFirstPixel(converter, M3TextureConversionRule.BaseColor, CreateDxt5Dds(alphaIndex: 1));

        Assert.Equal(255, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
        Assert.Equal(255, pixel.A);
    }

    [Fact]
    public void NormalRuleProducesUnitLength()
    {
        var converter = new M3TextureConverter();

        var pixel = ConvertFirstPixel(
            converter,
            M3TextureConversionRule.Normal,
            CreateDxt5Dds(alphaIndex: 4, colorIndex: 3, color0: 0xFFFF, color1: 0x0000));
        var x = pixel.R / 255.0f * 2.0f - 1.0f;
        var y = pixel.G / 255.0f * 2.0f - 1.0f;
        var z = pixel.B / 255.0f * 2.0f - 1.0f;
        var length = MathF.Sqrt(x * x + y * y + z * z);

        Assert.True(MathF.Abs(length - 1.0f) < 1e-2f, $"Expected unit normal, got length {length}.");
    }

    [Fact]
    public void RoughnessRuleMapsSpecularityEightyToR0()
    {
        var converter = new M3TextureConverter();

        var pixel = ConvertFirstPixel(
            converter,
            M3TextureConversionRule.Roughness,
            CreateDxt1Dds(0xFFFF, 0x0000, colorIndex: 0),
            specularity: 80.0f);

        Assert.Equal(0, pixel.B);
        Assert.InRange(pixel.G / 255.0f, 0.39f, 0.40f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EmissiveChannelSelectorsMapChannels(uint selector)
    {
        var converter = new M3TextureConverter();
        var dds = CreateDxt5Dds(alphaIndex: 2, colorIndex: 2);
        var decoded = Panopticon.FileFormats.M3.DxtDecoder.DecodeBc3(DdsPayload(dds), 4, 4);

        var pixel = ConvertFirstPixel(converter, M3TextureConversionRule.Emissive, dds, channelSelector: selector);

        var r = decoded[0];
        var g = decoded[1];
        var b = decoded[2];
        var a = decoded[3];
        var expected = selector switch
        {
            0 or 1 => (r, g, b),
            2 => (a, a, a),
            3 => (r, r, r),
            4 => (g, g, g),
            5 => (b, b, b),
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(expected.Item1, pixel.R);
        Assert.Equal(expected.Item2, pixel.G);
        Assert.Equal(expected.Item3, pixel.B);
        Assert.Equal(255, pixel.A);
    }

    [Fact]
    public void ResultsAreCachedByPathAndRule()
    {
        var converter = new M3TextureConverter();
        var dds = CreateDxt1Dds(0xF800, 0x07E0, 0);

        var first = converter.Convert("texture.dds", dds, M3TextureConversionRule.BaseColor);
        var second = converter.Convert("texture.dds", dds, M3TextureConversionRule.BaseColor);

        Assert.Same(first, second);
    }

    [Fact]
    public void NonDxtFourCcFailsNamingFourCc()
    {
        var converter = new M3TextureConverter();
        var dds = CreateDds("DXT3", new byte[16]);

        var ex = Assert.Throws<NotSupportedException>(
            () => converter.Convert("texture.dds", dds, M3TextureConversionRule.BaseColor));

        Assert.Contains("DXT3", ex.Message);
    }

    [Fact]
    public void ImportedImageDecoderStillRejectsDds()
    {
        var dds = CreateDxt1Dds(0xF800, 0x07E0, 0);

        Assert.Throws<NotSupportedException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateEmbeddedBytes(dds, "image/vnd-ms.dds", "texture.dds"),
                new MemoryFileSystem()));
    }

    private static (byte R, byte G, byte B, byte A) ConvertFirstPixel(
        M3TextureConverter converter,
        M3TextureConversionRule rule,
        byte[] dds,
        float specularity = 0.0f,
        uint channelSelector = 0)
    {
        var source = converter.Convert("texture.dds", dds, rule, specularity, channelSelector);
        var decoded = ImportedImageDecoder.Decode(source, new MemoryFileSystem());
        var pixels = decoded.RgbaPixels.ToArray();
        return (pixels[0], pixels[1], pixels[2], pixels[3]);
    }

    private static byte[] CreateDxt1Dds(ushort color0, ushort color1, int colorIndex)
    {
        return CreateDds("DXT1", CreateBc1Block(color0, color1, colorIndex));
    }

    private static byte[] CreateDxt5Dds(
        int alphaIndex,
        int colorIndex = 0,
        ushort color0 = 0xF800,
        ushort color1 = 0x001F)
    {
        var block = new byte[16];
        block[0] = 255;
        block[1] = 0;
        WriteAlphaIndex(block, alphaIndex);
        CreateBc1Block(color0, color1, colorIndex).CopyTo(block, 8);
        return CreateDds("DXT5", block);
    }

    private static byte[] CreateDds(string fourCc, byte[] topMip)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("DDS "));
        writer.Write((uint)124);
        writer.Write((uint)0x0002100F);
        writer.Write((uint)4);
        writer.Write((uint)4);
        writer.Write((uint)topMip.Length);
        writer.Write((uint)0);
        writer.Write((uint)1);
        writer.Write(new byte[44]);
        writer.Write((uint)32);
        writer.Write((uint)0x4);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(fourCc));
        writer.Write(new byte[20]);
        writer.Write((uint)0x1000);
        writer.Write(new byte[16]);
        writer.Write(topMip);
        return stream.ToArray();
    }

    private static byte[] CreateBc1Block(ushort color0, ushort color1, int colorIndex)
    {
        var block = new byte[8];
        block[0] = (byte)(color0 & 0xFF);
        block[1] = (byte)(color0 >> 8);
        block[2] = (byte)(color1 & 0xFF);
        block[3] = (byte)(color1 >> 8);
        block[4] = (byte)(colorIndex & 0x3);
        return block;
    }

    private static void WriteAlphaIndex(byte[] block, int alphaIndex)
    {
        ulong indices = (ulong)alphaIndex;
        for (var i = 0; i < 6; i++)
        {
            block[2 + i] = (byte)((indices >> (8 * i)) & 0xFF);
        }
    }

    private static ReadOnlySpan<byte> DdsPayload(byte[] dds)
    {
        return dds.AsSpan(128, 16);
    }
}
