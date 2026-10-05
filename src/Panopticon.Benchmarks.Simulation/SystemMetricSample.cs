using System;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// One measured subsystem span inside a tick: the logic tick's gameLogic, network, scene3d,
/// or partition step, in milliseconds.
/// </summary>
public sealed class SystemMetricSample
{
    public SystemMetricSample(uint frame, string system, double spanMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        Frame = frame;
        System = system;
        SpanMs = spanMs;
    }

    public uint Frame { get; }

    public string System { get; }

    public double SpanMs { get; }
}
