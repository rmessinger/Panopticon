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
/// <remarks>
/// Two driving modes: constructed with a <see cref="SimulationHost"/> the recorder subscribes to
/// the host's events; constructed without one, a caller driving frames outside
/// <see cref="SimulationHost.Run"/> (the TestHarness step loop) bracket each step with
/// <see cref="BeginTick"/>/<see cref="EndTick"/> and forward
/// <see cref="LogicStepper.SystemSpan"/> through <see cref="RecordSystemSpan"/>.
/// </remarks>
public sealed class BenchmarkMetricRecorder : IDisposable
{
    private readonly SimulationHost? _host;
    private readonly uint _memorySampleInterval;
    private readonly List<TickMetricSample> _tickSamples = [];
    private readonly List<SystemMetricSample> _systemSamples = [];

    private long _tickStartTimestamp;
    private long _tickStartAllocated;
    private int _tickStartGc0;
    private int _tickStartGc1;
    private int _tickStartGc2;
    private uint _inProgressFrame;

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

    /// <summary>
    /// Manual mode (WI-M1-02): the caller drives the step loop and brackets each frame itself.
    /// </summary>
    public BenchmarkMetricRecorder(uint memorySampleInterval = 30)
    {
        ArgumentOutOfRangeException.ThrowIfZero(memorySampleInterval);

        _memorySampleInterval = memorySampleInterval;
    }

    public IReadOnlyList<TickMetricSample> TickSamples => _tickSamples;

    public IReadOnlyList<SystemMetricSample> SystemSamples => _systemSamples;

    private void OnTickStarting(object? sender, SimulationHostTickEventArgs e)
    {
        BeginTick(e.Frame);
    }

    private void OnTickComplete(object? sender, SimulationHostTickEventArgs e)
    {
        EndTick(e.Frame);
    }

    /// <summary>
    /// Opens the measured bracket around one frame step: <paramref name="frame"/> is the frame
    /// the step is about to produce (the value <see cref="EndTick"/> will report).
    /// </summary>
    public void BeginTick(uint frame)
    {
        _inProgressFrame = frame;
        _tickStartTimestamp = Stopwatch.GetTimestamp();
        _tickStartAllocated = GC.GetAllocatedBytesForCurrentThread();
        _tickStartGc0 = GC.CollectionCount(0);
        _tickStartGc1 = GC.CollectionCount(1);
        _tickStartGc2 = GC.CollectionCount(2);
    }

    /// <summary>
    /// Closes the measured bracket around one frame step and records the sample.
    /// </summary>
    public void EndTick(uint frame)
    {
        var sample = new TickMetricSample(frame)
        {
            WallMs = Stopwatch.GetElapsedTime(_tickStartTimestamp).TotalMilliseconds,
            AllocBytes = GC.GetAllocatedBytesForCurrentThread() - _tickStartAllocated,
            Gc0 = GC.CollectionCount(0),
            Gc1 = GC.CollectionCount(1),
            Gc2 = GC.CollectionCount(2),
        };

        // Heap and working-set reads are comparatively expensive; sample them at the
        // configured interval rather than every tick.
        if (frame % _memorySampleInterval == 0)
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
        _systemSamples.Add(new SystemMetricSample(_host!.Frame + 1, system, span.TotalMilliseconds));
    }

    /// <summary>
    /// Manual-mode span capture (WI-M1-02): attributes the span to the frame the caller opened
    /// with <see cref="BeginTick"/>. Host-driven recorders get spans through the host event.
    /// </summary>
    public void RecordSystemSpan(string system, TimeSpan span)
    {
        _systemSamples.Add(new SystemMetricSample(_inProgressFrame, system, span.TotalMilliseconds));
    }

    public void Dispose()
    {
        if (_host is null)
        {
            return;
        }

        _host.TickStarting -= OnTickStarting;
        _host.TickComplete -= OnTickComplete;
        _host.LogicSystemSpan -= OnLogicSystemSpan;
    }
}
