using System;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// B1-2 scenario lookup: known ids resolve without touching an installation, unknown ids
/// fail with the id in the message.
/// </summary>
public class SimulationBenchmarkScenarioTests
{
    [Fact]
    public void Create_EmptyHost_ReturnsScenarioWithoutInstall()
    {
        var scenario = SimulationBenchmarkScenario.Create(EmptyHostBenchmarkScenario.ScenarioId);

        Assert.IsType<EmptyHostBenchmarkScenario>(scenario);
        Assert.Equal(EmptyHostBenchmarkScenario.ScenarioId, scenario.Id);
    }

    [Fact]
    public void Create_UnknownScenario_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => SimulationBenchmarkScenario.Create("b1.does-not-exist"));

        Assert.Contains("b1.does-not-exist", ex.Message);
    }
}
