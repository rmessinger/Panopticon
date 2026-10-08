using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid.ImageSharp;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class LinearMipBuilderTests
{
    [Fact]
    public void CheckerboardLevelOneAveragesInLinearLight()
    {
        // Per-pixel checkerboard: the 2x2 filter footprint of level one spans one black and
        // one white cell in both dimensions, averaging to linear 0.5 which encodes to ~188.
        // An encoded-space filter would produce 128 (the defect G6 fixes).
        using var image = CreateCheckerboard(8, 8, 1);

        var chain = LinearMipBuilder.GenerateLinearMipmaps(image);

        // Lanczos3 rings slightly on a per-pixel checker, so allow a small band around 188.
        Assert.InRange(chain[1][0, 0].R, 186, 190);
        Assert.InRange(chain[1][0, 0].G, 186, 190);
        Assert.InRange(chain[1][0, 0].B, 186, 190);
    }

    [Fact]
    public void AlphaSurvivesTheChainUnchanged()
    {
        using var image = CreateCheckerboard(8, 8, 4, alpha: 128);

        var chain = LinearMipBuilder.GenerateLinearMipmaps(image);

        foreach (var level in chain)
        {
            foreach (var pixel in EnumeratePixels(level))
            {
                Assert.Equal(128, pixel.A);
            }
        }
    }

    [Fact]
    public void BaseLevelIsTheOriginalImage()
    {
        using var image = CreateCheckerboard(8, 8, 4);

        var chain = LinearMipBuilder.GenerateLinearMipmaps(image);

        Assert.Equal(4, chain.Length);
        Assert.Equal(new Size(8, 8), new Size((int)chain[0].Width, (int)chain[0].Height));
        Assert.Equal(new Size(4, 4), new Size((int)chain[1].Width, (int)chain[1].Height));
        Assert.Equal(new Size(2, 2), new Size((int)chain[2].Width, (int)chain[2].Height));
        Assert.Equal(new Size(1, 1), new Size((int)chain[3].Width, (int)chain[3].Height));

        Assert.Equal(image[0, 0], chain[0][0, 0]);
        Assert.Equal(image[4, 4], chain[0][4, 4]);
    }

    [Fact]
    public void CoverageBuilderLinearPathPreservesCoverageAndAveragesLinearLight()
    {
        // Alpha coverage behaves exactly as in the encoded path...
        using var alphaChecker = new Image<Rgba32>(8, 8);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var covered = (x / 4 + y / 4) % 2 == 0;
                alphaChecker[x, y] = covered
                    ? new Rgba32(255, 255, 255, 255)
                    : new Rgba32(255, 255, 255, 0);
            }
        }

        var alphaChain = CoverageMipBuilder.GenerateCoverageMipmaps(alphaChecker, 0.37647f, linearRgb: true);

        Assert.Equal(32, CountPassing(alphaChain[0], 0.37647f));
        Assert.InRange(CountPassing(alphaChain[1], 0.37647f), 7, 9);

        // ...while RGB now averages in linear light: the 1x1 tail of a half-covered
        // black/white image encodes to ~188 instead of the encoded-space 128.
        using var colourChecker = CreateCheckerboard(8, 8, 4);

        var chain = CoverageMipBuilder.GenerateCoverageMipmaps(colourChecker, 0.37647f, linearRgb: true);

        Assert.Equal(188, chain[3][0, 0].R);
    }

    private static Image<Rgba32> CreateCheckerboard(int width, int height, int cell, byte alpha = 255)
    {
        var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var white = (x / cell + y / cell) % 2 == 0;
                image[x, y] = white
                    ? new Rgba32(255, 255, 255, alpha)
                    : new Rgba32(0, 0, 0, alpha);
            }
        }

        return image;
    }

    private static IEnumerable<Rgba32> EnumeratePixels(Image<Rgba32> image)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                yield return image[x, y];
            }
        }
    }

    private static int CountPassing(Image<Rgba32> image, float cutoff)
    {
        var count = 0;
        foreach (var pixel in EnumeratePixels(image))
        {
            if (pixel.A / 255f >= cutoff)
            {
                count++;
            }
        }

        return count;
    }
}
