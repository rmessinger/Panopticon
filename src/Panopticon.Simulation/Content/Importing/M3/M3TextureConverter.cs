#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Panopticon.FileFormats.M3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Panopticon.Content.Importing.M3;

internal enum M3TextureConversionRule
{
    BaseColor,
    Normal,
    Roughness,
    Emissive,
}

internal sealed class M3TextureConverter
{
    private readonly Dictionary<CacheKey, ImportedImageSource> _cache = new();

    public ImportedImageSource Convert(
        string path,
        byte[] ddsBytes,
        M3TextureConversionRule rule,
        float specularity = 0.0f,
        uint channelSelector = 0)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Texture path must not be null or empty.", nameof(path));
        }

        if (ddsBytes is null)
        {
            throw new ArgumentNullException(nameof(ddsBytes));
        }

        var key = new CacheKey(path, rule, specularity, channelSelector);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var dds = ParseDds(ddsBytes);
        var sourcePixels = dds.FourCc switch
        {
            "DXT1" => DxtDecoder.DecodeBc1(dds.TopMipBytes.Span, dds.Width, dds.Height),
            "DXT5" => DxtDecoder.DecodeBc3(dds.TopMipBytes.Span, dds.Width, dds.Height),
            _ => throw new NotSupportedException($"M3 texture '{path}' uses unsupported DDS four-CC '{dds.FourCc}'."),
        };
        var converted = ConvertPixels(sourcePixels, dds.Width, dds.Height, rule, specularity, channelSelector);
        var png = EncodePng(converted, dds.Width, dds.Height);
        var source = ImportedImageSource.CreateEmbeddedBytes(png, "image/png", $"{path}#{rule}");
        _cache.Add(key, source);
        return source;
    }

    private static byte[] ConvertPixels(
        byte[] source,
        int width,
        int height,
        M3TextureConversionRule rule,
        float specularity,
        uint channelSelector)
    {
        var converted = new byte[source.Length];
        var pixelCount = checked(width * height);
        var r0 = rule == M3TextureConversionRule.Roughness
            ? MathF.Pow(2.0f / (specularity + 2.0f), 0.25f)
            : 1.0f;

        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            var r = source[offset];
            var g = source[offset + 1];
            var b = source[offset + 2];
            var a = source[offset + 3];

            switch (rule)
            {
                case M3TextureConversionRule.BaseColor:
                    converted[offset] = r;
                    converted[offset + 1] = g;
                    converted[offset + 2] = b;
                    converted[offset + 3] = 255;
                    break;

                case M3TextureConversionRule.Normal:
                    WriteNormal(converted, offset, a, g);
                    break;

                case M3TextureConversionRule.Roughness:
                    var luma = (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255.0f;
                    var roughness = 1.0f + (r0 - 1.0f) * luma;
                    converted[offset] = 255;
                    converted[offset + 1] = ToByte(roughness);
                    converted[offset + 2] = 0;
                    converted[offset + 3] = 255;
                    break;

                case M3TextureConversionRule.Emissive:
                    WriteEmissive(converted, offset, r, g, b, a, channelSelector);
                    break;
            }
        }

        return converted;
    }

    private static void WriteNormal(byte[] destination, int offset, byte alpha, byte green)
    {
        var x = alpha / 255.0f * 2.0f - 1.0f;
        var y = green / 255.0f * 2.0f - 1.0f;
        var z = MathF.Sqrt(MathF.Max(0.0f, 1.0f - x * x - y * y));
        destination[offset] = ToByte(x * 0.5f + 0.5f);
        destination[offset + 1] = ToByte(y * 0.5f + 0.5f);
        destination[offset + 2] = ToByte(z * 0.5f + 0.5f);
        destination[offset + 3] = 255;
    }

    private static void WriteEmissive(
        byte[] destination,
        int offset,
        byte r,
        byte g,
        byte b,
        byte a,
        uint channelSelector)
    {
        switch (channelSelector)
        {
            case 0:
            case 1:
                destination[offset] = r;
                destination[offset + 1] = g;
                destination[offset + 2] = b;
                break;
            case 2:
                destination[offset] = a;
                destination[offset + 1] = a;
                destination[offset + 2] = a;
                break;
            case 3:
                destination[offset] = r;
                destination[offset + 1] = r;
                destination[offset + 2] = r;
                break;
            case 4:
                destination[offset] = g;
                destination[offset + 1] = g;
                destination[offset + 2] = g;
                break;
            case 5:
                destination[offset] = b;
                destination[offset + 1] = b;
                destination[offset + 2] = b;
                break;
            default:
                throw new NotSupportedException($"M3 emissive channel selector {channelSelector} is not supported.");
        }

        destination[offset + 3] = 255;
    }

    private static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static DdsTopMip ParseDds(byte[] bytes)
    {
        if (bytes.Length < 128 ||
            bytes[0] != (byte)'D' ||
            bytes[1] != (byte)'D' ||
            bytes[2] != (byte)'S' ||
            bytes[3] != (byte)' ')
        {
            throw new InvalidDataException("M3 texture is not a DDS file.");
        }

        var headerSize = ReadUInt32(bytes, 4);
        var pixelFormatSize = ReadUInt32(bytes, 76);
        if (headerSize != 124 || pixelFormatSize != 32)
        {
            throw new InvalidDataException("M3 DDS header has an unsupported size.");
        }

        var height = checked((int)ReadUInt32(bytes, 12));
        var width = checked((int)ReadUInt32(bytes, 16));
        var fourCc = System.Text.Encoding.ASCII.GetString(bytes, 84, 4);
        var blockSize = fourCc switch
        {
            "DXT1" => 8,
            "DXT5" => 16,
            _ => throw new NotSupportedException($"M3 texture uses unsupported DDS four-CC '{fourCc}'."),
        };
        var blockWidth = (width + 3) / 4;
        var blockHeight = (height + 3) / 4;
        var byteCount = checked(blockWidth * blockHeight * blockSize);
        if (bytes.Length < 128 + byteCount)
        {
            throw new InvalidDataException(
                $"M3 DDS top mip is truncated: expected {byteCount} bytes, found {bytes.Length - 128}.");
        }

        return new DdsTopMip(width, height, fourCc, bytes.AsMemory(128, byteCount));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp(MathF.Round(value * 255.0f), 0.0f, 255.0f);
    }

    private readonly record struct CacheKey(
        string Path,
        M3TextureConversionRule Rule,
        float Specularity,
        uint ChannelSelector);

    private sealed record DdsTopMip(int Width, int Height, string FourCc, ReadOnlyMemory<byte> TopMipBytes);
}
