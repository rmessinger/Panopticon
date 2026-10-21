using System;
using Veldrid;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Graphics;

public static class TemporalImageQualityMetrics
{
    /// <summary>
    /// RMS change in luminance error relative to matching supersampled frames, in [0, 1].
    /// Subtracting reference motion avoids treating all camera-induced change as shimmer.
    /// This screen-space proxy is not a world-reprojected or perceptual temporal metric.
    /// </summary>
    public static double Score(
        CapturedFrame previous,
        CapturedFrame current,
        CapturedFrame previousReference,
        CapturedFrame currentReference,
        Rectangle region)
    {
        Validate(previous, region);
        Validate(current, region);
        Validate(previousReference, region);
        Validate(currentReference, region);
        if (previous.Width != current.Width || previous.Height != current.Height
            || previousReference.Width != current.Width || previousReference.Height != current.Height
            || currentReference.Width != current.Width || currentReference.Height != current.Height)
        {
            throw new ArgumentException("Temporal frames must have matching dimensions.");
        }

        double sum = 0;
        for (var y = region.Y; y < region.Y + region.Height; y++)
        {
            for (var x = region.X; x < region.X + region.Width; x++)
            {
                var offset = (y * current.Width + x) * 4;
                var error = Luminance(current, offset) - Luminance(currentReference, offset)
                    - Luminance(previous, offset) + Luminance(previousReference, offset);
                sum += error * error;
            }
        }

        return Math.Sqrt(sum / ((double)region.Width * region.Height)) / 2;
    }

    private static void Validate(CapturedFrame frame, Rectangle region)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || (long)region.X + region.Width > frame.Width || (long)region.Y + region.Height > frame.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }

        if (frame.Format is not (PixelFormat.R8_G8_B8_A8_UNorm or PixelFormat.R8_G8_B8_A8_UNorm_SRgb
            or PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb))
        {
            throw new NotSupportedException($"Pixel format {frame.Format} is not supported.");
        }

        if (frame.PixelData.LongLength != (long)frame.Width * frame.Height * 4)
        {
            throw new ArgumentException("Temporal frame pixel buffer has an invalid length.", nameof(frame));
        }
    }

    private static double Luminance(CapturedFrame frame, int offset)
    {
        var bgra = frame.Format is PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
        var pixels = frame.PixelData;
        return (0.2126 * pixels[offset + (bgra ? 2 : 0)] + 0.7152 * pixels[offset + 1]
            + 0.0722 * pixels[offset + (bgra ? 0 : 2)]) / 255;
    }
}
