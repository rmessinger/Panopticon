using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests.Benchmarks;

/// <summary>
/// WI-A5-26: the A5 throughput benchmark emits all five authoring/replay operations as machine
/// readable B1 metrics. Runs the smoke plan only; full-scale fixture sizes are asserted as
/// constants, mirroring SimulationBenchmarkScenarioTests.
/// </summary>
public sealed class A5BenchmarkTests
{
    [Fact]
    public void SmokeRun_EmitsAllFiveOperationsAsB1Metrics()
    {
        var record = A5ThroughputBenchmark.Run(A5ThroughputPlan.SmokeScale);

        Assert.Equal("a5.throughput", record.ScenarioId);
        Assert.Equal(A5ThroughputPlan.SmokeScale.Iterations, record.TickSamples.Count);

        var metrics = record.Statistics.Select(s => s.Metric).ToList();
        Assert.Contains("tick.wallMs", metrics);
        Assert.Contains("tick.allocBytes", metrics);
        Assert.Contains("system.a5.expand.spanMs", metrics);
        Assert.Contains("system.a5.serialize.spanMs", metrics);
        Assert.Contains("system.a5.bundleOpen.spanMs", metrics);
        Assert.Contains("system.a5.randomSeek.spanMs", metrics);
        Assert.Contains("system.a5.visibleProjection.spanMs", metrics);

        Assert.NotNull(record.Environment);
        Assert.False(string.IsNullOrEmpty(record.Environment.Cpu));

        var json = BenchmarkReportWriter.ToJson(record);
        var reloaded = BenchmarkReportWriter.FromJson(json);
        Assert.Equal("a5.throughput", reloaded.ScenarioId);
        Assert.Contains(reloaded.Statistics, s => s.Metric == "system.a5.visibleProjection.spanMs");
        Assert.Contains(reloaded.Statistics, s => s.Metric == "system.a5.randomSeek.spanMs");
    }

    [Fact]
    public void FullScalePlan_MeetsDocumentedSizes()
    {
        Assert.Equal(10_000, A5ThroughputPlan.FullScale.EntityCount);
        Assert.Equal(10_000, A5ThroughputPlan.FullScale.CommandCount);
        Assert.Equal(250, A5ThroughputPlan.FullScale.RecordingFrames);
    }
}
