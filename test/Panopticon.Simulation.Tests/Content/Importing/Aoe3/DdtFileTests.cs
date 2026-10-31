using System;
using System.IO;
using System.Linq;
using Panopticon.FileFormats.Bar;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public sealed class DdtFileTests
{
    [Fact]
    public void ReadsHeaderAndMipTable()
    {
        var blob = new byte[32];
        new Random(1).NextBytes(blob);

        var ddt = DdtFile.Read(new MemoryStream(SyntheticDdtBuilder.Build(DdtFormat.Bc1, 8, 8, blob)));

        Assert.Equal(0, ddt.Usage);
        Assert.Equal(0, ddt.AlphaBits);
        Assert.Equal(DdtFormat.Bc1, ddt.Format);
        Assert.Equal(1, ddt.MipCount);
        Assert.Equal(8u, ddt.Width);
        Assert.Equal(8u, ddt.Height);

        var mip = Assert.Single(ddt.Mips);
        Assert.Equal(24u, mip.Offset);
        Assert.Equal(32u, mip.Size);
        Assert.True(ddt.GetMipData(0).SequenceEqual(blob));
    }

    [Fact]
    public void ReadsMultiMipChainWithAbsoluteOffsets()
    {
        var first = new byte[] { 1, 2, 3 };
        var second = new byte[] { 4, 5 };

        var ddt = DdtFile.Read(new MemoryStream(
            SyntheticDdtBuilder.Build(DdtFormat.Bgra, 4, 4, first, second)));

        Assert.Equal(2, ddt.MipCount);
        Assert.Equal((16 + 16) + 0u, ddt.Mips[0].Offset);
        Assert.Equal((16 + 16) + 3u, ddt.Mips[1].Offset);
        Assert.True(ddt.GetMipData(1).SequenceEqual(second));
    }

    [Fact]
    public void ThrowsOnBadMagic()
    {
        var image = SyntheticDdtBuilder.Build(DdtFormat.Bc1, 4, 4, new byte[8]);
        image[0] = (byte)'X';

        var exception = Assert.Throws<InvalidDataException>(
            () => DdtFile.Read(new MemoryStream(image)));

        Assert.Contains("RTS3", exception.Message);
    }

    [Fact]
    public void ThrowsOnUnsupportedFormatCode()
    {
        var image = SyntheticDdtBuilder.Build((DdtFormat)3, 4, 4, new byte[8]);

        var exception = Assert.Throws<InvalidDataException>(
            () => DdtFile.Read(new MemoryStream(image)));

        Assert.Contains("format code 3", exception.Message);
    }

    [Fact]
    public void ThrowsOnZeroMipCount()
    {
        var image = SyntheticDdtBuilder.Build(DdtFormat.Bc1, 4, 4, new byte[8]);
        image[7] = 0;

        var exception = Assert.Throws<InvalidDataException>(
            () => DdtFile.Read(new MemoryStream(image)));

        Assert.Contains("no mip levels", exception.Message);
    }

    [Fact]
    public void ThrowsWhenMipRangeExceedsFile()
    {
        var image = SyntheticDdtBuilder.Build(DdtFormat.Bc1, 4, 4, new byte[8]);
        image[16 + 4] = 0xFF; // mip 0 size high byte

        var exception = Assert.Throws<InvalidDataException>(
            () => DdtFile.Read(new MemoryStream(image)));

        Assert.Contains("mip 0", exception.Message);
    }

    [Fact]
    public void ThrowsWhenDataIsSmallerThanHeaderAndOneMip()
    {
        Assert.Throws<InvalidDataException>(() => DdtFile.Read(new byte[16]));
    }
}
