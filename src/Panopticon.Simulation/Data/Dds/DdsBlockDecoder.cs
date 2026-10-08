using System;
using Panopticon.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Panopticon.Data.Dds;

/// <summary>
/// Decodes the base mip of a BC-compressed DDS texture to RGBA. Only the cutout
/// rebuild path (WI-G5-02) uses this; the renderer keeps consuming compressed
/// blocks directly.
/// </summary>
internal static class DdsBlockDecoder
{
    public static Image<Rgba32> DecodeBaseMip(DdsFile file)
    {
        return DecodeBaseMip(file.MipMaps[0], file.ImageFormat);
    }

    public static Image<Rgba32> DecodeBaseMip(TextureMipMapData baseMip, DdsImageFormat format)
    {
        return format switch
        {
            DdsImageFormat.Bc1 => DecodeBc1(baseMip),
            DdsImageFormat.Bc2 => DecodeBc2(baseMip),
            DdsImageFormat.Bc3 => DecodeBc3(baseMip),
            _ => throw new NotSupportedException(
                $"Cutout mip rebuild requires a BC1/BC2/BC3 base mip; {format} textures are never cutout."),
        };
    }

    private static Image<Rgba32> DecodeBc1(TextureMipMapData mip)
    {
        var image = new Image<Rgba32>((int)mip.Width, (int)mip.Height);
        var blocksPerRow = (int)((mip.Width + 3) / 4);
        var offset = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (var blockY = 0; blockY < (mip.Height + 3) / 4; blockY++)
            {
                for (var blockX = 0; blockX < blocksPerRow; blockX++)
                {
                    var c0 = (ushort)(mip.Data[offset] | (mip.Data[offset + 1] << 8));
                    var c1 = (ushort)(mip.Data[offset + 2] | (mip.Data[offset + 3] << 8));
                    var indices = (uint)(mip.Data[offset + 4]
                        | (mip.Data[offset + 5] << 8)
                        | (mip.Data[offset + 6] << 16)
                        | (mip.Data[offset + 7] << 24));
                    offset += 8;

                    var palette = ExpandBc1Palette(c0, c1, out var punchthrough);

                    for (var p = 0; p < 16; p++)
                    {
                        var x = blockX * 4 + (p % 4);
                        var y = blockY * 4 + (p / 4);
                        if (x >= mip.Width || y >= mip.Height)
                        {
                            continue;
                        }

                        var index = (indices >> (p * 2)) & 0x3u;
                        var color = palette[index];
                        var alpha = punchthrough && index == 3 ? (byte)0 : (byte)255;
                        accessor.GetRowSpan((int)y)[(int)x] = new Rgba32(color.R, color.G, color.B, alpha);
                    }
                }
            }
        });

        return image;
    }

    private static Image<Rgba32> DecodeBc2(TextureMipMapData mip)
    {
        return DecodeBc2Bc3(mip, explicitAlpha: true);
    }

    private static Image<Rgba32> DecodeBc3(TextureMipMapData mip)
    {
        return DecodeBc2Bc3(mip, explicitAlpha: false);
    }

    private static Image<Rgba32> DecodeBc2Bc3(TextureMipMapData mip, bool explicitAlpha)
    {
        var image = new Image<Rgba32>((int)mip.Width, (int)mip.Height);
        var blocksPerRow = (int)((mip.Width + 3) / 4);
        var offset = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (var blockY = 0; blockY < (mip.Height + 3) / 4; blockY++)
            {
                for (var blockX = 0; blockX < blocksPerRow; blockX++)
                {
                    byte[] alphas;
                    var colorOffset = offset + 8;

                    if (explicitAlpha)
                    {
                        // BC2 stores four bits per texel: eight bytes per block.
                        alphas = new byte[16];
                        for (var i = 0; i < 8; i++)
                        {
                            var pair = mip.Data[offset + i];
                            alphas[i * 2] = (byte)((pair & 0x0F) * 17);
                            alphas[i * 2 + 1] = (byte)((pair >> 4) * 17);
                        }
                    }
                    else
                    {
                        alphas = ExpandBc3Alpha(mip.Data, offset);
                    }

                    var c0 = (ushort)(mip.Data[colorOffset] | (mip.Data[colorOffset + 1] << 8));
                    var c1 = (ushort)(mip.Data[colorOffset + 2] | (mip.Data[colorOffset + 3] << 8));
                    var indices = (uint)(mip.Data[colorOffset + 4]
                        | (mip.Data[colorOffset + 5] << 8)
                        | (mip.Data[colorOffset + 6] << 16)
                        | (mip.Data[colorOffset + 7] << 24));

                    // BC2/BC3 color blocks always run in four-colour mode; index 3 is a
                    // real colour and never transparent.
                    var palette = ExpandBc1Palette(c0, c1, out _);

                    for (var p = 0; p < 16; p++)
                    {
                        var x = blockX * 4 + (p % 4);
                        var y = blockY * 4 + (p / 4);
                        if (x >= mip.Width || y >= mip.Height)
                        {
                            continue;
                        }

                        var index = (indices >> (p * 2)) & 0x3u;
                        var color = palette[index];
                        accessor.GetRowSpan((int)y)[(int)x] = new Rgba32(color.R, color.G, color.B, alphas[p]);
                    }

                    offset += 16;
                }
            }
        });

        return image;
    }

    private static byte[] ExpandBc3Alpha(byte[] data, int offset)
    {
        var a0 = data[offset];
        var a1 = data[offset + 1];
        var alphas = new byte[16];

        Span<byte> palette = stackalloc byte[8];
        palette[0] = a0;
        palette[1] = a1;
        if (a0 > a1)
        {
            for (var i = 0; i < 6; i++)
            {
                palette[i + 2] = (byte)(((6 - i) * a0 + (i + 1) * a1) / 7);
            }
        }
        else
        {
            for (var i = 0; i < 4; i++)
            {
                palette[i + 2] = (byte)(((4 - i) * a0 + (i + 1) * a1) / 5);
            }

            palette[6] = 0;
            palette[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 6; i++)
        {
            bits |= (ulong)data[offset + 2 + i] << (i * 8);
        }

        for (var p = 0; p < 16; p++)
        {
            alphas[p] = palette[(int)((bits >> (p * 3)) & 0x7u)];
        }

        return alphas;
    }

    private static (byte R, byte G, byte B)[] ExpandBc1Palette(ushort c0, ushort c1, out bool punchthrough)
    {
        var color0 = ExpandRgb565(c0);
        var color1 = ExpandRgb565(c1);
        punchthrough = c0 <= c1;

        if (punchthrough)
        {
            return new[]
            {
                color0,
                color1,
                (
                    (byte)((color0.R + color1.R) / 2),
                    (byte)((color0.G + color1.G) / 2),
                    (byte)((color0.B + color1.B) / 2)
                ),
                ((byte)0, (byte)0, (byte)0),
            };
        }

        return new[]
        {
            color0,
            color1,
            (
                (byte)((2 * color0.R + color1.R) / 3),
                (byte)((2 * color0.G + color1.G) / 3),
                (byte)((2 * color0.B + color1.B) / 3)
            ),
            (
                (byte)((color0.R + 2 * color1.R) / 3),
                (byte)((color0.G + 2 * color1.G) / 3),
                (byte)((color0.B + 2 * color1.B) / 3)
            ),
        };
    }

    private static (byte R, byte G, byte B) ExpandRgb565(ushort value)
    {
        var r = (byte)((value >> 11) & 0x1F);
        var g = (byte)((value >> 5) & 0x3F);
        var b = (byte)(value & 0x1F);

        return ((byte)((r << 3) | (r >> 2)), (byte)((g << 2) | (g >> 4)), (byte)((b << 3) | (b >> 2)));
    }
}
