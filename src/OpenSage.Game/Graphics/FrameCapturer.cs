using System;
using Veldrid;

namespace OpenSage.Graphics;

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
