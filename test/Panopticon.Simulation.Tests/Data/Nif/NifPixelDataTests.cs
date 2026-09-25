using System;
using System.IO;
using System.Linq;
using Panopticon.FileFormats.Nif;
using Xunit;
using NifTestBlocks = Panopticon.Tests.Data.Nif.NifTestBlocks;

namespace Panopticon.Tests.Data.Nif;

public sealed class NifPixelDataTests
{
    private static void WritePixelDataHeader(NifBodyWriter w, uint format, uint repeat, int palette = -1)
    {
        w.WriteUInt32(0); // leading u32 (read by NifFile)
        w.WriteUInt32(format);
        for (var i = 0; i < 4; i++)
        {
            w.WriteUInt32(0); // RGBA masks
        }

        w.WriteUInt32(0); // bits per pixel
        w.WriteUInt32(repeat);
        w.WriteUInt32(0); // unknown 1
        w.WriteUInt32(0); // unknown 2
        w.WriteInt32(palette);
    }

    private static NifFile Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return NifFile.Read(stream);
    }

    [Fact]
    public void Dxt1TexcacheRoundTripsMipChain()
    {
        var bytes = NifTestTexcache.Texcache(
            NifPixelData.FormatDxt1,
            (64, 32), (32, 16), (16, 8), (8, 4), (4, 4), (2, 2), (1, 1));

        var file = Read(bytes);
        var pixelData = Assert.IsType<NifPixelData>(file.Blocks[0]);

        Assert.Equal(NifPixelData.FormatDxt1, pixelData.Format);
        Assert.Equal(7, pixelData.Mips.Count);
        Assert.Equal(8u, pixelData.BytesPerBlock);

        Assert.Equal(64u, pixelData.Mips[0].Width);
        Assert.Equal(32u, pixelData.Mips[0].Height);
        Assert.Equal(0u, pixelData.Mips[0].Offset);

        // 64x32 = 16x8 blocks * 8 bytes = 1024 bytes.
        Assert.Equal(1024, pixelData.GetMipSize(0));

        // Minimum block coverage for the 1x1 mip.
        Assert.Equal(8, pixelData.GetMipSize(6));

        var expectedTotal = 0L;
        for (var i = 0; i < pixelData.Mips.Count; i++)
        {
            Assert.Equal((uint)expectedTotal, pixelData.Mips[i].Offset);
            expectedTotal += pixelData.GetMipSize(i);
        }

        Assert.Equal(expectedTotal, pixelData.PixelData.Length);
    }

    [Fact]
    public void Dxt3TexcacheUsesSixteenByteBlocks()
    {
        var bytes = NifTestTexcache.Texcache(NifPixelData.FormatDxt3, (32, 32));
        var pixelData = Assert.IsType<NifPixelData>(Read(bytes).Blocks[0]);
        Assert.Equal(NifPixelData.FormatDxt3, pixelData.Format);
        Assert.Equal(16u, pixelData.BytesPerBlock);
        Assert.Equal(16 * 8 * 8, pixelData.PixelData.Length);
    }

    [Fact]
    public void UnsupportedFormatFailsWithNotSupported()
    {
        var bytes = NifTestTexcache.Texcache(0, (16, 16));
        Assert.Throws<NotSupportedException>(() => Read(bytes));
    }

    [Fact]
    public void FormatRepeatMismatchFails()
    {
        // Handcraft: format 4, repeat 5.
        var builder = new NifTestDataBuilder();
        var pixelType = builder.AddBlockType("NiPixelData");
        var body = new MemoryStream();
        var w = new NifBodyWriter(body);
        WritePixelDataHeader(w, format: 4, repeat: 5);
        w.WriteUInt32(1);
        w.WriteUInt32(0);
        w.WriteUInt32(4);
        w.WriteUInt32(4);
        w.WriteUInt32(0);
        w.WriteUInt32(8);
        w.WriteBytes(new byte[8]);
        builder.AddBlock(pixelType, body.ToArray());
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("repeats format", ex.Message);
    }

    [Fact]
    public void NonContiguousMipOffsetFails()
    {
        // Mip 0 declares 8 bytes; mip 1 offset must be 8, not 16.
        var builder = new NifTestDataBuilder();
        var pixelType = builder.AddBlockType("NiPixelData");
        var body = new MemoryStream();
        var w = new NifBodyWriter(body);
        WritePixelDataHeader(w, format: 4, repeat: 4);
        w.WriteUInt32(2);
        w.WriteUInt32(0);
        w.WriteUInt32(4);
        w.WriteUInt32(4);
        w.WriteUInt32(0);
        w.WriteUInt32(4);
        w.WriteUInt32(4);
        w.WriteUInt32(16);
        w.WriteUInt32(16);
        w.WriteBytes(new byte[16]);
        builder.AddBlock(pixelType, body.ToArray());
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("contiguous", ex.Message);
    }

    [Fact]
    public void PixelByteCountMismatchFails()
    {
        // One 8x8 DXT1 mip computes a 32-byte chain; declaring 16 must fail.
        var builder = new NifTestDataBuilder();
        var pixelType = builder.AddBlockType("NiPixelData");
        var body = new MemoryStream();
        var w = new NifBodyWriter(body);
        WritePixelDataHeader(w, format: 4, repeat: 4);
        w.WriteUInt32(1);
        w.WriteUInt32(0);
        w.WriteUInt32(8);
        w.WriteUInt32(8);
        w.WriteUInt32(0);
        w.WriteUInt32(16);
        w.WriteBytes(new byte[16]);
        builder.AddBlock(pixelType, body.ToArray());
        builder.AddTrailing(NifTestBlocks.Footer());

        var ex = Assert.Throws<InvalidDataException>(() => Read(builder.Build()));
        Assert.Contains("mip chain totals", ex.Message);
    }

    [Fact]
    public void PaletteReferenceFailsWithNotSupported()
    {
        var builder = new NifTestDataBuilder();
        var pixelType = builder.AddBlockType("NiPixelData");
        var body = new MemoryStream();
        var w = new NifBodyWriter(body);
        WritePixelDataHeader(w, format: 4, repeat: 4, palette: 0);
        w.WriteUInt32(1);
        w.WriteUInt32(0);
        w.WriteUInt32(4);
        w.WriteUInt32(4);
        w.WriteUInt32(0);
        w.WriteUInt32(8);
        w.WriteBytes(new byte[8]);
        builder.AddBlock(pixelType, body.ToArray());
        builder.AddTrailing(NifTestBlocks.Footer());

        Assert.Throws<NotSupportedException>(() => Read(builder.Build()));
    }
}
