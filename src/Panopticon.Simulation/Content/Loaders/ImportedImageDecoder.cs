#nullable enable

using System;
using System.IO;
using Panopticon.Content.Importing;
using Panopticon.Data.Dds;
using Panopticon.Data.Tga;
using Panopticon.FileFormats.Bar;
using Panopticon.IO;
using Panopticon.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using IOFileSystem = Panopticon.IO.FileSystem;

namespace Panopticon.Content.Loaders;

/// <summary>
/// Engine-owned CPU image decoder for imported image sources. External sources open exactly the
/// normalized virtual path (no SAGE texture-path resolution); embedded sources decode their
/// private byte copy. PNG, JPEG, and TGA decode to RGBA8 without touching a GraphicsDevice.
/// AoE3 DDT decodes its base mip only; mip regeneration stays in the materializer and stored
/// mips are not uploaded. DDS stays on the W3D texture path.
/// </summary>
internal static class ImportedImageDecoder
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] DdsMagic = [0x44, 0x44, 0x53, 0x20];
    private static readonly byte[] Rts3Magic = [0x52, 0x54, 0x53, 0x33];

    public static DecodedImportedImage Decode(
        ImportedImageSource source,
        IOFileSystem fileSystem,
        bool generateMipmaps = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        ArgumentNullException.ThrowIfNull(fileSystem);

        Stream stream;
        if (source.Kind == ImportedImageSourceKind.VirtualPath)
        {
            var entry = fileSystem.GetFile(source.VirtualPath)
                ?? throw new FileNotFoundException(
                    $"Imported image '{source.VirtualPath}' was not found in the file system.");
            stream = entry.Open();
        }
        else
        {
            stream = new MemoryStream(source.EmbeddedBytes.ToArray());
        }

        using (stream)
        using (var buffered = new BufferedStream(stream))
        {
            return DecodeStream(buffered, source, generateMipmaps);
        }
    }

    private static DecodedImportedImage DecodeStream(
        Stream stream,
        ImportedImageSource source,
        bool generateMipmaps)
    {
        byte[] rgbaPixels;
        int width;
        int height;

        if (StartsWith(stream, DdsMagic))
        {
            throw new NotSupportedException(
                $"Imported image '{source.DiagnosticName}' is DDS-encoded; DDS remains on the W3D texture path.");
        }

        if (StartsWith(stream, PngMagic))
        {
            ValidateMime(source, "image/png", "PNG");
            (rgbaPixels, width, height) = DecodeWithImageSharp(stream, source);
        }
        else if (StartsWith(stream, Rts3Magic))
        {
            (rgbaPixels, width, height) = DecodeDdt(stream, source);
        }
        else if (StartsWith(stream, JpegMagic))
        {
            ValidateMime(source, "image/jpeg", "JPEG");
            (rgbaPixels, width, height) = DecodeWithImageSharp(stream, source);
        }
        else
        {
            ValidateMime(source, "image/x-tga", "TGA");
            if (source.Kind == ImportedImageSourceKind.VirtualPath
                && !source.VirtualPath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Imported image '{source.DiagnosticName}' has no recognizable PNG or JPEG signature and is not a .tga path; the format is unsupported.");
            }

            (rgbaPixels, width, height) = DecodeTga(stream, source);
        }

        return new DecodedImportedImage(width, height, rgbaPixels, generateMipmaps, source);
    }

    private static (byte[] Pixels, int Width, int Height) DecodeDdt(
        Stream stream,
        ImportedImageSource source)
    {
        try
        {
            var ddt = DdtFile.Read(stream);

            byte[] pixels;
            switch (ddt.Format)
            {
                case DdtFormat.Bc1:
                case DdtFormat.Bc1Alpha:
                    pixels = DecodeDdtBlockCompressed(ddt, DdsImageFormat.Bc1);
                    break;
                case DdtFormat.Bc2:
                    pixels = DecodeDdtBlockCompressed(ddt, DdsImageFormat.Bc2);
                    break;
                case DdtFormat.Bc3:
                    pixels = DecodeDdtBlockCompressed(ddt, DdsImageFormat.Bc3);
                    break;
                case DdtFormat.Bgra:
                    pixels = DecodeDdtBgra(ddt);
                    break;
                case DdtFormat.R8:
                    pixels = DecodeDdtR8(ddt);
                    break;
                default:
                    throw new NotSupportedException($"DDT format {ddt.Format} cannot be decoded.");
            }

            return (pixels, (int)ddt.Width, (int)ddt.Height);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or NotSupportedException or IndexOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Imported DDT image '{source.DiagnosticName}' could not be decoded: {exception.Message}.", exception);
        }
    }

    private static byte[] DecodeDdtBlockCompressed(DdtFile ddt, DdsImageFormat format)
    {
        var mip = new TextureMipMapData(
            ddt.GetMipData(0).ToArray(),
            0,
            ddt.Mips[0].Size,
            ddt.Width,
            ddt.Height);

        using var image = DdsBlockDecoder.DecodeBaseMip(mip, format);
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static byte[] DecodeDdtBgra(DdtFile ddt)
    {
        var mip = ddt.GetMipData(0);
        var pixels = new byte[ddt.Width * ddt.Height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = mip[i + 2];
            pixels[i + 1] = mip[i + 1];
            pixels[i + 2] = mip[i];
            pixels[i + 3] = mip[i + 3];
        }

        return pixels;
    }

    private static byte[] DecodeDdtR8(DdtFile ddt)
    {
        var mip = ddt.GetMipData(0);
        var pixels = new byte[ddt.Width * ddt.Height * 4];
        for (var i = 0; i < ddt.Width * ddt.Height; i++)
        {
            pixels[i * 4] = mip[i];
            pixels[i * 4 + 1] = mip[i];
            pixels[i * 4 + 2] = mip[i];
            pixels[i * 4 + 3] = 255;
        }

        return pixels;
    }

    private static (byte[] Pixels, int Width, int Height) DecodeWithImageSharp(
        Stream stream,
        ImportedImageSource source)
    {
        try
        {
            using var image = Image.Load<Rgba32>(stream);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            return (pixels, image.Width, image.Height);
        }
        catch (Exception exception) when (
            exception is UnknownImageFormatException
                or InvalidImageContentException
                or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Imported image '{source.DiagnosticName}' could not be decoded: {exception.Message}.", exception);
        }
    }

    private static (byte[] Pixels, int Width, int Height) DecodeTga(
        Stream stream,
        ImportedImageSource source)
    {
        try
        {
            var tgaFile = TgaFile.FromStream(stream);
            var pixels = TgaFile.ConvertPixelsToRgba8(tgaFile);
            return (pixels, tgaFile.Header.Width, tgaFile.Header.Height);
        }
        catch (Exception exception) when (exception is NotSupportedException or EndOfStreamException or IndexOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Imported TGA image '{source.DiagnosticName}' could not be decoded: {exception.Message}.", exception);
        }
    }

    private static void ValidateMime(ImportedImageSource source, string expectedMime, string formatName)
    {
        if (source.Kind == ImportedImageSourceKind.EmbeddedBytes
            && !string.Equals(source.MimeType, expectedMime, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Imported image '{source.DiagnosticName}' declares MIME type '{source.MimeType}', but its content is {formatName}.");
        }
    }

    private static bool StartsWith(Stream stream, byte[] magic)
    {
        stream.Position = 0;
        var buffer = new byte[magic.Length];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0)
            {
                throw new InvalidDataException("Imported image content is empty or truncated before any format signature.");
            }

            read += count;
        }

        for (var i = 0; i < magic.Length; i++)
        {
            if (buffer[i] != magic[i])
            {
                stream.Position = 0;
                return false;
            }
        }

        stream.Position = 0;
        return true;
    }
}
