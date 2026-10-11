using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Panopticon.Graphics;
using Panopticon.Utilities;
using Panopticon.Utilities.Extensions;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics;

// WI-X2-01: static upload helpers must not grow the device's deferred disposal queue. The queue
// only drains on WaitForIdle, so before the fix every upload left its command list and staging
// resource alive until some unrelated idle wait (or device teardown).

[Collection("GraphicsTests")]
public class ResourceUploadLifetimeTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);

    private static readonly FieldInfo DeferredDisposalsField = typeof(GraphicsDevice)
        .GetField("_disposables", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private int DeferredDisposalCount => ((ICollection)DeferredDisposalsField.GetValue(_device)!).Count;

    [GraphicsFact]
    public void RepeatedBufferUploads_KeepDeferredDisposalQueueDrained()
    {
        DeviceBuffer? previous = null;
        for (var i = 0; i < 64; i++)
        {
            previous?.Dispose();
            previous = _device.CreateStaticBuffer(
                new uint[] { (uint)i, (uint)i + 1, (uint)i + 2, (uint)i + 3 },
                BufferUsage.IndexBuffer);
            Assert.Equal(0, DeferredDisposalCount);
        }

        var readBack = ReadBack(previous!, 16);
        Assert.Equal(new uint[] { 63, 64, 65, 66 }, readBack);
        previous!.Dispose();
    }

    [GraphicsFact]
    public void RepeatedTextureUploads_KeepDeferredDisposalQueueDrained()
    {
        Texture? previous = null;
        for (var i = 0; i < 32; i++)
        {
            previous?.Dispose();
            var pixels = new byte[16 * 4];
            pixels.AsSpan().Fill((byte)i);
            previous = _device.CreateStaticTexture2D(
                4, 4, 1,
                new TextureMipMapData(pixels, 16, 64, 4, 4),
                PixelFormat.R8_G8_B8_A8_UNorm);
            Assert.Equal(0, DeferredDisposalCount);
        }

        Assert.Equal(31, ReadBackPixel(previous!, 0, 0).R);
        Assert.Equal(31, ReadBackPixel(previous!, 3, 3).R);
        previous!.Dispose();
    }

    [GraphicsFact]
    public void UniformBufferRoundingStillUploadsExactData()
    {
        var buffer = _device.CreateStaticBuffer(
            new TestConstants { Value0 = 1f, Value1 = 2f, Value2 = 3f },
            BufferUsage.UniformBuffer);
        var expected = new TestConstants { Value0 = 1f, Value1 = 2f, Value2 = 3f };

        var readBack = ReadBackStruct<TestConstants>(buffer, (uint)Marshal.SizeOf<TestConstants>());

        Assert.Equal(expected.Value0, readBack[0].Value0);
        Assert.Equal(expected.Value1, readBack[0].Value1);
        Assert.Equal(expected.Value2, readBack[0].Value2);
        Assert.Equal(0, DeferredDisposalCount);
        buffer.Dispose();
    }

    [GraphicsFact]
    public void FailedTextureUpload_ThrowsAndLeavesNoDeferredTemporaries()
    {
        // 15 bytes cannot fill a 4x4 RGBA mip, so recording fails after staging, destination,
        // and command list exist.
        var truncated = new TextureMipMapData(new byte[15], 16, 64, 4, 4);
        Exception? thrown = null;
        try
        {
            _device.CreateStaticTexture2D(4, 4, 1, truncated, PixelFormat.R8_G8_B8_A8_UNorm);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        Assert.NotNull(thrown);
        Assert.Equal(0, DeferredDisposalCount);
    }

    private uint[] ReadBack(DeviceBuffer buffer, uint sizeInBytes)
    {
        return ReadBackStruct<uint>(buffer, sizeInBytes);
    }

    private T[] ReadBackStruct<T>(DeviceBuffer buffer, uint sizeInBytes)
        where T : unmanaged
    {
        var readback = _device.ResourceFactory.CreateBuffer(
            new BufferDescription(sizeInBytes, BufferUsage.Staging));
        var commands = _device.ResourceFactory.CreateCommandList();
        commands.Begin();
        commands.CopyBuffer(buffer, 0, readback, 0, sizeInBytes);
        commands.End();
        _device.SubmitCommands(commands);
        _device.WaitForIdle();

        var view = _device.Map<T>(readback, MapMode.Read);
        var result = new T[view.Count];
        for (var i = 0; i < view.Count; i++)
        {
            result[i] = view[i];
        }

        _device.Unmap(readback);
        commands.Dispose();
        readback.Dispose();
        return result;
    }

    private RgbaByte ReadBackPixel(Texture texture, uint x, uint y)
    {
        var readback = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            texture.Width, texture.Height, 1, 1, texture.Format, TextureUsage.Staging));
        var commands = _device.ResourceFactory.CreateCommandList();
        commands.Begin();
        commands.CopyTexture(texture, 0, 0, 0, 0, 0, readback, 0, 0, 0, 0, 0, texture.Width, texture.Height, 1, 1);
        commands.End();
        _device.SubmitCommands(commands);
        _device.WaitForIdle();

        var mapped = _device.Map(readback, MapMode.Read, 0);
        var row = (int)(y * mapped.RowPitch + x * 4);
        var pixels = new byte[4];
        Marshal.Copy(mapped.Data + row, pixels, 0, 4);
        _device.Unmap(readback, 0);
        commands.Dispose();
        readback.Dispose();
        return new RgbaByte(pixels[0], pixels[1], pixels[2], pixels[3]);
    }

    private readonly record struct TestConstants(float Value0, float Value1, float Value2);

    public void Dispose()
    {
        _device.Dispose();
    }
}
