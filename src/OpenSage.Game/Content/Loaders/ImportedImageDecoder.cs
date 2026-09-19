#nullable enable

using System;
using System.IO;
using OpenSage.Content.Importing;
using OpenSage.Data.Tga;
using OpenSage.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using IOFileSystem = OpenSage.IO.FileSystem;

namespace OpenSage.Content.Loaders;

/// <summary>
/// Engine-owned CPU image decoder for imported image sources. External sources open exactly the
/// normalized virtual path (no SAGE texture-path resolution); embedded sources decode their
/// private byte copy. PNG, JPEG, and TGA decode to RGBA8 without touching a GraphicsDevice.
/// DDS stays on the W3D texture path.
/// </summary>
internal static class ImportedImageDecoder
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] DdsMagic = [0x44, 0x44, 0x53, 0x20];

    public static DecodedImportedImage Decode(
        ImportedImageSource source,
        IOFileSystem fileSystem,
        bool generateMipmaps = false)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (fileSystem is null)
        {
            throw new ArgumentNullException(nameof(fileSystem));
        }

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
