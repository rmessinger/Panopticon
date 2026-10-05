using System;
using System.Collections.Generic;
using Panopticon.Logic.Object;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// `b1.path-query`: 200 Rangers on the largest official map, each re-targeted to a seeded
/// random far point every 150 frames, for sustained pathfinding pressure on long distances.
/// </summary>
public sealed class PathQueryBenchmarkScenario : SimulationBenchmarkScenario
{
    public const string ScenarioId = "b1.path-query";

    public const string DefaultMapPath = "maps/Twilight Flame/Twilight Flame.map";

    public const string UnitDefinition = "AmericaInfantryRanger";

    public const int UnitCount = 200;

    public const uint RetargetIntervalFrames = 150;

    private const float Spacing = 20f;

    private const int Columns = 20;

    private readonly List<GameObject> _units = [];

    public override string Id => ScenarioId;

    public override SimulationBenchmarkRun CreateRun(SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var run = BootSkirmish(plan, plan.MapPath ?? DefaultMapPath);
        var game = run.Game;
        var owner = game.Scene3D.Players[2];
        var (originX, originY) = BenchmarkUnitSpawner.OriginInUsableArea(game, 0.45f, 0.45f);

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
        var random = new Random((int)plan.Seed);
        RetargetAll(game, random);

        host.TickStarting += (_, _) =>
        {
            if (host.Frame % RetargetIntervalFrames == 0)
            {
                RetargetAll(game, random);
            }
        };
    }

    private void RetargetAll(Game game, Random random)
    {
        var heightMap = game.Scene3D.Terrain.HeightMap;
        var maxX = heightMap.MaxXCoordinate;
        var maxY = heightMap.MaxYCoordinate;

        foreach (var unit in _units)
        {
            if (unit.IsDestroyed)
            {
                continue;
            }

            var x = maxX * (0.1f + 0.8f * (float)random.NextDouble());
            var y = maxY * (0.1f + 0.8f * (float)random.NextDouble());
            var height = game.Scene3D.Terrain.GetGroundHeight(new System.Numerics.Vector2(x, y)) ?? 0f;
            unit.AIUpdate?.SetTargetPoint(new System.Numerics.Vector3(x, y, height));
        }
    }
}
