using System;
using System.Collections.Generic;
using Veldrid;
using Vortice.Direct3D11;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Direct3D 11 timestamp-query timer behind <see cref="GpuFrameTimer"/>. Wraps the raw
/// ID3D11Device from <see cref="BackendInfoD3D11"/>; the device and immediate context stay
/// owned by Veldrid (the wrappers are never disposed here, only the queries created below).
/// Each frame issues Begin/End timestamp pairs plus a disjoint query on the immediate context,
/// bracketing the frame's draw submissions in driver queue order.
/// </summary>
internal sealed class D3D11GpuFrameTimer : GpuFrameTimer
{
    private const int FrameSlots = 8;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Query[] _beginQueries;
    private readonly ID3D11Query[] _endQueries;
    private readonly ID3D11Query[] _disjointQueries;
    private readonly bool[] _slotBusy;
    private readonly Queue<int> _pending = new();
    private readonly Queue<double> _completed = new();
    private int _nextSlot;
    private int _frameSlot;
    private bool _frameActive;

    public static D3D11GpuFrameTimer? TryCreate(BackendInfoD3D11 info)
    {
        return new D3D11GpuFrameTimer(info);
    }

    private D3D11GpuFrameTimer(BackendInfoD3D11 info)
    {
        _device = new ID3D11Device(info.Device);
        _context = _device.ImmediateContext;
        _beginQueries = new ID3D11Query[FrameSlots];
        _endQueries = new ID3D11Query[FrameSlots];
        _disjointQueries = new ID3D11Query[FrameSlots];
        _slotBusy = new bool[FrameSlots];
        for (var i = 0; i < FrameSlots; i++)
        {
            _beginQueries[i] = _device.CreateQuery(new QueryDescription(QueryType.Timestamp));
            _endQueries[i] = _device.CreateQuery(new QueryDescription(QueryType.Timestamp));
            _disjointQueries[i] = _device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
        }
    }

    public override void BeginFrame()
    {
        _frameActive = false;
        var slot = _nextSlot;
        _nextSlot = (_nextSlot + 1) % FrameSlots;
        if (_slotBusy[slot] && !_context.GetData(_disjointQueries[slot], out QueryDataTimestampDisjoint _))
        {
            // The slot's previous pass has not been consumed by the GPU yet.
            return;
        }

        _slotBusy[slot] = true;
        _context.Begin(_disjointQueries[slot]);
        _context.End(_beginQueries[slot]);
        _pending.Enqueue(slot);
        _frameSlot = slot;
        _frameActive = true;
    }

    public override void EndFrame()
    {
        if (!_frameActive)
        {
            return;
        }

        _context.End(_endQueries[_frameSlot]);
        _context.End(_disjointQueries[_frameSlot]);
    }

    public override bool TryDequeue(out double gpuMilliseconds)
    {
        while (_completed.Count == 0 && _pending.Count > 0)
        {
            var slot = _pending.Peek();
            if (!_context.GetData(_disjointQueries[slot], out QueryDataTimestampDisjoint disjoint))
            {
                break;
            }

            _pending.Dequeue();
            _slotBusy[slot] = false;
            if (disjoint.Disjoint || !_context.GetData(_beginQueries[slot], out ulong begin)
                || !_context.GetData(_endQueries[slot], out ulong end))
            {
                continue;
            }

            _completed.Enqueue((end - begin) * 1000.0 / disjoint.Frequency);
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
        for (var i = 0; i < FrameSlots; i++)
        {
            _beginQueries[i]?.Dispose();
            _endQueries[i]?.Dispose();
            _disjointQueries[i]?.Dispose();
        }
    }
}
