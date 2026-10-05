using System;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// A named B1 macro scenario: owns game construction (map, players, rules) for a plan and
/// returns a booted run. Scenarios never touch the harness measurement loop.
/// </summary>
public abstract class SimulationBenchmarkScenario
{
    public abstract string Id { get; }

    /// <summary>Boots the game for the plan and wraps it in a disposable run scope.</summary>
    public abstract SimulationBenchmarkRun CreateRun(SimulationBenchmarkPlan plan);

    /// <summary>Injects scheduled orders after the host exists; the default injects nothing.</summary>
    public virtual void ConfigureOrders(SimulationHost host, SimulationBenchmarkPlan plan)
    {
    }

    public static SimulationBenchmarkScenario Create(string scenarioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);

        return scenarioId switch
        {
            EmptyHostBenchmarkScenario.ScenarioId => new EmptyHostBenchmarkScenario(),
            _ => throw new ArgumentException($"Unknown benchmark scenario '{scenarioId}'."),
        };
    }
}
