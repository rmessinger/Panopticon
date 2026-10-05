using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// `b1.1k-move`: 1000 Paladins in a corner grid, all ordered across the map on one target
/// point; a movement and partition baseline with real pathfinding.
/// </summary>
public sealed class OneThousandMoveBenchmarkScenario : SimulationBenchmarkScenario
{
    public const string ScenarioId = "b1.1k-move";

    public const string DefaultMapPath = "maps/Dark Mountain/Dark Mountain.map";

    public const string UnitDefinition = "AmericaTankPaladin";

    public const int UnitCount = 1000;

    private const float Spacing = 60f;

    private const int Columns = 50;

    private readonly List<GameObject> _units = [];

    public override string Id => ScenarioId;

    public override SimulationBenchmarkRun CreateRun(SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var run = BootSkirmish(plan, plan.MapPath ?? DefaultMapPath);
        var game = run.Game;
        var owner = game.Scene3D.Players[2];
        var (originX, originY) = BenchmarkUnitSpawner.OriginInUsableArea(game, 0.1f, 0.1f);

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
        ArgumentNullException.ThrowIfNull(plan);

        var game = host.Game as Game ?? throw new InvalidOperationException("Scenario requires a concrete Game.");
        var (targetX, targetY) = BenchmarkUnitSpawner.OriginInUsableArea(game, 0.9f, 0.9f);
        var height = game.Scene3D.Terrain.GetGroundHeight(new System.Numerics.Vector2(targetX, targetY)) ?? 0f;
        var target = new System.Numerics.Vector3(targetX, targetY, height);

        foreach (var unit in _units.Where(u => !u.IsDestroyed))
        {
            unit.AIUpdate?.SetTargetPoint(target);
        }
    }
}
