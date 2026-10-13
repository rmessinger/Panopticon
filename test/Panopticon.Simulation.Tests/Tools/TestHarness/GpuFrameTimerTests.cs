using System;
using Panopticon.Graphics;
using Panopticon.Tools.TestHarness;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Tools.TestHarness;

[Collection("GraphicsTests")]
public class GpuFrameTimerTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);

    [GraphicsFact]
    public void TimestampBracketsReportNonNegativeFrameDurations()
    {
        // Only Vulkan runs headlessly on Linux; the D3D11 timer is exercised on Windows runs.
        if (_device.BackendType != GraphicsBackend.Vulkan)
        {
            return;
        }

        using var timer = GpuFrameTimer.TryCreate(_device);
        if (timer == null)
        {
            // The graphics queue reports no timestamp support (timestampValidBits == 0).
            return;
        }

        var harvested = 0;
        for (var frame = 0; frame < 16 && harvested == 0; frame++)
        {
            timer.BeginFrame();
            using var commands = _device.ResourceFactory.CreateCommandList();
            commands.Begin();
            commands.End();
            _device.SubmitCommands(commands);
            timer.EndFrame();
            while (timer.TryDequeue(out var gpuMilliseconds))
            {
                Assert.True(gpuMilliseconds >= 0);
                harvested++;
            }
        }

        Assert.True(harvested > 0);
    }

    [GraphicsFact]
    public void DisposeWithPendingFrameDoesNotThrow()
    {
        using var timer = GpuFrameTimer.TryCreate(_device);
        if (timer == null)
        {
            return;
        }

        timer.BeginFrame();
        timer.EndFrame();
        timer.Dispose();
    }

    public void Dispose()
    {
        _device.Dispose();
    }
}
