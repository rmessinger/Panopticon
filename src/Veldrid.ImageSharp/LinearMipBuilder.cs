using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Veldrid.ImageSharp;

/// <summary>
/// Builds colour mip chains that filter in linear light: RGB is decoded before resampling
/// and re-encoded for sRGB storage afterwards, alpha is filtered raw. Counterpart of the
/// encoded-space chain in <see cref="MipmapHelper"/> for the Linear colour space mode (G6).
/// </summary>
public static class LinearMipBuilder
{
    public static Image<Rgba32>[] GenerateLinearMipmaps(Image<Rgba32> baseImage)
    {
        var mipLevels = new Image<Rgba32>[MipmapHelper.ComputeMipLevels(baseImage.Width, baseImage.Height)];
        mipLevels[0] = baseImage;
        var i = 1;

        using var linearBase = CloneLinear(baseImage);

        var currentWidth = baseImage.Width;
        var currentHeight = baseImage.Height;
        while (currentWidth != 1 || currentHeight != 1)
        {
            var newWidth = Math.Max(1, currentWidth / 2);
            var newHeight = Math.Max(1, currentHeight / 2);
            mipLevels[i] = ResizeLinearLevel(linearBase, newWidth, newHeight);

            i++;
            currentWidth = newWidth;
            currentHeight = newHeight;
        }

        return mipLevels;
    }

    public static Image<Rgba32> ResizeLinear(Image<Rgba32> source, int width, int height)
    {
        using var linearSource = CloneLinear(source);

        return ResizeLinearLevel(linearSource, width, height);
    }

    internal static Image<RgbaVector> CloneLinear(Image<Rgba32> source)
    {
        var linear = source.CloneAs<RgbaVector>();

        linear.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new RgbaVector(
                        SrgbTransfer.ToLinear(row[x].R),
                        SrgbTransfer.ToLinear(row[x].G),
                        SrgbTransfer.ToLinear(row[x].B),
                        row[x].A);
                }
            }
        });

        return linear;
    }

    internal static Image<Rgba32> ResizeLinearLevel(Image<RgbaVector> linearSource, int width, int height)
    {
        using var resized = linearSource.Clone(context => context.Resize(width, height, KnownResamplers.Lanczos3));

        return EncodeToRgba32(resized);
    }

    private static Image<Rgba32> EncodeToRgba32(Image<RgbaVector> linearImage)
    {
        if (!linearImage.DangerousTryGetSinglePixelMemory(out var pixelMemory))
        {
            throw new InvalidOperationException("Linear mip source is not contiguous.");
        }

        var encoded = new Image<Rgba32>(linearImage.Width, linearImage.Height);

        encoded.ProcessPixelRows(accessor =>
        {
            var linearSpan = pixelMemory.Span;
            for (var y = 0; y < accessor.Height; y++)
            {
                var targetRow = accessor.GetRowSpan(y);
                var sourceRow = linearSpan.Slice(y * accessor.Width, accessor.Width);
                for (var x = 0; x < sourceRow.Length; x++)
                {
                    targetRow[x] = new Rgba32(
                        SrgbTransfer.ToSrgbByte(sourceRow[x].R),
                        SrgbTransfer.ToSrgbByte(sourceRow[x].G),
                        SrgbTransfer.ToSrgbByte(sourceRow[x].B),
                        ToRawByte(sourceRow[x].A));
                }
            }
        });

        return encoded;
    }

    private static byte ToRawByte(float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);

        return (byte)Math.Round(clamped * 255f, MidpointRounding.AwayFromZero);
    }
}
