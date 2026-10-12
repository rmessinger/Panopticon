using System;
using System.Collections.Generic;
using Veldrid;
using Vulkan;
using static Vulkan.VulkanNative;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Vulkan timestamp-query timer behind <see cref="GpuFrameTimer"/>. Veldrid 4.9.0 exposes no
/// query-pool API, so this drives the raw Vk handles from <see cref="BackendInfoVulkan"/>.
/// Each frame submits two tiny pre-recorded command buffers bracketing the frame's draw
/// submissions; a per-slot fence makes the query results readable once the GPU retires the
/// frame, a few frames later, without stalling the CPU. Submissions and timestamps all land on
/// Veldrid's graphics queue from the window thread, so queue order preserves the bracket.
/// </summary>
internal sealed unsafe class VulkanGpuFrameTimer : GpuFrameTimer
{
    private const uint FrameSlots = 64;

    private readonly VkDevice _device;
    private readonly VkQueue _queue;
    private readonly VkQueryPool _queryPool;
    private readonly VkCommandPool _commandPool;
    private readonly VkFence[] _fences;
    private readonly VkCommandBuffer[] _beginBuffers;
    private readonly VkCommandBuffer[] _endBuffers;
    private readonly bool[] _slotBusy;
    private readonly Queue<int> _pending = new();
    private readonly Queue<double> _completed = new();
    private readonly float _timestampPeriodNanoseconds;
    private readonly ulong _timestampMask;
    private int _nextSlot;
    private int _frameSlot;
    private bool _frameActive;
    private bool _disposed;

