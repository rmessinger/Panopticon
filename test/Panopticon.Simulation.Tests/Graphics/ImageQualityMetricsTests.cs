using System;
using Panopticon.Graphics;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class ImageQualityMetricsTests
{
    [Fact]
    public void Score_UniformField_ScoresZeroOnBothMetrics()
    {
        var frame = Frame(width: 32, height: 32, (x, y) => (128, 128, 128, 255));

        var scores = ImageQualityMetrics.Score(frame);

        Assert.Equal(0, scores.Sharpness, precision: 10);
        Assert.Equal(0, scores.EdgeAliasing, precision: 10);
    }

    [Fact]
    public void Score_RampedEdge_ScoresLowerAliasingThanStepEdge()
    {
        var step = Frame(32, 32, (x, y) => x + y >= 32 ? White : Black);

        // Same diagonal, but the two pixels straddling the edge ramp between the extremes.
        var ramped = Frame(32, 32, (x, y) =>
        {
            var distance = x + y - 32;
            return distance switch
            {
                0 => (128, 128, 128, 255),
                1 => (128, 128, 128, 255),
                > 1 => White,
                _ => Black,
            };
        });

        var stepScores = ImageQualityMetrics.Score(step);
        var rampedScores = ImageQualityMetrics.Score(ramped);

        Assert.True(
            rampedScores.EdgeAliasing < stepScores.EdgeAliasing,
            $"ramped {rampedScores.EdgeAliasing} should score lower than step {stepScores.EdgeAliasing}");
    }

    [Fact]
    public void Score_StepEdge_HighAliasing()
    {
        var frame = Frame(32, 32, (x, y) => x + y >= 32 ? White : Black);

        var scores = ImageQualityMetrics.Score(frame);

        Assert.True(scores.EdgeAliasing > 0.5, $"step edge aliasing was {scores.EdgeAliasing}");
        Assert.True(scores.Sharpness > 0, "a step edge must register gradient energy");
    }

    [Fact]
    public void Score_SharpCheckerboard_ScoresHigherSharpnessThanBlurred()
    {
        var sharp = Frame(32, 32, (x, y) => (x / 4 + y / 4) % 2 == 0 ? White : Black);

        // 3x3 box blur of the same checkerboard.
        var blurred = Blur(sharp, width: 32, height: 32);

        var sharpScores = ImageQualityMetrics.Score(sharp);
        var blurredScores = ImageQualityMetrics.Score(blurred);

        Assert.True(
            sharpScores.Sharpness > blurredScores.Sharpness,
            $"sharp {sharpScores.Sharpness} should exceed blurred {blurredScores.Sharpness}");
    }

    [Fact]
    public void Score_BgraFrame_MatchesRgbaScores()
    {
        var rgba = Frame(24, 24, (x, y) => ((byte)((x * 7) % 256), (byte)((y * 11) % 256), (byte)((x * y) % 256), 255));
        var bgra = Frame(24, 24, (x, y) => ((byte)((x * 7) % 256), (byte)((y * 11) % 256), (byte)((x * y) % 256), 255), bgra: true);

        var rgbaScores = ImageQualityMetrics.Score(rgba);
        var bgraScores = ImageQualityMetrics.Score(bgra);

        Assert.Equal(rgbaScores.Sharpness, bgraScores.Sharpness, precision: 10);
        Assert.Equal(rgbaScores.EdgeAliasing, bgraScores.EdgeAliasing, precision: 10);
    }

    [Fact]
    public void Score_Region_MatchesScoreOfCroppedFrame()
    {
        var region = new Panopticon.Mathematics.Rectangle(8, 8, 16, 16);
        var full = Frame(32, 32, (x, y) => ((byte)((x * 13 + y * 5) % 256), (byte)((x * 3) % 256), (byte)((y * 7) % 256), 255));
        var cropped = Frame(16, 16, (x, y) =>
        {
            var source = full.PixelData;
            var stride = 32 * 4;
            var i = (y + 8) * stride + (x + 8) * 4;
            return (source[i], source[i + 1], source[i + 2], 255);
        });

        var regionScores = ImageQualityMetrics.Score(full, region);
        var croppedScores = ImageQualityMetrics.Score(cropped);

        Assert.Equal(croppedScores.Sharpness, regionScores.Sharpness, precision: 10);
        Assert.Equal(croppedScores.EdgeAliasing, regionScores.EdgeAliasing, precision: 10);
    }

    [Fact]
    public void Score_OutOfBoundsRegion_Throws()
    {
        var frame = Frame(8, 8, (x, y) => White);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImageQualityMetrics.Score(frame, new Panopticon.Mathematics.Rectangle(0, 0, 9, 8)));
    }

    private static readonly (byte R, byte G, byte B, byte A) White = (255, 255, 255, 255);
    private static readonly (byte R, byte G, byte B, byte A) Black = (0, 0, 0, 255);

    private static CapturedFrame Frame(
        int width,
        int height,
        Func<int, int, (byte R, byte G, byte B, byte A)> pixel,
        bool bgra = false)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var i = (y * width + x) * 4;
                data[i] = bgra ? b : r;
                data[i + 1] = g;
                data[i + 2] = bgra ? r : b;
                data[i + 3] = a;
            }
        }

        var format = bgra ? PixelFormat.B8_G8_R8_A8_UNorm : PixelFormat.R8_G8_B8_A8_UNorm;
        return new CapturedFrame(data, width, height, format);
    }

    private static CapturedFrame Blur(CapturedFrame source, int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sums = new int[3];
                var count = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        var i = (ny * width + nx) * 4;
                        sums[0] += source.PixelData[i];
                        sums[1] += source.PixelData[i + 1];
                        sums[2] += source.PixelData[i + 2];
                        count++;
                    }
                }

                var o = (y * width + x) * 4;
                data[o] = (byte)(sums[0] / count);
                data[o + 1] = (byte)(sums[1] / count);
                data[o + 2] = (byte)(sums[2] / count);
                data[o + 3] = 255;
            }
        }

        return new CapturedFrame(data, width, height, PixelFormat.R8_G8_B8_A8_UNorm);
    }
}
