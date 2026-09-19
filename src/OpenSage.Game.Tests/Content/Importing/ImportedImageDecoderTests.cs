using System;
using System.IO;
using System.Text;
using OpenSage.Content.Importing;
using OpenSage.Content.Loaders;
using OpenSage.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public sealed class ImportedImageDecoderTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    public void ExternalAndEmbedded_DecodeToSameRgba8(string format)
    {
        var encoded = EncodeTestImage(format, out var width, out var height);

        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile($"models/textures/base.{format}", encoded);

        var external = ImportedImageDecoder.Decode(
            ImportedImageSource.CreateVirtualPath($"models/textures/base.{format}"),
            fileSystem);
        var embedded = ImportedImageDecoder.Decode(
            ImportedImageSource.CreateEmbeddedBytes(encoded, format == "png" ? "image/png" : "image/jpeg", "helmet.glb#image-2"),
            fileSystem);

        Assert.Equal(width, external.Width);
        Assert.Equal(height, external.Height);
        Assert.Equal(external.RgbaPixels.ToArray(), embedded.RgbaPixels.ToArray());
        Assert.Equal("models/textures/base." + format, external.DiagnosticName);
        Assert.Equal("helmet.glb#image-2", embedded.DiagnosticName);

        if (format == "png")
        {
            // JPEG is lossy, so exact pixel values are only asserted for the PNG round trip.
            var pixels = external.RgbaPixels.ToArray();
            Assert.Equal(255, pixels[0]);
            Assert.Equal(0, pixels[1]);
            Assert.Equal(0, pixels[2]);
            Assert.Equal(255, pixels[3]);
            Assert.Equal(255, pixels[(width * 4) + 2]);
        }
    }

    [Fact]
    public void ExternalSource_OpensOnlyTheExactPath()
    {
        var encoded = EncodeTestImage("png", out _, out _);
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("textures/base.png", encoded);

        var decoded = ImportedImageDecoder.Decode(
            ImportedImageSource.CreateVirtualPath("textures/base.png"),
            fileSystem);
        Assert.Equal(2, decoded.Width);

        // No extension probing: the exact path only, no SAGE texture-path fallback.
        Assert.Throws<FileNotFoundException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateVirtualPath("textures/base"),
                fileSystem));
    }

    [Fact]
    public void MissingExternalSource_FailsWithContext()
    {
        var fileSystem = new MemoryFileSystem();

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateVirtualPath("textures/absent.png"),
                fileSystem));
        Assert.Contains("textures/absent.png", exception.Message);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public void EmbeddedMimeMismatch_FailsWithContext(string declaredMime)
    {
        var encoded = EncodeTestImage(declaredMime == "image/png" ? "jpg" : "png", out _, out _);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateEmbeddedBytes(encoded, declaredMime, "helmet.glb#image-0"),
                new MemoryFileSystem()));
        Assert.Contains("MIME", exception.Message);
    }

    [Fact]
    public void TruncatedPng_FailsWithContext()
    {
        var encoded = EncodeTestImage("png", out _, out _);
        var truncated = new byte[encoded.Length / 2];
        Array.Copy(encoded, truncated, truncated.Length);

        Assert.Throws<InvalidDataException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateEmbeddedBytes(truncated, "image/png", "truncated.png"),
                new MemoryFileSystem()));
    }

    [Fact]
    public void EmptyContent_Fails()
    {
        Assert.Throws<InvalidDataException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateEmbeddedBytes(new byte[] { 0x89 }, "image/png", "almost-empty.png"),
                new MemoryFileSystem()));
    }

    [Fact]
    public void DdsContent_IsExplicitlyUnsupported()
    {
        var ddsBytes = Encoding.ASCII.GetBytes("DDS |fake block-compressed payload.......");

        var exception = Assert.Throws<NotSupportedException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateEmbeddedBytes(ddsBytes, "image/vnd-ms.dds", "crate.dds"),
                new MemoryFileSystem()));
        Assert.Contains("DDS", exception.Message);
    }

    [Fact]
    public void UnrecognizedExternalContent_Fails()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("textures/mystery.bin", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.Throws<InvalidDataException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateVirtualPath("textures/mystery.bin"),
                fileSystem));
    }

    [Theory]
    [InlineData("external")]
    [InlineData("embedded")]
    public void TgaSources_ProduceRgba8Contract(string form)
    {
        var tga = CreateUncompressedRgbTga(width: 2, height: 1, pixels: [(255, 0, 0), (0, 255, 0)]);

        ImportedImageSource source;
        var fileSystem = new MemoryFileSystem();
        if (form == "external")
        {
            fileSystem.AddFile("textures/plate.tga", tga);
            source = ImportedImageSource.CreateVirtualPath("textures/plate.tga");
        }
        else
        {
            source = ImportedImageSource.CreateEmbeddedBytes(tga, "image/x-tga", "helmet.glb#tga-1");
        }

        var decoded = ImportedImageDecoder.Decode(source, fileSystem);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(1, decoded.Height);
        var pixels = decoded.RgbaPixels.ToArray();
        Assert.Equal(255, pixels[0]);
        Assert.Equal(0, pixels[1]);
        Assert.Equal(0, pixels[2]);
        Assert.Equal(255, pixels[3]);
        Assert.Equal(0, pixels[4]);
        Assert.Equal(255, pixels[5]);
        Assert.Equal(0, pixels[6]);
        Assert.Equal(255, pixels[7]);
    }

    [Fact]
    public void TgaSources_RejectMalformedContent()
    {
        // Valid TGA-shaped header claiming 8x8 but no pixel payload follows.
        var malformed = new byte[18];
        malformed[2] = 2;
        malformed[12] = 8;
        malformed[13] = 0;
        malformed[14] = 8;
        malformed[15] = 0;
        malformed[16] = 0x18;

        Assert.Throws<InvalidDataException>(() =>
            ImportedImageDecoder.Decode(
                ImportedImageSource.CreateEmbeddedBytes(malformed, "image/x-tga", "broken.tga"),
                new MemoryFileSystem()));
    }

    private static byte[] EncodeTestImage(string format, out int width, out int height)
    {
        width = 2;
        height = 2;
        using var image = new Image<Rgba32>(width, height);
        image[0, 0] = new Rgba32(255, 0, 0, 255);
        image[1, 0] = new Rgba32(0, 255, 0, 255);
        image[0, 1] = new Rgba32(0, 0, 255, 255);
        image[1, 1] = new Rgba32(255, 255, 0, 255);

        using var stream = new MemoryStream();
        image.Save(stream, format == "png"
            ? new SixLabors.ImageSharp.Formats.Png.PngEncoder()
            : new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
        return stream.ToArray();
    }

    private static byte[] CreateUncompressedRgbTga(
        int width,
        int height,
        params (byte R, byte G, byte B)[] pixels)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)2);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((byte)0);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((short)width);
        writer.Write((short)height);
        writer.Write((byte)24);
        writer.Write((byte)0);

        // TGA stores bottom-up; write rows in reverse.
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                writer.Write(pixel.B);
                writer.Write(pixel.G);
                writer.Write(pixel.R);
            }
        }

        return stream.ToArray();
    }
}
