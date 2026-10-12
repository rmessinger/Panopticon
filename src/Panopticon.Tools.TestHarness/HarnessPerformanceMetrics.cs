using System;
using System.Diagnostics;

namespace Panopticon.Tools.TestHarness;

public sealed class HarnessPerformanceMetrics
{
    private double _elapsed;
    private double _updateMilliseconds;
    private double _renderMilliseconds;
    private int _frames;

    public double FramesPerSecond { get; private set; }
    public double FrameMilliseconds { get; private set; }
    public double UpdateMilliseconds { get; private set; }
    public double RenderMilliseconds { get; private set; }
    public long WorkingSetBytes { get; private set; }
    public long ManagedBytes { get; private set; }
    public int Gen0Collections { get; private set; }
    public int Gen1Collections { get; private set; }
    public int Gen2Collections { get; private set; }

    public void Record(double frameSeconds, double updateMilliseconds, double renderMilliseconds)
    {
        if (!double.IsFinite(frameSeconds) || frameSeconds <= 0)
        {
            return;
        }

        _elapsed += frameSeconds;
        _updateMilliseconds += updateMilliseconds;
        _renderMilliseconds += renderMilliseconds;
        _frames++;
        if (_elapsed < 1)
        {
            return;
        }

        FramesPerSecond = _frames / _elapsed;
        FrameMilliseconds = 1000 * _elapsed / _frames;
        UpdateMilliseconds = _updateMilliseconds / _frames;
        RenderMilliseconds = _renderMilliseconds / _frames;
        using var process = Process.GetCurrentProcess();
        WorkingSetBytes = process.WorkingSet64;
        ManagedBytes = GC.GetTotalMemory(false);
        Gen0Collections = GC.CollectionCount(0);
        Gen1Collections = GC.CollectionCount(1);
        Gen2Collections = GC.CollectionCount(2);
        _elapsed = 0;
        _updateMilliseconds = 0;
        _renderMilliseconds = 0;
        _frames = 0;
    }
}
