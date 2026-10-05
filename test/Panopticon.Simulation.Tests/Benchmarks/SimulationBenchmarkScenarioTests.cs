using System;
using System.Linq;
using Panopticon.Benchmarks.Simulation;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// B1-2/B1-6 scenario lookup: every known id resolves without touching an installation,
/// carries its constants, and unknown ids fail with the id in the message.
/// </summary>
public class SimulationBenchmarkScenarioTests
{
    [Theory]
    [InlineData(EmptyHostBenchmarkScenario.ScenarioId, typeof(EmptyHostBenchmarkScenario))]
    [InlineData(OneThousandMoveBenchmarkScenario.ScenarioId, typeof(OneThousandMoveBenchmarkScenario))]
    [InlineData(MixedCombatBenchmarkScenario.ScenarioId, typeof(MixedCombatBenchmarkScenario))]
    [InlineData(PathQueryBenchmarkScenario.ScenarioId, typeof(PathQueryBenchmarkScenario))]
    [InlineData(TenThousandScaleAnchorBenchmarkScenario.ScenarioId, typeof(TenThousandScaleAnchorBenchmarkScenario))]
    public void Create_KnownScenarios_ResolveWithoutInstall(string scenarioId, Type expectedType)
    {
        var scenario = SimulationBenchmarkScenario.Create(scenarioId);

        Assert.IsType(expectedType, scenario);
        Assert.Equal(scenarioId, scenario.Id);
    }

    [Fact]
    public void Scenarios_CarryReferenceWorkloadConstants()
    {
        Assert.Equal(1000, OneThousandMoveBenchmarkScenario.UnitCount);
        Assert.Equal(300, MixedCombatBenchmarkScenario.UnitsPerSide);
        Assert.Equal(200, PathQueryBenchmarkScenario.UnitCount);
        Assert.Equal(10000, TenThousandScaleAnchorBenchmarkScenario.UnitCount);
        Assert.NotEqual(
            OneThousandMoveBenchmarkScenario.DefaultMapPath,
            PathQueryBenchmarkScenario.DefaultMapPath);
    }

    [Fact]
    public void Create_UnknownScenario_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => SimulationBenchmarkScenario.Create("b1.does-not-exist"));

        Assert.Contains("b1.does-not-exist", ex.Message);
    }
}
