using System;
using Veldrid;

namespace Panopticon.Graphics;

/// <summary>
/// Objective image-quality scores over a captured frame, as pure functions of the pixel buffer.
/// Both scores are normalized to [0, 1]; see the individual members for their formulas.
/// </summary>
public sealed record ImageQualityScores
{
    /// <summary>
    /// Mean squared luminance gradient, normalized by its theoretical maximum, in [0, 1].
    /// Zero for a uniform image; higher values indicate more high-contrast detail.
    /// Comparable only between images of the same dimensions and scene content.
    /// </summary>
    public double Sharpness { get; init; }

    /// <summary>
    /// Fraction of edge pixels at a hard two-value staircase rather than a smooth ramp, in
    /// [0, 1]. Zero for a uniform image (no edges) and for fully ramped edges; a hard
    /// aliased edge scores close to 1.
    /// </summary>
    public double EdgeAliasing { get; init; }
}

/// <summary>
/// Sharpness and edge-aliasing metrics over a <see cref="CapturedFrame"/>. Formula-defined and
/// explainable by design; no perceptual or learned model.
/// </summary>
public static class ImageQualityMetrics
{
    private const double EdgeGradientThreshold = 0.1;

    // Luminance is computed in normalized [0, 1] sRGB units without linearization; both scores
    // compare frames captured by the same pipeline, so a consistent space is sufficient.
    private static readonly double LuminanceRed = 0.2126;
    private static readonly double LuminanceGreen = 0.7152;
    private static readonly double LuminanceBlue = 0.0722;

    /// <summary>
    /// Scores a region of <paramref name="frame"/>. A null <paramref name="region"/> scores the
    /// whole image. Scores are normalized to [0, 1]; see <see cref="ImageQualityScores"/>.
    /// </summary>
    public static ImageQualityScores Score(CapturedFrame frame, in Panopticon.Mathematics.Rectangle? region = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var rect = region ?? new Panopticon.Mathematics.Rectangle(0, 0, frame.Width, frame.Height);
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0
            || rect.X + rect.Width > frame.Width || rect.Y + rect.Height > frame.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "Region is outside the frame bounds.");
        }

        var isBgra = frame.Format is PixelFormat.B8_G8_R8_A8_UNorm
                                  or PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
        if (!isBgra
            && frame.Format is not (PixelFormat.R8_G8_B8_A8_UNorm or PixelFormat.R8_G8_B8_A8_UNorm_SRgb))
        {
            throw new NotSupportedException($"Pixel format {frame.Format} is not supported.");
        }

        var luminance = BuildLuminance(frame, isBgra);

        return Score(luminance, frame.Width, rect);
    }

    private static double[] BuildLuminance(CapturedFrame frame, bool isBgra)
    {
        var pixels = frame.PixelData;
        var luminance = new double[frame.Width * frame.Height];
        var stride = frame.Width * 4;

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var i = y * stride + x * 4;
                double r = isBgra ? pixels[i + 2] : pixels[i];
                double g = pixels[i + 1];
                double b = isBgra ? pixels[i] : pixels[i + 2];

                luminance[y * frame.Width + x] =
                    (LuminanceRed * r + LuminanceGreen * g + LuminanceBlue * b) / 255.0;
            }
        }

        return luminance;
    }

    private static ImageQualityScores Score(double[] luminance, int width, in Panopticon.Mathematics.Rectangle rect)
    {
        var height = rect.Height;
        var bottom = rect.Y + height;

        double gradientEnergySum = 0;
        var gradientEnergyCount = 0;
        var edgePixels = 0;
        var staircaseEdgePixels = 0;

        for (var y = rect.Y; y < bottom; y++)
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                var left = luminance[y * width + Math.Max(x - 1, rect.X)];
                var right = luminance[y * width + Math.Min(x + 1, rect.X + rect.Width - 1)];
                var up = luminance[Math.Max(y - 1, rect.Y) * width + x];
                var down = luminance[Math.Min(y + 1, bottom - 1) * width + x];

                var dx = right - left;
                var dy = down - up;
                var magnitudeSquared = dx * dx + dy * dy;

                gradientEnergySum += magnitudeSquared;
                gradientEnergyCount++;

                if (Math.Sqrt(magnitudeSquared) > EdgeGradientThreshold)
                {
                    edgePixels++;

                    if (IsStaircaseValue(luminance, width, x, y, rect))
                    {
                        staircaseEdgePixels++;
                    }
                }
            }
        }

        if (gradientEnergyCount == 0)
        {
            return new ImageQualityScores();
        }

        // Central differences over [0, 1] luminance span at most 2 per axis, so the squared
        // gradient is at most 8; normalizing by it maps the mean into [0, 1].
        var sharpness = gradientEnergySum / gradientEnergyCount / 8.0;
        var edgeAliasing = edgePixels == 0 ? 0 : (double)staircaseEdgePixels / edgePixels;

        return new ImageQualityScores
        {
            Sharpness = sharpness,
            EdgeAliasing = edgeAliasing,
        };
    }

    private static bool IsStaircaseValue(double[] luminance, int width, int x, int y, in Panopticon.Mathematics.Rectangle rect)
    {
        // An edge pixel counts as a staircase step when its value sits at a local extreme of its
        // 3x3 neighborhood: a two-value staircase never produces intermediate ramp values, while
        // an antialiased edge does. The epsilon is one 8-bit luminance step.
        const double Epsilon = 1.0 / 255.0;

        var value = luminance[y * width + x];
        var min = double.MaxValue;
        var max = double.MinValue;

        for (var ny = Math.Max(y - 1, rect.Y); ny <= Math.Min(y + 1, rect.Y + rect.Height - 1); ny++)
        {
            for (var nx = Math.Max(x - 1, rect.X); nx <= Math.Min(x + 1, rect.X + rect.Width - 1); nx++)
            {
                var neighbour = luminance[ny * width + nx];
                min = Math.Min(min, neighbour);
                max = Math.Max(max, neighbour);
            }
        }

        return value <= min + Epsilon || value >= max - Epsilon;
    }
}
