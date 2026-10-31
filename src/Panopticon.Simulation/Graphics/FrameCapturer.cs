using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace Panopticon.Graphics;

/// <summary>
/// Reads a rendered GPU texture back to CPU memory.
/// </summary>
public static class FrameCapturer
{
    /// <summary>
    /// Copies <paramref name="source"/> from GPU to CPU and returns the raw pixel bytes.
    /// Blocks until the GPU has finished copying.
    /// </summary>
    public static unsafe CapturedFrame Capture(GraphicsDevice graphicsDevice, Texture source)
    {
        var width = source.Width;
        var height = source.Height;
        var format = source.Format;
        var bytesPerPixel = GetBytesPerPixel(format);

        using var staging = graphicsDevice.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(width, height, 1, 1, format, TextureUsage.Staging));

        using var commandList = graphicsDevice.ResourceFactory.CreateCommandList();
        commandList.Begin();
        commandList.CopyTexture(
            source, 0, 0, 0, 0, 0,
            staging, 0, 0, 0, 0, 0,
            width, height, 1, 1);
        commandList.End();

        graphicsDevice.SubmitCommands(commandList);
        graphicsDevice.WaitForIdle();

        var mapped = graphicsDevice.Map(staging, MapMode.Read, 0);

        // Row pitch may be padded (e.g. Metal requires 256-byte alignment).
        // Copy row-by-row to produce a tightly-packed pixel array.
        var pixels = new byte[width * height * bytesPerPixel];
        var rowBytes = (int)width * bytesPerPixel;
        for (var y = 0; y < (int)height; y++)
        {
            var src = (byte*)mapped.Data + y * (int)mapped.RowPitch;
            fixed (byte* dst = &pixels[y * rowBytes])
            {
                Buffer.MemoryCopy(src, dst, rowBytes, rowBytes);
            }
        }

        graphicsDevice.Unmap(staging, 0);

        return new CapturedFrame(pixels, (int)width, (int)height, format);
    }

    /// <summary>
    /// Captures <paramref name="source"/> and writes it to <paramref name="path"/> as a PNG.
    /// </summary>
    public static void CaptureToPng(GraphicsDevice graphicsDevice, Texture source, string path)
    {
        var frame = Capture(graphicsDevice, source);
        using var image = ToImage(frame);
        image.SaveAsPng(path);
    }

    /// <summary>
    /// Converts a captured frame's raw pixels into an RGBA image, normalizing BGRA sources.
    /// </summary>
    public static Image<Rgba32> ToImage(CapturedFrame frame)
    {
        var image = new Image<Rgba32>(frame.Width, frame.Height);
        var isBgra = frame.Format is PixelFormat.B8_G8_R8_A8_UNorm
                                  or PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
        var pixels = frame.PixelData;
        var stride = frame.Width * 4;

        for (var y = 0; y < frame.Height; y++)
        {
            var row = image.Frames[0].PixelBuffer.DangerousGetRowSpan(y);
            for (var x = 0; x < frame.Width; x++)
            {
                var i = y * stride + x * 4;
                var r = pixels[isBgra ? i + 2 : i];
                var g = pixels[i + 1];
                var b = pixels[isBgra ? i : i + 2];

                row[x] = new Rgba32(r, g, b, pixels[i + 3]);
            }
        }

        return image;
    }

    private static int GetBytesPerPixel(PixelFormat format) => format switch
    {
        PixelFormat.R8_G8_B8_A8_UNorm => 4,
        PixelFormat.R8_G8_B8_A8_UNorm_SRgb => 4,
        PixelFormat.B8_G8_R8_A8_UNorm => 4,
        PixelFormat.B8_G8_R8_A8_UNorm_SRgb => 4,
        _ => throw new NotSupportedException($"Pixel format {format} is not supported for frame capture."),
    };
}

/// <summary>
/// A frame captured from the GPU. Pixel data is tightly packed in the format described by
/// <see cref="Format"/>.
/// </summary>
public sealed class CapturedFrame
{
    public byte[] PixelData { get; }
    public int Width { get; }
    public int Height { get; }
    public PixelFormat Format { get; }

    internal CapturedFrame(byte[] pixelData, int width, int height, PixelFormat format)
    {
        PixelData = pixelData;
        Width = width;
        Height = height;
        Format = format;
    }
}
