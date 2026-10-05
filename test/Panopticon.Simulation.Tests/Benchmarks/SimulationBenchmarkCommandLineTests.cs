using System;
using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// B1-2 CLI parsing: defaults, full argument sets, and error paths, all install-free.
/// </summary>
public class SimulationBenchmarkCommandLineTests
{
    [Fact]
    public void Parse_NoArguments_AppliesDefaults()
    {
        var plan = SimulationBenchmarkCommandLine.Parse([]);

        Assert.NotNull(plan);
        Assert.Equal(SimulationBenchmarkCommandLine.DefaultScenarioId, plan.ScenarioId);
        Assert.Equal(SimulationBenchmarkCommandLine.DefaultSeed, plan.Seed);
        Assert.Equal(SimulationBenchmarkCommandLine.DefaultWarmupFrames, plan.WarmupFrames);
        Assert.Equal(SimulationBenchmarkCommandLine.DefaultMeasuredFrames, plan.MeasuredFrames);
        Assert.Null(plan.MapPath);
        Assert.Null(plan.JsonPath);
        Assert.Null(plan.CsvPath);
    }

    [Fact]
    public void Parse_AllOptions_ReadsEveryValue()
    {
        var plan = SimulationBenchmarkCommandLine.Parse(
        [
            "--scenario", "b1.empty-host",
            "--seed", "42",
            "--warmup-frames", "5",
            "--measured-frames", "60",
            "--map", "maps/x.map",
            "--json", "r.json",
            "--csv", "r.csv",
        ]);

        Assert.NotNull(plan);
        Assert.Equal("b1.empty-host", plan.ScenarioId);
        Assert.Equal(42u, plan.Seed);
        Assert.Equal(5, plan.WarmupFrames);
        Assert.Equal(60, plan.MeasuredFrames);
        Assert.Equal("maps/x.map", plan.MapPath);
        Assert.Equal("r.json", plan.JsonPath);
        Assert.Equal("r.csv", plan.CsvPath);
    }

    [Fact]
    public void Parse_Help_ReturnsNull()
    {
        Assert.Null(SimulationBenchmarkCommandLine.Parse(["--help"]));
    }

    [Fact]
    public void Parse_UnknownOption_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => SimulationBenchmarkCommandLine.Parse(["--nope", "1"]));

        Assert.Contains("--nope", ex.Message);
    }

    [Fact]
    public void Parse_MissingValue_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => SimulationBenchmarkCommandLine.Parse(["--seed"]));

        Assert.Throws<ArgumentException>(
            () => SimulationBenchmarkCommandLine.Parse(["--seed", "--json"]));
    }

    [Fact]
    public void Parse_NonNumericValue_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => SimulationBenchmarkCommandLine.Parse(["--seed", "abc"]));

        Assert.Throws<ArgumentException>(
            () => SimulationBenchmarkCommandLine.Parse(["--measured-frames", "x"]));
    }

    [Fact]
    public void Parse_InvalidFrames_SurfacesPlanError()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => SimulationBenchmarkCommandLine.Parse(["--measured-frames", "0"]));

        Assert.StartsWith("Invalid plan:", ex.Message);
    }

    [Fact]
    public void Usage_ListsEveryOption()
    {
        var usage = SimulationBenchmarkCommandLine.Usage;

        foreach (var option in new[]
                 {
                     "--scenario", "--seed", "--warmup-frames", "--measured-frames",
                     "--map", "--json", "--csv", "--help",
                 })
        {
            Assert.Contains(option, usage);
        }
    }
}
