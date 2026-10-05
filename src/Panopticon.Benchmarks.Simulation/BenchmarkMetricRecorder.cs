using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Bounded metric capture for one measured run: subscribes to the host's tick hooks and
/// per-system spans, accumulating tick samples (wall time, allocation delta, cumulative GC
/// counts, interval-sampled memory) and subsystem spans. Attach after warmup so warmup frames
/// are discarded by construction.
/// </summary>
public sealed class BenchmarkMetricRecorder : IDisposable
{
    private readonly SimulationHost _host;
    private readonly uint _memorySampleInterval;
    private readonly List<TickMetricSample> _tickSamples = [];
    private readonly List<SystemMetricSample> _systemSamples = [];

    private long _tickStartTimestamp;
    private long _tickStartAllocated;
    private int _tickStartGc0;
    private int _tickStartGc1;
    private int _tickStartGc2;

    public BenchmarkMetricRecorder(SimulationHost host, uint memorySampleInterval = 30)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfZero(memorySampleInterval);

        _host = host;
        _memorySampleInterval = memorySampleInterval;

        _host.TickStarting += OnTickStarting;
        _host.TickComplete += OnTickComplete;
        _host.LogicSystemSpan += OnLogicSystemSpan;
    }

    public IReadOnlyList<TickMetricSample> TickSamples => _tickSamples;

    public IReadOnlyList<SystemMetricSample> SystemSamples => _systemSamples;

    private void OnTickStarting(object? sender, SimulationHostTickEventArgs e)
    {
        _tickStartTimestamp = Stopwatch.GetTimestamp();
        _tickStartAllocated = GC.GetAllocatedBytesForCurrentThread();
        _tickStartGc0 = GC.CollectionCount(0);
        _tickStartGc1 = GC.CollectionCount(1);
        _tickStartGc2 = GC.CollectionCount(2);
    }

    private void OnTickComplete(object? sender, SimulationHostTickEventArgs e)
    {
        var sample = new TickMetricSample(e.Frame)
        {
            WallMs = Stopwatch.GetElapsedTime(_tickStartTimestamp).TotalMilliseconds,
            AllocBytes = GC.GetAllocatedBytesForCurrentThread() - _tickStartAllocated,
            Gc0 = GC.CollectionCount(0),
            Gc1 = GC.CollectionCount(1),
            Gc2 = GC.CollectionCount(2),
        };

        // Heap and working-set reads are comparatively expensive; sample them at the
        // configured interval rather than every tick.
        if (e.Frame % _memorySampleInterval == 0)
        {
            sample.HeapBytes = GC.GetGCMemoryInfo().HeapSizeBytes;
            sample.WorkingSetBytes = Environment.WorkingSet;
        }

        _tickSamples.Add(sample);
    }

    private void OnLogicSystemSpan(string system, TimeSpan span)
    {
        // Spans fire inside AdvanceLogic, before the host increments its frame counter; the
        // in-progress step is the one TickComplete will report as _host.Frame + 1.
        _systemSamples.Add(new SystemMetricSample(_host.Frame + 1, system, span.TotalMilliseconds));
    }

    public void Dispose()
    {
        _host.TickStarting -= OnTickStarting;
        _host.TickComplete -= OnTickComplete;
        _host.LogicSystemSpan -= OnLogicSystemSpan;
    }
}
