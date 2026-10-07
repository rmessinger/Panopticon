using System;
using Veldrid;

namespace Panopticon.Graphics;

/// <summary>
/// CPU-side box resampling of captured frames. Used by the visual regression harness to build
/// supersampled references: the same scene captured at twice the resolution, reduced back, so
/// antialiasing improvements can be scored against a near-oracle image.
/// </summary>
public static class FrameDownsampler
{
    /// <summary>
    /// Halves <paramref name="frame"/> with a 2x2 box filter (each output pixel is the average
    /// of its four source pixels, per channel, rounded to nearest). Preserves the pixel format;
    /// both dimensions must be even.
    /// </summary>
    public static CapturedFrame Downsample2x2(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Width % 2 != 0 || frame.Height % 2 != 0)
        {
            throw new ArgumentException("Both frame dimensions must be even.", nameof(frame));
        }

        var source = frame.PixelData;
        var sourceStride = frame.Width * 4;
        var width = frame.Width / 2;
        var height = frame.Height / 2;
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sx = x * 2;
                var sy = y * 2;

                var i0 = sy * sourceStride + sx * 4;
                var i1 = i0 + 4;
                var i2 = i0 + sourceStride;
                var i3 = i2 + 4;

                var o = (y * width + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    // (v + 2) >> 2 rounds the 4-sample average to nearest instead of truncating.
                    pixels[o + c] = (byte)((source[i0 + c] + source[i1 + c] + source[i2 + c] + source[i3 + c] + 2) >> 2);
                }
            }
        }

        return new CapturedFrame(pixels, width, height, frame.Format);
    }
}
