using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid.ImageSharp;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class PrefilteredTerrainMipBuilderTests
{
    [Theory]
    [InlineData(8, 8)]
    [InlineData(3, 7)]
    [InlineData(8, 16)]
    [InlineData(640, 640)]
    public void ProducesCompleteMipChain_AndPreservesConstantColour(int sourceSize, int targetSize)
    {
        using var source = new Image<Rgba32>(sourceSize, sourceSize, new Rgba32(128, 64, 192, 255));
        var levels = PrefilteredTerrainMipBuilder.Generate(source, targetSize);
        try
        {
            var expectedSize = targetSize;
            foreach (var level in levels)
            {
                Assert.Equal(expectedSize, level.Width);
                Assert.Equal(expectedSize, level.Height);
                Assert.Equal(new Rgba32(128, 64, 192, 255), level[0, 0]);
                expectedSize = Math.Max(1, expectedSize / 2);
            }

            Assert.Equal(1, levels[^1].Width);
        }
        finally
        {
            foreach (var level in levels)
            {
                level.Dispose();
            }
        }
    }

    [Fact]
    public void FiltersAcrossTileEdges_InLinearLight_WithoutChangingSource()
    {
        using var source = new Image<Rgba32>(8, 8, new Rgba32(0, 0, 0, 255));
        for (var y = 0; y < 8; y++)
        {
            source[0, y] = new Rgba32(255, 255, 255, 255);
        }

        var levels = PrefilteredTerrainMipBuilder.Generate(source, 8);
        var originalLevels = LinearMipBuilder.GenerateLinearMipmaps(source.Clone());
        try
        {
            Assert.InRange(levels[0][1, 0].R, 43, 45);
            Assert.Equal(levels[0][1, 0], levels[0][7, 0]);
            var expectedAverage = SrgbTransfer.ToSrgbByte(
                SrgbTransfer.ToLinear(originalLevels[^1][0, 0].R / 255f) * 0.8f + 0.125f * 0.2f);
            Assert.Equal(expectedAverage, levels[^1][0, 0].R);
            Assert.Equal(255, source[0, 0].R);
            Assert.Equal(0, source[7, 0].R);
        }
        finally
        {
            foreach (var level in levels)
            {
                level.Dispose();
            }

            foreach (var level in originalLevels)
            {
                level.Dispose();
            }
        }
    }

    [Fact]
    public void BlackWhiteAreaAverage_IsNotGammaSpaceMidGrey()
    {
        using var source = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 0, 255));
        source[0, 0] = source[1, 1] = new Rgba32(255, 255, 255, 255);
        var levels = PrefilteredTerrainMipBuilder.Generate(source, 2);
        try
        {
            Assert.InRange(levels[^1][0, 0].R, 187, 189);
        }
        finally
        {
            foreach (var level in levels)
            {
                level.Dispose();
            }
        }
    }

    [Fact]
    public void NonSquareInputIsRejected()
    {
        using var source = new Image<Rgba32>(4, 2);
        Assert.Throws<ArgumentException>(() => PrefilteredTerrainMipBuilder.Generate(source, 4));
    }
}
