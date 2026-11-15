using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Measures each stage invocation as one B1 tick bracket and one named system span, and keeps the
/// per-repeat values for <see cref="B4aStageSummary"/>.
/// </summary>
internal sealed class B4aStageMeter : IDisposable
{
    private readonly BenchmarkMetricRecorder _recorder = new();
    private readonly List<string> _stages = [];
    private readonly Dictionary<string, List<B4aStageRepeat>> _repeats = new(StringComparer.Ordinal);
    private uint _frame;

    public Action<string>? StageStarting { get; init; }

    public IReadOnlyList<TickMetricSample> TickSamples => _recorder.TickSamples;

    public IReadOnlyList<SystemMetricSample> SystemSamples => _recorder.SystemSamples;

    public T Measure<T>(string stage, Func<T> body)
    {
        StageStarting?.Invoke(stage);

        if (!_repeats.TryGetValue(stage, out var repeats))
        {
            repeats = [];
            _repeats.Add(stage, repeats);
            _stages.Add(stage);
        }

        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);

        _recorder.BeginTick(_frame);
        var stopwatch = Stopwatch.StartNew();
        var result = body();
        stopwatch.Stop();
        _recorder.EndTick(_frame);

        var tick = _recorder.TickSamples[^1];
        _recorder.RecordSystemSpan(stage, stopwatch.Elapsed);
        repeats.Add(new B4aStageRepeat(
            tick.WallMs,
            tick.AllocBytes,
            tick.Gc0 - gc0,
            tick.Gc1 - gc1,
            tick.Gc2 - gc2));

        _frame++;
        return result;
    }

    public void Measure(string stage, Action body)
    {
        Measure<bool>(stage, () =>
        {
            body();
            return true;
        });
    }

    public IReadOnlyList<B4aStageSummary> Summaries()
    {
        return [.. _stages.Select(stage => B4aStageSummary.FromRepeats(stage, _repeats[stage]))];
    }

    public void Dispose()
    {
        _recorder.Dispose();
    }
}
