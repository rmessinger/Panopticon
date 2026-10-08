using System;
using System.Linq;
using Panopticon.Graphics;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class FrameDownsamplerTests
{
    [Fact]
    public void Downsample2x2_UniformFrame_ProducesSamePixel()
    {
        var frame = Frame(2, 2, (_, _) => ((byte)10, (byte)20, (byte)30, (byte)255));

        var result = FrameDownsampler.Downsample2x2(frame);

        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal([10, 20, 30, 255], result.PixelData);
    }

    [Fact]
    public void Downsample2x2_MixedQuad_RoundsToNearest()
    {
        // Per channel: (0 + 1 + 2 + 4 + 2) >> 2 = 2.
        var values = new byte[4] { 0, 1, 2, 4 };
        var frame = Frame(2, 2, (x, y) => (values[y * 2 + x], (byte)0, (byte)0, (byte)255));

        var result = FrameDownsampler.Downsample2x2(frame);

        Assert.Equal([2, 0, 0, 255], result.PixelData);
    }

    [Fact]
    public void Downsample2x2_Checkerboard_HalvesDimensionsAndAverages()
    {
        var frame = Frame(4, 4, (x, y) => ((x + y) % 2 == 0 ? (byte)200 : (byte)100, (byte)0, (byte)0, (byte)255));

        var result = FrameDownsampler.Downsample2x2(frame);

        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.All(result.PixelData.Where((value, index) => index % 4 == 0), value => Assert.Equal(150, value));
    }

    [Fact]
    public void Downsample2x2_PreservesFormat()
    {
        var frame = Frame(2, 2, (_, _) => ((byte)0, (byte)0, (byte)0, (byte)0), bgra: true);

        var result = FrameDownsampler.Downsample2x2(frame);

        Assert.Equal(PixelFormat.B8_G8_R8_A8_UNorm, result.Format);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    public void Downsample2x2_OddDimension_Throws(int width, int height)
    {
        var frame = Frame(width, height, (_, _) => ((byte)0, (byte)0, (byte)0, (byte)255));

        Assert.Throws<ArgumentException>(() => FrameDownsampler.Downsample2x2(frame));
    }

    [Fact]
    public void Downsample2x2_NullFrame_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FrameDownsampler.Downsample2x2(null!));
    }

    private static CapturedFrame Frame(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel, bool bgra = false)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var i = (y * width + x) * 4;
                if (bgra)
                {
                    pixels[i] = b;
                    pixels[i + 1] = g;
                    pixels[i + 2] = r;
                }
                else
                {
                    pixels[i] = r;
                    pixels[i + 1] = g;
                    pixels[i + 2] = b;
                }

                pixels[i + 3] = a;
            }
        }

        return new CapturedFrame(pixels, width, height, bgra ? PixelFormat.B8_G8_R8_A8_UNorm : PixelFormat.R8_G8_B8_A8_UNorm);
    }
}
