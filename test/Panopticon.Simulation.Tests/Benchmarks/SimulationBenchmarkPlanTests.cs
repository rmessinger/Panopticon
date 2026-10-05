using System;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// B1-2 plan construction: argument validation and retained configuration, all without a
/// game installation.
/// </summary>
public class SimulationBenchmarkPlanTests
{
    [Fact]
    public void Constructor_RetainsValues()
    {
        var plan = new SimulationBenchmarkPlan("b1.empty-host", 7, 10, 90)
        {
            MapPath = "maps/a.map",
            JsonPath = "out.json",
            CsvPath = "out.csv",
        };

        Assert.Equal("b1.empty-host", plan.ScenarioId);
        Assert.Equal(7u, plan.Seed);
        Assert.Equal(10, plan.WarmupFrames);
        Assert.Equal(90, plan.MeasuredFrames);
        Assert.Equal("maps/a.map", plan.MapPath);
        Assert.Equal("out.json", plan.JsonPath);
        Assert.Equal("out.csv", plan.CsvPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_RejectsEmptyScenarioId(string scenarioId)
    {
        Assert.Throws<ArgumentException>(() => new SimulationBenchmarkPlan(scenarioId, 1, 0, 1));
    }

    [Fact]
    public void Constructor_RejectsNegativeWarmupFrames()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationBenchmarkPlan("b1.empty-host", 1, -1, 1));
    }

    [Fact]
    public void Constructor_RejectsZeroMeasuredFrames()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationBenchmarkPlan("b1.empty-host", 1, 0, 0));
    }
}
