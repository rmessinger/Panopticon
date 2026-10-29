using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.FileFormats.Bar;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Decodes synthetic DDT images through the ImportedImageDecoder RTS3 branch:
/// BC formats via the DDS block decoder, BGRA swizzled to RGBA, R8 replicated
/// to RGB with opaque alpha. Only the base mip is consumed.
/// </summary>
public sealed class DdtImportedImageDecoderTests
{
    // c0 = 0xF800 (565 red) > c1 = 0, all indices 0: an opaque red 4x4 block.
    private static readonly byte[] RedBc1ColorBlock =
        [0x00, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public void DecodesBc1BaseMip()
    {
        // A trailing junk mip proves the decoder consumes the base mip only.
        var decoded = Decode(DdtFormat.Bc1, 4, 4, RedBc1ColorBlock, new byte[] { 9, 9 });

        Assert.Equal(4, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Assert.All(
            Enumerable.Range(0, decoded.Width * decoded.Height),
            i =>
            {
                var pixels = decoded.RgbaPixels.ToArray();
                Assert.Equal(255, pixels[i * 4]);
                Assert.Equal(0, pixels[i * 4 + 1]);
                Assert.Equal(0, pixels[i * 4 + 2]);
                Assert.Equal(255, pixels[i * 4 + 3]);
            });
    }

    [Fact]
    public void DecodesBc2BaseMip()
    {
        // Eight alpha bytes of 0x88: every texel nibble 8 -> alpha 136.
        var block = new byte[16];
        for (var i = 0; i < 8; i++)
        {
            block[i] = 0x88;
        }

        RedBc1ColorBlock.CopyTo(block, 8);

        var decoded = Decode(DdtFormat.Bc2, 4, 4, block);

        var pixels = decoded.RgbaPixels.ToArray();
        Assert.Equal(255, pixels[0]);
        Assert.Equal(0, pixels[1]);
        Assert.Equal(0, pixels[2]);
        Assert.Equal(136, pixels[3]);
    }

    [Fact]
    public void DecodesBc3BaseMip()
    {
        // a0 = 200 > a1 = 100 with all-zero alpha indices: alpha 200 everywhere.
        var block = new byte[16];
        block[0] = 200;
        block[1] = 100;
        RedBc1ColorBlock.CopyTo(block, 8);

        var decoded = Decode(DdtFormat.Bc3, 4, 4, block);

        var pixels = decoded.RgbaPixels.ToArray();
        Assert.Equal(255, pixels[0]);
        Assert.Equal(200, pixels[3]);
    }

    [Fact]
    public void DecodesBgraBaseMipBySwizzlingToRgba()
    {
        var bgra = new byte[]
        {
            10, 20, 30, 255, // B G R A -> RGBA 30 20 10 255
            40, 50, 60, 128, // -> RGBA 60 50 40 128
        };

        var decoded = Decode(DdtFormat.Bgra, 2, 1, bgra);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal([30, 20, 10, 255, 60, 50, 40, 128], decoded.RgbaPixels.ToArray());
    }

    [Fact]
    public void DecodesR8BaseMipByReplicatingChannels()
    {
        var decoded = Decode(DdtFormat.R8, 2, 1, new byte[] { 200, 100 });

        Assert.Equal([200, 200, 200, 255, 100, 100, 100, 255], decoded.RgbaPixels.ToArray());
    }

    private static DecodedImportedImage Decode(DdtFormat format, uint width, uint height, params byte[][] mips)
    {
        var image = SyntheticDdtBuilder.Build(format, width, height, mips);

        return ImportedImageDecoder.Decode(
            ImportedImageSource.CreateEmbeddedBytes(image, "image/x-ddt", "synthetic.ddt"),
            new MemoryFileSystem());
    }
}
