using System;
using Panopticon.Data.Dds;
using Panopticon.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Panopticon.Tests.Data.Dds;

public class DdsBlockDecoderTests
{
    private const ushort Red565 = 0xF800;
    private const ushort Green565 = 0x07E0;

    [Fact]
    public void DecodesBc1SolidOpaqueBlock()
    {
        var data = BuildBc1Block(Red565, Green565, indices: 0u);
        var mip = new TextureMipMapData(data, 8, 8, 4, 4);

        using var image = DdsBlockDecoder.DecodeBaseMip(mip, DdsImageFormat.Bc1);

        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var pixel = image[x, y];
                Assert.Equal(255, pixel.A);
                Assert.Equal(255, pixel.R);
                Assert.Equal(0, pixel.G);
            }
        }
    }

    [Fact]
    public void DecodesBc1PunchthroughAsTransparent()
    {
        // Equal endpoints select three-colour mode; index 3 is punchthrough.
        var data = BuildBc1Block(Red565, Red565, indices: 0xFFFFFFFFu);
        var mip = new TextureMipMapData(data, 8, 8, 4, 4);

        using var image = DdsBlockDecoder.DecodeBaseMip(mip, DdsImageFormat.Bc1);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(0, image[x, y].A);
            }
        }
    }

    [Fact]
    public void DecodesBc1PartialBlocksAtEdges()
    {
        // 5x5 spans a 2x2 block grid; out-of-bounds texels must be skipped.
        var block = BuildBc1Block(Red565, Green565, indices: 0u);
        var data = new byte[4 * block.Length];
        Array.Copy(block, data, block.Length);
        Array.Copy(block, 0, data, block.Length, block.Length);
        Array.Copy(block, 0, data, 2 * block.Length, block.Length);
        Array.Copy(block, 0, data, 3 * block.Length, block.Length);
        var mip = new TextureMipMapData(data, 16, 64, 5, 5);

        using var image = DdsBlockDecoder.DecodeBaseMip(mip, DdsImageFormat.Bc1);

        Assert.Equal(5, image.Width);
        Assert.Equal(5, image.Height);
        Assert.Equal(255, image[4, 4].R);
    }

    [Fact]
    public void DecodesBc2ExplicitAlpha()
    {
        // Four-bit alpha pairs: 0x0F = pixel0 15 (255), pixel1 0 (0).
        var alpha = new byte[] { 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F };
        var data = new byte[16];
        Array.Copy(alpha, 0, data, 0, 8);
        var colorBlock = BuildBc1Block(Red565, Green565, indices: 0u);
        Array.Copy(colorBlock, 0, data, 8, 8);
        var mip = new TextureMipMapData(data, 16, 16, 4, 4);

        using var image = DdsBlockDecoder.DecodeBaseMip(mip, DdsImageFormat.Bc2);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var expected = x % 2 == 0 ? 255 : 0;
                Assert.Equal(expected, image[x, y].A);
            }
        }
    }

    [Fact]
    public void DecodesBc3InterpolatedAlpha()
    {
        var data = new byte[16];
        var colorBlock = BuildBc1Block(Red565, Green565, indices: 0u);
        Array.Copy(colorBlock, 0, data, 8, 8);

        // Endpoints 255 and 0 with eight-value interpolation; indices select
        // palette entries 0, 1 and 2 for the first three texels.
        data[0] = 255;
        data[1] = 0;
        ulong bits = (0u << 0) | (1u << 3) | (2u << 6);
        for (var i = 0; i < 6; i++)
        {
            data[2 + i] = (byte)((bits >> (i * 8)) & 0xFF);
        }

        var mip = new TextureMipMapData(data, 16, 16, 4, 4);

        using var image = DdsBlockDecoder.DecodeBaseMip(mip, DdsImageFormat.Bc3);

        Assert.Equal(255, image[0, 0].A);
        Assert.Equal(0, image[1, 0].A);
        Assert.Equal(218, image[2, 0].A);
    }

    [Fact]
    public void RejectsNonCutoutFormats()
    {
        var mip = new TextureMipMapData(Array.Empty<byte>(), 0, 0, 1, 1);

        Assert.Throws<NotSupportedException>(
            () => DdsBlockDecoder.DecodeBaseMip(mip, DdsImageFormat.Rgba16Float));
    }

    private static byte[] BuildBc1Block(ushort c0, ushort c1, uint indices)
    {
        var data = new byte[8];
        data[0] = (byte)(c0 & 0xFF);
        data[1] = (byte)(c0 >> 8);
        data[2] = (byte)(c1 & 0xFF);
        data[3] = (byte)(c1 >> 8);
        data[4] = (byte)(indices & 0xFF);
        data[5] = (byte)((indices >> 8) & 0xFF);
        data[6] = (byte)((indices >> 16) & 0xFF);
        data[7] = (byte)((indices >> 24) & 0xFF);
        return data;
    }
}
