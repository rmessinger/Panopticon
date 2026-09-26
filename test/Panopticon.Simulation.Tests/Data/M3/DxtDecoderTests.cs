using Panopticon.FileFormats.M3;
using Xunit;

namespace Panopticon.Tests.Data.M3;

public sealed class DxtDecoderTests
{
    [Fact]
    public void Bc1FourColorBlockDecodesExactRgba()
    {
        var block = CreateBc1Block(0xF800, 0x07E0, [0, 1, 2, 3]);

        var pixels = DxtDecoder.DecodeBc1(block, 4, 4);

        AssertPixel(pixels, 0, 255, 0, 0, 255);
        AssertPixel(pixels, 1, 0, 255, 0, 255);
        AssertPixel(pixels, 2, 170, 85, 0, 255);
        AssertPixel(pixels, 3, 85, 170, 0, 255);
    }

    [Fact]
    public void Bc1ThreeColorBlockDecodesOneBitAlpha()
    {
        var block = CreateBc1Block(0x0000, 0xFFFF, [0, 1, 2, 3]);

        var pixels = DxtDecoder.DecodeBc1(block, 4, 4);

        AssertPixel(pixels, 0, 0, 0, 0, 255);
        AssertPixel(pixels, 1, 255, 255, 255, 255);
        AssertPixel(pixels, 2, 127, 127, 127, 255);
        AssertPixel(pixels, 3, 0, 0, 0, 0);
    }

    [Fact]
    public void Bc3BlockDecodesExplicitAlphaAndColor()
    {
        var block = new byte[16];
        block[0] = 255;
        block[1] = 0;
        WriteAlphaIndices(block, [0, 1, 2, 7]);
        CreateBc1Block(0xF800, 0x001F, [0, 1, 2, 3]).CopyTo(block, 8);

        var pixels = DxtDecoder.DecodeBc3(block, 4, 4);

        AssertPixel(pixels, 0, 255, 0, 0, 255);
        AssertPixel(pixels, 1, 0, 0, 255, 0);
        AssertPixel(pixels, 2, 170, 0, 85, 218);
        AssertPixel(pixels, 3, 85, 0, 170, 36);
    }

    [Fact]
    public void ClipsPartialBlocks()
    {
        var block = CreateBc1Block(0xF800, 0x07E0, [0]);

        var pixels = DxtDecoder.DecodeBc1(block, 1, 1);

        Assert.Equal(4, pixels.Length);
        AssertPixel(pixels, 0, 255, 0, 0, 255);
    }

    private static byte[] CreateBc1Block(ushort color0, ushort color1, int[] firstIndices)
    {
        var block = new byte[8];
        block[0] = (byte)(color0 & 0xFF);
        block[1] = (byte)(color0 >> 8);
        block[2] = (byte)(color1 & 0xFF);
        block[3] = (byte)(color1 >> 8);
        uint indices = 0;
        for (var i = 0; i < firstIndices.Length; i++)
        {
            indices |= (uint)firstIndices[i] << (2 * i);
        }

        block[4] = (byte)(indices & 0xFF);
        block[5] = (byte)((indices >> 8) & 0xFF);
        block[6] = (byte)((indices >> 16) & 0xFF);
        block[7] = (byte)((indices >> 24) & 0xFF);
        return block;
    }

    private static void WriteAlphaIndices(byte[] block, int[] firstIndices)
    {
        ulong indices = 0;
        for (var i = 0; i < firstIndices.Length; i++)
        {
            indices |= (ulong)firstIndices[i] << (3 * i);
        }

        for (var i = 0; i < 6; i++)
        {
            block[2 + i] = (byte)((indices >> (8 * i)) & 0xFF);
        }
    }

    private static void AssertPixel(byte[] pixels, int pixelIndex, byte r, byte g, byte b, byte a)
    {
        var offset = pixelIndex * 4;
        Assert.Equal(r, pixels[offset]);
        Assert.Equal(g, pixels[offset + 1]);
        Assert.Equal(b, pixels[offset + 2]);
        Assert.Equal(a, pixels[offset + 3]);
    }
}
