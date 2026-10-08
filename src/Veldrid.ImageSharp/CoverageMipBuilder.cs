using System;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Veldrid.ImageSharp;

/// <summary>
/// Builds mip chains whose alpha keeps the fraction of texels passing the cutout
/// cutoff constant across levels (WI-G5-02). RGB matches the regular Lanczos3
/// chain; each level's alpha is rescaled by one bounded factor found by binary
/// search so downsampling cannot erode thin cutout silhouettes. The rescale is
/// baked into the stored alpha; the runtime cutoff stays a single constant.
/// </summary>
public static class CoverageMipBuilder
{
    private const int ScaleSearchSteps = 24;
    private const float MaxScale = 8f;

    public static Image<Rgba32>[] GenerateCoverageMipmaps(
        Image<Rgba32> baseImage, float cutoff, bool linearRgb = false)
    {
        var width = baseImage.Width;
        var height = baseImage.Height;
        var levels = MipmapHelper.ComputeMipLevels(width, height);
        var chain = new Image<Rgba32>[levels];
        chain[0] = baseImage;

        using var linearBase = linearRgb ? LinearMipBuilder.CloneLinear(baseImage) : null;
        for (var level = 1; level < levels; level++)
        {
            var newWidth = MipmapHelper.GetDimension(width, level);
            var newHeight = MipmapHelper.GetDimension(height, level);

            // Same direct-from-base Lanczos3 resize as the standard chain, so RGB
            // and unrescaled alpha are identical to it; the linear variant filters
            // RGB in linear light and re-encodes for sRGB storage (WI-G6-02).
            var image = linearBase != null
                ? LinearMipBuilder.ResizeLinearLevel(linearBase, newWidth, newHeight)
                : baseImage.Clone(context => context.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
            RescaleAlpha(image, chain[level - 1], cutoff);
            chain[level] = image;
        }

        return chain;
    }

    private static void RescaleAlpha(Image<Rgba32> image, Image<Rgba32> reference, float cutoff)
    {
        if (!image.DangerousTryGetSinglePixelMemory(out var imageSpan)
            || !reference.DangerousTryGetSinglePixelMemory(out var referenceSpan))
        {
            throw new InvalidOperationException("Coverage mip generation requires single-memory pixel buffers.");
        }

        // The target is the previous level's passing fraction applied to this
        // level's texel count; coverage is a fraction, not an absolute count.
        var target = (long)Math.Round(
            (double)CountPassing(referenceSpan.Span, cutoff) * imageSpan.Length / referenceSpan.Length);
        var pixels = imageSpan.Span;

        // Coverage is monotone in the scale factor, so a binary search over
        // [0, MaxScale] finds the factor whose passing count best matches the
        // previous level. Scales above 1 restore coverage that Lanczos blurs
        // away; scales below 1 trim coverage the resize gained.
        var lowScale = 0f;
        var highScale = MaxScale;
        var bestScale = 1f;
        var bestError = long.MaxValue;

        for (var step = 0; step < ScaleSearchSteps; step++)
        {
            var mid = (lowScale + highScale) / 2f;
            var error = Math.Abs(CountPassingScaled(pixels, cutoff, mid) - target);
            if (error < bestError)
            {
                bestError = error;
                bestScale = mid;
            }

            if (CountPassingScaled(pixels, cutoff, mid) < target)
            {
                lowScale = mid;
            }
            else
            {
                highScale = mid;
            }
        }

        if (Math.Abs(bestScale - 1f) < 0.001f)
        {
            return;
        }

        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            var alpha = (byte)Math.Clamp(MathF.Round(pixel.A * bestScale), 0, 255);
            pixels[i] = new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
        }
    }

    private static long CountPassing(ReadOnlySpan<Rgba32> pixels, float cutoff)
    {
        long count = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].A / 255f >= cutoff)
            {
                count++;
            }
        }

        return count;
    }

    private static long CountPassingScaled(ReadOnlySpan<Rgba32> pixels, float cutoff, float scale)
    {
        long count = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (Math.Min(pixels[i].A * scale, 255f) / 255f >= cutoff)
            {
                count++;
            }
        }

        return count;
    }
}
