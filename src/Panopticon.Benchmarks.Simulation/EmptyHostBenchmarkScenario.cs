using System;
using System.Linq;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// `b1.empty-host`: a skirmish boot with two passive players, no units, no orders, and an
/// inert victory rule, so the only measured cost is host stepping plus recorder overhead.
/// </summary>
public sealed class EmptyHostBenchmarkScenario : SimulationBenchmarkScenario
{
    public const string ScenarioId = "b1.empty-host";

    public const string DefaultMapPath = "maps/alpine assault/alpine assault.map";

    public override string Id => ScenarioId;

    public override SimulationBenchmarkRun CreateRun(SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return BootSkirmish(plan, plan.MapPath ?? DefaultMapPath);
    }
}
