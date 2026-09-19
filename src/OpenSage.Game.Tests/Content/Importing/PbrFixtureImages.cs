#nullable enable

using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OpenSage.Tests.Content.Importing;

/// <summary>
/// Deterministic fixture images: fixed dimensions, pixels, and encoder settings.
/// </summary>
internal static class PbrFixtureImages
{
    public static byte[] BaseColorPng { get; } = CreatePng();

    public static byte[] EmissiveJpeg { get; } = CreateJpeg();

    private static Image<Rgba32> CreatePattern()
    {
        // 4x4 checker in fixed colors.
        var image = new Image<Rgba32>(4, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var even = (x / 2 + y / 2) % 2 == 0;
                image[x, y] = even ? new Rgba32(200, 40, 40, 255) : new Rgba32(40, 200, 80, 255);
            }
        }

        return image;
    }

    private static byte[] CreatePng()
    {
        using var image = CreatePattern();
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static byte[] CreateJpeg()
    {
        using var image = CreatePattern();
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }
}
