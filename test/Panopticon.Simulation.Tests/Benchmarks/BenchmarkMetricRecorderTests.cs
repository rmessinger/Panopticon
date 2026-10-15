using System;
using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// B1-3 metric capture against the mocked game: one tick sample per step, per-system spans
/// from the logic tick, interval-sampled memory, and clean unsubscribe on dispose.
/// </summary>
public class BenchmarkMetricRecorderTests : MockedGameTest
{
    private static SimulationHost CreateHost()
    {
        var game = new TestGame(SageGame.CncGenerals);
        return new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
    }

    [Fact]
    public void Run_RecordsOneTickSamplePerStep()
    {
        var host = CreateHost();
        using var recorder = new BenchmarkMetricRecorder(host);

        host.Run(3);

        var frames = recorder.TickSamples.Select(s => s.Frame).ToArray();
        Assert.Equal(new uint[] { 1, 2, 3 }, frames);
        Assert.All(recorder.TickSamples, s =>
        {
            Assert.True(s.WallMs >= 0);
            Assert.True(s.AllocBytes >= 0);
            Assert.True(s.Gc0 >= 0);
            Assert.True(s.Gc1 >= 0);
            Assert.True(s.Gc2 >= 0);
        });
    }

    [Fact]
    public void Run_RecordsLogicTickSystemSpans()
    {
        var host = CreateHost();
        using var recorder = new BenchmarkMetricRecorder(host);

        host.Run(2);

        Assert.NotEmpty(recorder.SystemSamples);
        Assert.All(recorder.SystemSamples, s =>
        {
            Assert.True(s.SpanMs >= 0);
            Assert.Contains(s.Frame, new uint[] { 1, 2 });
        });

        // GameLogic is always present on the mocked game; network is optional (null in the
        // mock), so the span set is gameLogic/ai/scene3d/partition at minimum. WI-M4-02:
        // "ai" is the nested decision-time span LogicStepper emits after gameLogic.
        var systems = recorder.SystemSamples.Select(s => s.System).ToHashSet();
        Assert.Contains("gameLogic", systems);
        Assert.Subset(
            new System.Collections.Generic.HashSet<string> { "gameLogic", "ai", "network", "scene3d", "partition" },
            systems);
    }

    [Fact]
    public void MemorySampleInterval_ControlsHeapAndWorkingSetCapture()
    {
        var hostEvery = CreateHost();
        using var recorderEvery = new BenchmarkMetricRecorder(hostEvery, memorySampleInterval: 1);

        hostEvery.Run(2);

        Assert.All(recorderEvery.TickSamples, s =>
        {
            Assert.NotNull(s.HeapBytes);
            Assert.NotNull(s.WorkingSetBytes);
        });

        var hostDefault = CreateHost();
        using var recorderDefault = new BenchmarkMetricRecorder(hostDefault);

        hostDefault.Run(2);

        Assert.All(recorderDefault.TickSamples, s =>
        {
            Assert.Null(s.HeapBytes);
            Assert.Null(s.WorkingSetBytes);
        });
    }

    [Fact]
    public void Dispose_StopsSampling()
    {
        var host = CreateHost();
        var recorder = new BenchmarkMetricRecorder(host);
        recorder.Dispose();

        host.Run(2);

        Assert.Empty(recorder.TickSamples);
        Assert.Empty(recorder.SystemSamples);
    }

    [Fact]
    public void Constructor_RejectsZeroInterval()
    {
        var host = CreateHost();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BenchmarkMetricRecorder(host, memorySampleInterval: 0));
    }
}
