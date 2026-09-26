using System;
using System.IO;

namespace Panopticon.FileFormats.M3;

public static class DxtDecoder
{
    public static byte[] DecodeBc1(ReadOnlySpan<byte> blocks, int width, int height)
    {
        return Decode(blocks, width, height, bytesPerBlock: 8, hasExplicitAlpha: false);
    }

    public static byte[] DecodeBc3(ReadOnlySpan<byte> blocks, int width, int height)
    {
        return Decode(blocks, width, height, bytesPerBlock: 16, hasExplicitAlpha: true);
    }

    private static byte[] Decode(ReadOnlySpan<byte> blocks, int width, int height, int bytesPerBlock, bool hasExplicitAlpha)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        var blockWidth = (width + 3) / 4;
        var blockHeight = (height + 3) / 4;
        var expectedBytes = checked(blockWidth * blockHeight * bytesPerBlock);
        if (blocks.Length < expectedBytes)
        {
            throw new InvalidDataException(
                $"DXT payload has {blocks.Length} bytes, expected at least {expectedBytes} for {width}x{height}.");
        }

        var pixels = new byte[checked(width * height * 4)];
        for (var by = 0; by < blockHeight; by++)
        {
            for (var bx = 0; bx < blockWidth; bx++)
            {
                var blockOffset = (by * blockWidth + bx) * bytesPerBlock;
                if (hasExplicitAlpha)
                {
                    DecodeBc3Block(blocks.Slice(blockOffset, bytesPerBlock), pixels, width, height, bx * 4, by * 4);
                }
                else
                {
                    DecodeBc1Block(blocks.Slice(blockOffset, bytesPerBlock), pixels, width, height, bx * 4, by * 4, forceFourColor: false);
                }
            }
        }

        return pixels;
    }

    private static void DecodeBc3Block(
        ReadOnlySpan<byte> block,
        byte[] pixels,
        int width,
        int height,
        int x,
        int y)
    {
        Span<byte> alphas = stackalloc byte[8];
        alphas[0] = block[0];
        alphas[1] = block[1];
        if (alphas[0] > alphas[1])
        {
            for (var i = 1; i <= 6; i++)
            {
                alphas[i + 1] = (byte)(((7 - i) * alphas[0] + i * alphas[1]) / 7);
            }
        }
        else
        {
            for (var i = 1; i <= 4; i++)
            {
                alphas[i + 1] = (byte)(((5 - i) * alphas[0] + i * alphas[1]) / 5);
            }

            alphas[6] = 0;
            alphas[7] = 255;
        }

        Span<byte> alphaValues = stackalloc byte[16];
        ulong alphaBits = 0;
        for (var i = 0; i < 6; i++)
        {
            alphaBits |= (ulong)block[2 + i] << (8 * i);
        }

        for (var i = 0; i < 16; i++)
        {
            alphaValues[i] = alphas[(int)((alphaBits >> (3 * i)) & 0x7)];
        }

        DecodeBc1Block(block[8..], pixels, width, height, x, y, forceFourColor: true, alphaValues);
    }

    private static void DecodeBc1Block(
        ReadOnlySpan<byte> block,
        byte[] pixels,
        int width,
        int height,
        int x,
        int y,
        bool forceFourColor,
        ReadOnlySpan<byte> explicitAlpha = default)
    {
        var color0 = (ushort)(block[0] | (block[1] << 8));
        var color1 = (ushort)(block[2] | (block[3] << 8));

        Span<byte> colors = stackalloc byte[16];
        UnpackRgb565(color0, colors[..4]);
        UnpackRgb565(color1, colors.Slice(4, 4));

        if (forceFourColor || color0 > color1)
        {
            Interpolate(colors[..4], colors.Slice(4, 4), colors.Slice(8, 4), 2, 1, 3);
            Interpolate(colors[..4], colors.Slice(4, 4), colors.Slice(12, 4), 1, 2, 3);
        }
        else
        {
            Interpolate(colors[..4], colors.Slice(4, 4), colors.Slice(8, 4), 1, 1, 2);
            colors[12] = 0;
            colors[13] = 0;
            colors[14] = 0;
            colors[15] = 0;
        }

        var indices = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));
        for (var py = 0; py < 4; py++)
        {
            var dstY = y + py;
            if (dstY >= height)
            {
                continue;
            }

            for (var px = 0; px < 4; px++)
            {
                var dstX = x + px;
                if (dstX >= width)
                {
                    continue;
                }

                var pixelIndex = py * 4 + px;
                var colorIndex = (int)((indices >> (2 * pixelIndex)) & 0x3);
                var sourceOffset = colorIndex * 4;
                var destinationOffset = (dstY * width + dstX) * 4;
                pixels[destinationOffset] = colors[sourceOffset];
                pixels[destinationOffset + 1] = colors[sourceOffset + 1];
                pixels[destinationOffset + 2] = colors[sourceOffset + 2];
                pixels[destinationOffset + 3] = explicitAlpha.IsEmpty ? colors[sourceOffset + 3] : explicitAlpha[pixelIndex];
            }
        }
    }

    private static void UnpackRgb565(ushort value, Span<byte> destination)
    {
        destination[0] = (byte)((((value >> 11) & 0x1F) * 255 + 15) / 31);
        destination[1] = (byte)((((value >> 5) & 0x3F) * 255 + 31) / 63);
        destination[2] = (byte)(((value & 0x1F) * 255 + 15) / 31);
        destination[3] = 255;
    }

    private static void Interpolate(
        ReadOnlySpan<byte> color0,
        ReadOnlySpan<byte> color1,
        Span<byte> destination,
        int weight0,
        int weight1,
        int divisor)
    {
        destination[0] = (byte)((weight0 * color0[0] + weight1 * color1[0]) / divisor);
        destination[1] = (byte)((weight0 * color0[1] + weight1 * color1[1]) / divisor);
        destination[2] = (byte)((weight0 * color0[2] + weight1 * color1[2]) / divisor);
        destination[3] = 255;
    }
}
