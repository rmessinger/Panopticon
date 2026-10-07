using System;
using Panopticon.Graphics;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class GoldenFrameComparerTests
{
    [Fact]
    public void IdenticalFramesAndScoresPass()
    {
        var frame = Frame(128, 128, (x, y) => ((byte)x, (byte)y, 64, 255));
        var scores = new ImageQualityScores { Sharpness = 0.5, EdgeAliasing = 0.2 };

        var comparison = GoldenFrameComparer.Compare(frame, frame, scores, scores);

        Assert.True(comparison.Passed);
        Assert.Null(comparison.FailureReason);
        Assert.Equal(128 * 128, comparison.PixelsCompared);
        Assert.Equal(0, comparison.DifferingPixels);
        Assert.Equal(0, comparison.MaxChannelDeltaObserved);
    }

    [Fact]
    public void PerturbedBlockExceedingPixelBudgetFails()
    {
        var actual = Frame(128, 128, (x, y) => Perturbed(x, y));
        var golden = Frame(128, 128, (x, y) => ((byte)x, (byte)y, 64, 255));
        var scores = new ImageQualityScores { Sharpness = 0.5, EdgeAliasing = 0.2 };

        static (byte R, byte G, byte B, byte A) Perturbed(int x, int y)
        {
            var r = x / 8 == 4 && y / 8 == 4 ? x + 50 : x;
            return ((byte)r, (byte)y, 64, 255);
        }

        var comparison = GoldenFrameComparer.Compare(actual, golden, scores, scores);

        Assert.False(comparison.Passed);
        Assert.Contains("Differing pixel fraction", comparison.FailureReason);
        Assert.Equal(64, comparison.DifferingPixels);
        Assert.Equal(50, comparison.MaxChannelDeltaObserved);
    }

    [Fact]
    public void SubThresholdDeltasPass()
    {
        // Every pixel moves by 1, below MaxChannelDelta 2: zero differing pixels.
        var actual = Frame(64, 64, (x, y) => ((byte)(x + 1), (byte)y, 64, 255));
        var golden = Frame(64, 64, (x, y) => ((byte)x, (byte)y, 64, 255));
        var scores = new ImageQualityScores { Sharpness = 0.5, EdgeAliasing = 0.2 };

        var comparison = GoldenFrameComparer.Compare(actual, golden, scores, scores);

        Assert.True(comparison.Passed);
        Assert.Equal(0, comparison.DifferingPixels);
        Assert.Equal(1, comparison.MaxChannelDeltaObserved);
    }

    [Fact]
    public void MetricMoveBeyondToleranceFails()
    {
        var frame = Frame(64, 64, (x, y) => ((byte)x, (byte)y, 64, 255));
        var actualScores = new ImageQualityScores { Sharpness = 0.55, EdgeAliasing = 0.2 };
        var goldenScores = new ImageQualityScores { Sharpness = 0.40, EdgeAliasing = 0.2 };

        var comparison = GoldenFrameComparer.Compare(frame, frame, actualScores, goldenScores);

        Assert.False(comparison.Passed);
        Assert.Contains("Sharpness moved by", comparison.FailureReason);
    }

    [Fact]
    public void SceneToleranceOverridesApply()
    {
        var actual = Frame(64, 64, (x, y) => ((byte)(x + 4), (byte)y, 64, 255));
        var golden = Frame(64, 64, (x, y) => ((byte)x, (byte)y, 64, 255));
        var scores = new ImageQualityScores { Sharpness = 0.5, EdgeAliasing = 0.2 };

        var defaultComparison = GoldenFrameComparer.Compare(actual, golden, scores, scores);
        var relaxed = GoldenFrameComparer.Compare(
            actual, golden, scores, scores, new GoldenTolerance { MaxChannelDelta = 4 });

        Assert.False(defaultComparison.Passed);
        Assert.True(relaxed.Passed);
    }

    [Fact]
    public void MismatchedSizesFailWithReason()
    {
        var actual = Frame(64, 64, pixel: (x, y) => ((byte)x, (byte)y, 64, 255));
        var golden = Frame(32, 32, (x, y) => ((byte)x, (byte)y, 64, 255));
        var scores = new ImageQualityScores { Sharpness = 0.5, EdgeAliasing = 0.2 };

        var comparison = GoldenFrameComparer.Compare(actual, golden, scores, scores);

        Assert.False(comparison.Passed);
        Assert.StartsWith("Size mismatch", comparison.FailureReason);
        Assert.Equal(0, comparison.PixelsCompared);
    }

    private static CapturedFrame Frame(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var i = (y * width + x) * 4;
                pixels[i] = r;
                pixels[i + 1] = g;
                pixels[i + 2] = b;
                pixels[i + 3] = a;
            }
        }

        return new CapturedFrame(pixels, width, height, PixelFormat.R8_G8_B8_A8_UNorm);
    }
}
