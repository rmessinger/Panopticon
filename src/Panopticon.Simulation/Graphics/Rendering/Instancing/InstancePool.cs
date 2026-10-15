using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Panopticon.Graphics.Shaders;
using Veldrid;

namespace Panopticon.Graphics.Rendering.Instancing;

/// <summary>
/// Frame-scoped instance stream (B5-2): every instanced batch of the frame appends its
/// instances here and records one draw over a contiguous range. One grow-only structured
/// buffer backs all instanced shader families; the batch start travels through a shared
/// 16-byte constants buffer, updated per batch (the same update-then-bind pattern the
/// per-item path uses for RenderItemConstantsVS).
/// </summary>
public sealed class InstancePool : DisposableBase
{
    private const int InitialCapacity = 4096;

    private static readonly int Stride = Marshal.SizeOf<InstanceData>();

    private readonly GraphicsDevice _graphicsDevice;

    private readonly Dictionary<(ResourceLayout Layout, DeviceBuffer MeshConstants), ResourceSet> _itemResourceSets = new();

    private InstanceData[] _staging = new InstanceData[InitialCapacity];

    private DeviceBuffer _instanceBuffer;

    /// <summary>Instances appended since the last Reset (CPU view).</summary>
    public int Count { get; private set; }

    public ConstantBuffer<InstancedItemConstants> ItemConstants { get; }

    public InstancePool(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;

        _instanceBuffer = AddDisposable(CreateInstanceBuffer(InitialCapacity));
        ItemConstants = AddDisposable(new ConstantBuffer<InstancedItemConstants>(graphicsDevice, "InstancedItemConstants"));
    }

    /// <summary>Appends one instance and returns its index in the frame stream.</summary>
    public int Append(in InstanceData instance)
    {
        if (Count == _staging.Length)
        {
            Array.Resize(ref _staging, _staging.Length * 2);
        }

        _staging[Count] = instance;
        return Count++;
    }

    /// <summary>Uploads the appended range of instances for the recorded frame.</summary>
    public void UploadRange(CommandList commandList, int start, int count)
    {
        if (count == 0)
        {
            return;
        }

        EnsureBufferCapacity();
        commandList.UpdateBuffer(
            _instanceBuffer,
            (uint)(start * Stride),
            _staging.AsSpan(start, count));
    }

    /// <summary>
    /// Sets the batch start for the next instanced draw and binds the family's item set
    /// (set 3: mesh constants, batch start, instance stream).
    /// </summary>
    public void BindItemSet(
        CommandList commandList,
        ResourceLayout layout,
        DeviceBuffer meshConstantsBuffer,
        uint instanceStart)
    {
        ItemConstants.Value = instanceStart;
        ItemConstants.Update(commandList);

        commandList.SetGraphicsResourceSet(3, GetOrCreateItemResourceSet(layout, meshConstantsBuffer));
    }

    private ResourceSet GetOrCreateItemResourceSet(ResourceLayout layout, DeviceBuffer meshConstantsBuffer)
    {
        var key = (layout, meshConstantsBuffer);
        if (!_itemResourceSets.TryGetValue(key, out var set))
        {
            set = AddDisposable(_graphicsDevice.ResourceFactory.CreateResourceSet(
                new ResourceSetDescription(layout, meshConstantsBuffer, ItemConstants.Buffer, _instanceBuffer)));
            _itemResourceSets.Add(key, set);
        }

        return set;
    }

    private void EnsureBufferCapacity()
    {
        if ((uint)(Count * Stride) <= _instanceBuffer.SizeInBytes)
        {
            return;
        }

        // Grow-and-replace: sets created against the replaced buffer are dropped from the
        // cache. The old buffer stays alive until this pool is disposed (commands recorded
        // earlier in the frame may still reference it), which is acceptable because growth
        // happens at most a handful of times over a session.
        _instanceBuffer = AddDisposable(CreateInstanceBuffer(_staging.Length));
        _itemResourceSets.Clear();
    }

    private DeviceBuffer CreateInstanceBuffer(int capacity)
    {
        return _graphicsDevice.ResourceFactory.CreateBuffer(
            new BufferDescription(
                (uint)(capacity * Stride),
                BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic,
                (uint)Stride,
                true));
    }

    /// <summary>Starts a new frame; instances append from index zero again.</summary>
    public void Reset()
    {
        Count = 0;
    }
}
