using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Veldrid.ImageSharp;

public static class PrefilteredTerrainMipBuilder
{
    public const string AlgorithmVersion = "linear-lanczos-area-tent-mix020-v2";

    public static Image<Rgba32>[] Generate(Image<Rgba32> source, int arraySize)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width != source.Height || arraySize < 1 || arraySize > 4096)
        {
            throw new ArgumentException("Terrain mip inputs must be square, with a target size in [1, 4096].");
        }

        var levels = new List<Image<Rgba32>>();
        using var linear = LinearMipBuilder.CloneLinear(source);
        var originalBase = source.Width == arraySize ? source.Clone() : LinearMipBuilder.ResizeLinear(source, arraySize, arraySize);
        var originalLevels = LinearMipBuilder.GenerateLinearMipmaps(originalBase);
        try
        {
            for (var size = arraySize; ; size = Math.Max(1, size / 2))
            {
                using var resized = ResizePeriodic(linear, size);
                using var filtered = FilterPeriodic(resized);
                using var original = LinearMipBuilder.CloneLinear(originalLevels[levels.Count]);
                original.ProcessPixelRows(filtered, (baseline, candidate) =>
                {
                    for (var y = 0; y < baseline.Height; y++)
                    {
                        var baselineRow = baseline.GetRowSpan(y);
                        var candidateRow = candidate.GetRowSpan(y);
                        for (var x = 0; x < baseline.Width; x++)
                        {
                            candidateRow[x] = ToPixel(Vector4.Lerp(baselineRow[x].ToVector4(), candidateRow[x].ToVector4(), 0.2f));
                        }
                    }
                });
                levels.Add(LinearMipBuilder.EncodeToRgba32(filtered));
                if (size == 1)
                {
                    return levels.ToArray();
                }
            }
        }
        catch
        {
            foreach (var level in levels)
            {
                level.Dispose();
            }

            throw;
        }
        finally
        {
            foreach (var level in originalLevels)
            {
                level.Dispose();
            }
        }
    }

    private static Image<RgbaVector> ResizePeriodic(Image<RgbaVector> source, int size)
    {
        var result = new Image<RgbaVector>(size, size);
        var ratio = source.Width / (float)size;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                Vector4 value;
                if (size <= source.Width)
                {
                    value = Vector4.Zero;
                    var left = x * ratio;
                    var right = (x + 1) * ratio;
                    var top = y * ratio;
                    var bottom = (y + 1) * ratio;
                    for (var sy = (int)top; sy < MathF.Ceiling(bottom); sy++)
                    {
                        var wy = MathF.Min(sy + 1, bottom) - MathF.Max(sy, top);
                        for (var sx = (int)left; sx < MathF.Ceiling(right); sx++)
                        {
                            var wx = MathF.Min(sx + 1, right) - MathF.Max(sx, left);
                            value += Pixel(source, sx, sy) * (wx * wy);
                        }
                    }

                    value /= ratio * ratio;
                }
                else
                {
                    var sx = (x + 0.5f) * ratio - 0.5f;
                    var sy = (y + 0.5f) * ratio - 0.5f;
                    var left = (int)MathF.Floor(sx);
                    var top = (int)MathF.Floor(sy);
                    var tx = sx - left;
                    var ty = sy - top;
                    var bottomRow = Vector4.Lerp(Pixel(source, left, top), Pixel(source, left + 1, top), tx);
                    var topRow = Vector4.Lerp(Pixel(source, left, top + 1), Pixel(source, left + 1, top + 1), tx);
                    value = Vector4.Lerp(bottomRow, topRow, ty);
                }

                result[x, y] = ToPixel(value);
            }
        }

        return result;
    }

    private static Image<RgbaVector> FilterPeriodic(Image<RgbaVector> source)
    {
        using var horizontal = new Image<RgbaVector>(source.Width, source.Height);
        var result = new Image<RgbaVector>(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                horizontal[x, y] = ToPixel(Pixel(source, x - 1, y) * 0.125f
                    + Pixel(source, x, y) * 0.75f + Pixel(source, x + 1, y) * 0.125f);
            }
        }

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                result[x, y] = ToPixel(Pixel(horizontal, x, y - 1) * 0.125f
                    + Pixel(horizontal, x, y) * 0.75f + Pixel(horizontal, x, y + 1) * 0.125f);
            }
        }

        return result;
    }

    private static Vector4 Pixel(Image<RgbaVector> image, int x, int y) =>
        image[(x % image.Width + image.Width) % image.Width, (y % image.Height + image.Height) % image.Height].ToVector4();

    private static RgbaVector ToPixel(Vector4 value) => new(value.X, value.Y, value.Z, value.W);
}