    public static VulkanGpuFrameTimer? TryCreate(BackendInfoVulkan info)
    {
        var physicalDevice = new VkPhysicalDevice(info.PhysicalDevice);
        uint familyCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, null);
        var families = new VkQueueFamilyProperties[familyCount];
        fixed (VkQueueFamilyProperties* pFamilies = families)
        {
            vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, pFamilies);
        }

        if (info.GraphicsQueueFamilyIndex >= familyCount || families[info.GraphicsQueueFamilyIndex].timestampValidBits == 0)
        {
            return null;
        }

        VkPhysicalDeviceProperties properties;
        vkGetPhysicalDeviceProperties(physicalDevice, &properties);
        return new VulkanGpuFrameTimer(
            info,
            families[info.GraphicsQueueFamilyIndex].timestampValidBits,
            properties.limits.timestampPeriod);
    }

    private VulkanGpuFrameTimer(BackendInfoVulkan info, uint timestampValidBits, float timestampPeriodNanoseconds)
    {
        _device = new VkDevice(info.Device);
        _queue = new VkQueue(info.GraphicsQueue);
        _timestampPeriodNanoseconds = timestampPeriodNanoseconds;
        _timestampMask = timestampValidBits >= 64 ? ulong.MaxValue : (1ul << (int)timestampValidBits) - 1;
        _fences = new VkFence[FrameSlots];
        _beginBuffers = new VkCommandBuffer[FrameSlots];
        _endBuffers = new VkCommandBuffer[FrameSlots];
        _slotBusy = new bool[FrameSlots];

        var queryPoolInfo = new VkQueryPoolCreateInfo
        {
            sType = VkStructureType.QueryPoolCreateInfo,
            queryType = VkQueryType.Timestamp,
            queryCount = FrameSlots * 2,
        };
        Check(vkCreateQueryPool(_device, &queryPoolInfo, null, out _queryPool), "vkCreateQueryPool");

        var commandPoolInfo = new VkCommandPoolCreateInfo
        {
            sType = VkStructureType.CommandPoolCreateInfo,
            queueFamilyIndex = info.GraphicsQueueFamilyIndex,
        };
        Check(vkCreateCommandPool(_device, &commandPoolInfo, null, out _commandPool), "vkCreateCommandPool");

        var allocateInfo = new VkCommandBufferAllocateInfo
        {
            sType = VkStructureType.CommandBufferAllocateInfo,
            commandPool = _commandPool,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = FrameSlots * 2,
        };
        var commandBuffers = new VkCommandBuffer[FrameSlots * 2];
        fixed (VkCommandBuffer* pCommandBuffers = commandBuffers)
        {
            Check(vkAllocateCommandBuffers(_device, &allocateInfo, pCommandBuffers), "vkAllocateCommandBuffers");
        }

        var fenceInfo = new VkFenceCreateInfo { sType = VkStructureType.FenceCreateInfo };
        for (var i = 0; i < FrameSlots; i++)
        {
            _beginBuffers[i] = commandBuffers[i * 2];
            _endBuffers[i] = commandBuffers[i * 2 + 1];
            fixed (VkFence* pFence = &_fences[i])
            {
                Check(vkCreateFence(_device, &fenceInfo, null, pFence), "vkCreateFence");
            }

            RecordBracket(_beginBuffers[i], (uint)i, true);
            RecordBracket(_endBuffers[i], (uint)i, false);
        }
    }

    public override void BeginFrame()
    {
        _frameActive = false;
        var slot = _nextSlot;
        _nextSlot = (_nextSlot + 1) % (int)FrameSlots;
        if (_slotBusy[slot] && vkGetFenceStatus(_device, _fences[slot]) != VkResult.Success)
        {
            // The GPU has not retired this slot from its previous pass FrameSlots frames ago;
            // dropping the frame's timing is cheaper than stalling the CPU.
            return;
        }

        _slotBusy[slot] = true;
        fixed (VkFence* pFence = &_fences[slot])
        {
            Check(vkResetFences(_device, 1, pFence), "vkResetFences");
        }

        Submit(_beginBuffers[slot], default);
        _pending.Enqueue(slot);
        _frameSlot = slot;
        _frameActive = true;
    }

    public override void EndFrame()
    {
        if (_frameActive)
        {
            Submit(_endBuffers[_frameSlot], _fences[_frameSlot]);
        }
    }

    public override bool TryDequeue(out double gpuMilliseconds)
    {
        var results = stackalloc ulong[2];
        while (_completed.Count == 0 && _pending.Count > 0)
        {
            var slot = _pending.Peek();
            if (vkGetFenceStatus(_device, _fences[slot]) != VkResult.Success)
            {
                break;
            }

            _pending.Dequeue();
            _slotBusy[slot] = false;
            if (vkGetQueryPoolResults(
                    _device, _queryPool, (uint)slot * 2, 2, (nuint)16, results, 8, VkQueryResultFlags._64)
                != VkResult.Success)
            {
                continue;
            }

            var ticks = (results[1] - results[0]) & _timestampMask;
            _completed.Enqueue(ticks * _timestampPeriodNanoseconds / 1_000_000.0);
        }

        if (_completed.Count == 0)
        {
            gpuMilliseconds = 0;
            return false;
        }

        gpuMilliseconds = _completed.Dequeue();
        return true;
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        vkQueueWaitIdle(_queue);
        for (var i = 0; i < FrameSlots; i++)
        {
            vkDestroyFence(_device, _fences[i], null);
        }

        vkDestroyQueryPool(_device, _queryPool, null);
        vkDestroyCommandPool(_device, _commandPool, null);
    }

    private void RecordBracket(VkCommandBuffer commandBuffer, uint slot, bool isBegin)
    {
        var beginInfo = new VkCommandBufferBeginInfo { sType = VkStructureType.CommandBufferBeginInfo };
        Check(vkBeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
        if (isBegin)
        {
            vkCmdResetQueryPool(commandBuffer, _queryPool, slot * 2, 2);
        }

        vkCmdWriteTimestamp(
            commandBuffer,
            isBegin ? VkPipelineStageFlags.TopOfPipe : VkPipelineStageFlags.BottomOfPipe,
            _queryPool,
            slot * 2 + (isBegin ? 0u : 1u));
        Check(vkEndCommandBuffer(commandBuffer), "vkEndCommandBuffer");
    }

    private void Submit(VkCommandBuffer commandBuffer, VkFence fence)
    {
        var submitInfo = new VkSubmitInfo { sType = VkStructureType.SubmitInfo, commandBufferCount = 1 };
        submitInfo.pCommandBuffers = &commandBuffer;
        Check(vkQueueSubmit(_queue, 1, &submitInfo, fence), "vkQueueSubmit");
    }

    private static void Check(VkResult result, string entryPoint)
    {
        if (result != VkResult.Success)
        {
            throw new InvalidOperationException($"{entryPoint} failed: {result}");
        }
    }
}
