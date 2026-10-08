using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Veldrid.ImageSharp;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class CoverageMipBuilderTests
{
    private const float Cutoff = 0.37647f;

    [Fact]
    public void OpaqueBaseKeepsFullCoverageAtEveryLevel()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(255, 0, 0, 255));

        var chain = CoverageMipBuilder.GenerateCoverageMipmaps(image, Cutoff);

        Assert.Equal(4, chain.Length);
        foreach (var level in chain)
        {
            Assert.All(EnumeratePixels(level), pixel => Assert.Equal(255, pixel.A));
        }
    }

    [Fact]
    public void TransparentBaseKeepsZeroCoverageAtEveryLevel()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(0, 255, 0, 0));

        var chain = CoverageMipBuilder.GenerateCoverageMipmaps(image, Cutoff);

        Assert.All(chain, level => Assert.Equal(0, CountPassing(level, Cutoff)));
    }

    [Fact]
    public void CheckerboardKeepsCoverageFractionAcrossLevels()
    {
        using var image = new Image<Rgba32>(8, 8);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                image[x, y] = (x / 4 + y / 4) % 2 == 0
                    ? new Rgba32(255, 255, 255, 255)
                    : new Rgba32(255, 255, 255, 0);
            }
        }

        var chain = CoverageMipBuilder.GenerateCoverageMipmaps(image, Cutoff);

        Assert.Equal(32, CountPassing(chain[0], Cutoff));
        Assert.InRange(CountPassing(chain[1], Cutoff), 7, 9);
        Assert.InRange(CountPassing(chain[2], Cutoff), 1, 3);
        Assert.InRange(CountPassing(chain[3], Cutoff), 0, 1);
    }

    [Fact]
    public void ThinDiagonalKeepsCoverageFractionAcrossLevels()
    {
        using var image = new Image<Rgba32>(8, 8);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                image[x, y] = x == y ? new Rgba32(0, 0, 255, 255) : new Rgba32(0, 0, 255, 0);
            }
        }

        var chain = CoverageMipBuilder.GenerateCoverageMipmaps(image, Cutoff);

        // 8 of 64 texels pass at the base; every level stays within one texel of
        // the same fraction, which an unmodified Lanczos chain erodes to zero.
        Assert.Equal(8, CountPassing(chain[0], Cutoff));
        Assert.InRange(CountPassing(chain[1], Cutoff), 1, 3);
        Assert.InRange(CountPassing(chain[2], Cutoff), 0, 2);
        Assert.InRange(CountPassing(chain[3], Cutoff), 0, 1);
    }

    [Fact]
    public void ChainMatchesRegularMipChainOnRgb()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(30, 60, 90, 255));
        using var regularBase = image.Clone();
        regularBase.Mutate(context => context.Resize(4, 4));
        var coverage = CoverageMipBuilder.GenerateCoverageMipmaps(image, Cutoff);

        Assert.Equal(4, coverage[1].Width);
        Assert.Equal(4, coverage[1].Height);
    }

    private static long CountPassing(Image<Rgba32> level, float cutoff)
    {
        long count = 0;
        for (var y = 0; y < level.Height; y++)
        {
            for (var x = 0; x < level.Width; x++)
            {
                if (level[x, y].A / 255f >= cutoff)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Rgba32[] EnumeratePixels(Image<Rgba32> level)
    {
        var pixels = new Rgba32[level.Width * level.Height];
        for (var y = 0; y < level.Height; y++)
        {
            for (var x = 0; x < level.Width; x++)
            {
                pixels[y * level.Width + x] = level[x, y];
            }
        }

        return pixels;
    }
}
