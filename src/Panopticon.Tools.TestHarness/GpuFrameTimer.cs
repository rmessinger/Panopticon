using System;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Measures the GPU-side duration of a frame's render submissions with backend timestamp
/// queries. <see cref="BeginFrame"/> is called just before the frame's draw work is submitted
/// and <see cref="EndFrame"/> right after; completed frames surface through
/// <see cref="TryDequeue"/> once the GPU retires them, so harvesting never blocks the CPU.
/// Vulkan and Direct3D 11 are supported; other backends get no timer (null from
/// <see cref="TryCreate"/>).
/// </summary>
internal abstract class GpuFrameTimer : IDisposable
{
    public static GpuFrameTimer? TryCreate(GraphicsDevice device)
    {
        if (device.GetVulkanInfo(out var vulkan))
        {
            return VulkanGpuFrameTimer.TryCreate(vulkan);
        }

        if (device.GetD3D11Info(out var d3d11))
        {
            return D3D11GpuFrameTimer.TryCreate(d3d11);
        }

        return null;
    }

    public abstract void BeginFrame();

    public abstract void EndFrame();

    /// <summary>Returns one completed frame's GPU duration, oldest first; false when none are ready.</summary>
    public abstract bool TryDequeue(out double gpuMilliseconds);

    public abstract void Dispose();
}
