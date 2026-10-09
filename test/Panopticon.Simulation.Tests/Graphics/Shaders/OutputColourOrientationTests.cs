using System;
using Panopticon.Graphics;
using Panopticon.Graphics.Shaders;
using Panopticon.Rendering;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics.Shaders;

[Collection("GraphicsTests")]
public sealed class OutputColourOrientationTests
{
    [GraphicsFact]
    public void OutputPass_PreservesAsymmetricTextureRowsAndColumns()
    {
        using var device = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        var output = new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.R8_G8_B8_A8_UNorm));
        using var store = new ShaderSetStore(device, output);
        using var shader = new OutputColourShaderResources(store, output);

        AssertOrientation(device, shader, 8, 6);
        AssertOrientation(device, shader, 13, 9);
    }

    private static void AssertOrientation(GraphicsDevice device, OutputColourShaderResources shader, uint width, uint height)
    {
        using var source = device.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        using var target = device.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        using var framebuffer = device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, target));
        using var commands = device.ResourceFactory.CreateCommandList();

        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = x < width / 2 ? (byte)255 : (byte)0;
                pixels[offset + 1] = y < height / 2 ? (byte)255 : (byte)0;
                pixels[offset + 2] = x >= width / 2 && y >= height / 2 ? (byte)255 : (byte)0;
                pixels[offset + 3] = 255;
            }
        }

        device.UpdateTexture(source, pixels, 0, 0, 0, width, height, 1, 0, 0);
        commands.Begin();
        commands.SetFramebuffer(framebuffer);
        commands.SetPipeline(shader.Pipeline);
        commands.SetGraphicsResourceSet(0, shader.GetOrCreateSourceResourceSet(source, device.PointSampler));
        commands.Draw(3);
        commands.End();
        device.SubmitCommands(commands);

        var captured = FrameCapturer.Capture(device, target);
        for (var i = 0; i < pixels.Length; i++)
        {
            Assert.True(Math.Abs(pixels[i] - captured.PixelData[i]) <= 1,
                $"Pixel byte {i} at {width}x{height}: expected {pixels[i]}, actual {captured.PixelData[i]}.");
        }
    }
}
