#nullable enable

using System;
using OpenSage.Content.Importing;

namespace OpenSage.Content.Loaders;

/// <summary>
/// CPU-only decode result: immutable RGBA8 pixels, dimensions, mip intent, and source
/// diagnostics. No GPU types; upload and color-space handling happen at materialization.
/// </summary>
public sealed class DecodedImportedImage
{
    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> RgbaPixels { get; }

    public bool GenerateMipmaps { get; }

    public string DiagnosticName { get; }

    internal DecodedImportedImage(
        int width,
        int height,
        byte[] rgbaPixels,
        bool generateMipmaps,
        ImportedImageSource source)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Decoded image dimensions must be positive.");
        }

        if (rgbaPixels.Length != width * height * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(rgbaPixels), "Pixel buffer size does not match dimensions.");
        }

        Width = width;
        Height = height;
        RgbaPixels = rgbaPixels;
        GenerateMipmaps = generateMipmaps;
        DiagnosticName = source.DiagnosticName;
    }
}
