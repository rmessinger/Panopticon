using System;
using System.Collections.Generic;
using Panopticon.Logic.Object;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// `b1.10k-scale-anchor`: 10000 Rangers in a centered grid on the largest official map, every
/// tenth unit ordered across the map. The near-term scale anchor from the scale-anchor
/// decision: full object field plus 1000 movers.
/// </summary>
public sealed class TenThousandScaleAnchorBenchmarkScenario : SimulationBenchmarkScenario
{
    public const string ScenarioId = "b1.10k-scale-anchor";

    public const string DefaultMapPath = "maps/Twilight Flame/Twilight Flame.map";

    public const string UnitDefinition = "AmericaInfantryRanger";

    public const int UnitCount = 10000;

    public const int MoverInterval = 10;

    private const float Spacing = 12f;

    private const int Columns = 100;

    private readonly List<GameObject> _units = [];

    public override string Id => ScenarioId;

    public override SimulationBenchmarkRun CreateRun(SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var run = BootSkirmish(plan, plan.MapPath ?? DefaultMapPath);
        var game = run.Game;
        var owner = game.Scene3D.Players[2];
        var (originX, originY) = BenchmarkUnitSpawner.OriginInUsableArea(game, 0.3f, 0.3f);

        _units.Clear();
        for (var i = 0; i < UnitCount; i++)
        {
            var (x, y) = BenchmarkUnitSpawner.GridPosition(i, Columns, originX, originY, Spacing);
            var unit = BenchmarkUnitSpawner.SpawnAt(game, UnitDefinition, owner, x, y);
            if (unit is null)
            {
                throw new InvalidOperationException($"Definition '{UnitDefinition}' not found in the asset store.");
            }

            _units.Add(unit);
        }

        return run;
    }

    public override void ConfigureOrders(SimulationHost host, SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(host);

        var game = host.Game as Game ?? throw new InvalidOperationException("Scenario requires a concrete Game.");
        var (targetX, targetY) = BenchmarkUnitSpawner.OriginInUsableArea(game, 0.9f, 0.9f);
        var height = game.Scene3D.Terrain.GetGroundHeight(new System.Numerics.Vector2(targetX, targetY)) ?? 0f;
        var target = new System.Numerics.Vector3(targetX, targetY, height);

        for (var i = 0; i < _units.Count; i += MoverInterval)
        {
            var unit = _units[i];
            if (!unit.IsDestroyed)
            {
                unit.AIUpdate?.SetTargetPoint(target);
            }
        }
    }
}
