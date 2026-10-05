using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.Object;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// `b1.mixed-combat`: two 300-strong Ranger armies on hostile teams, placed within weapon
/// range and paired with direct weapon targets. Weapons fire from the first measured tick;
/// deaths churn the object field while idle auto-acquire re-engages survivors.
/// </summary>
public sealed class MixedCombatBenchmarkScenario : SimulationBenchmarkScenario
{
    public const string ScenarioId = "b1.mixed-combat";

    public const string DefaultMapPath = "maps/Dark Mountain/Dark Mountain.map";

    public const string UnitDefinition = "AmericaInfantryRanger";

    public const int UnitsPerSide = 300;

    // Two ranks 80 world units apart, inside the Ranger's 100-unit weapon range, so the fight
    // starts without movement (moving clears weapon targets until arrival).
    private const float RankSeparation = 80f;

    private const float Spacing = 20f;

    private const int Columns = 30;

    // Six attackers per victim so deaths (and the object churn they cause) begin within a
    // reasonable frame budget instead of after twelve seconds of one-on-one fire.
    private const int FocusFactor = 6;

    // Standardized low HP (validation-ground pattern): turret auto-acquire re-engages freely
    // and overrides scripted pairings, so fast kills come from low victim HP, not from
    // insisting on a particular attacker-to-victim mapping.
    private const float UnitHp = 30f;

    private readonly List<GameObject> _sideA = [];

    private readonly List<GameObject> _sideB = [];

    public override string Id => ScenarioId;

    public override SimulationBenchmarkRun CreateRun(SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var run = BootSkirmish(plan, plan.MapPath ?? DefaultMapPath, teamA: 0, teamB: 1);
        var game = run.Game;
        var playerA = game.Scene3D.Players[2];
        var playerB = game.Scene3D.Players[3];
        var (originX, originY) = BenchmarkUnitSpawner.OriginInUsableArea(game, 0.35f, 0.45f);

        _sideA.Clear();
        _sideB.Clear();
        SpawnRank(game, playerA, _sideA, originX, originY, 0f);
        SpawnRank(game, playerB, _sideB, originX, originY + RankSeparation, MathF.PI);

        return run;
    }

    public override void ConfigureOrders(SimulationHost host, SimulationBenchmarkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(host);

        var game = host.Game as Game ?? throw new InvalidOperationException("Scenario requires a concrete Game.");

        // Weapon slots are not usable until the game has ticked at least once, so pairs are
        // assigned from the first tick and re-armed every 30 frames for units whose weapon
        // lost its target (partner died), keeping both sides engaged without AI.
        host.TickStarting += (_, e) =>
        {
            if (e.Frame == 1 || e.Frame % 30 == 0)
            {
                AssignPairedTargets(game);
            }
        };
    }

    private void AssignPairedTargets(Game game)
    {
        var livingA = _sideA.Where(u => !u.IsDestroyed).ToList();
        var livingB = _sideB.Where(u => !u.IsDestroyed).ToList();
        if (livingA.Count == 0 || livingB.Count == 0)
        {
            return;
        }

        var focusA = Math.Max(1, livingA.Count / FocusFactor);
        var focusB = Math.Max(1, livingB.Count / FocusFactor);
        for (var i = 0; i < UnitsPerSide; i++)
        {
            var a = _sideA[i];
            if (!a.IsDestroyed)
            {
                a.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, livingB[i % focusB].Id));
            }

            var b = _sideB[i];
            if (!b.IsDestroyed)
            {
                b.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, livingA[i % focusA].Id));
            }
        }
    }

    private void SpawnRank(Game game, Player owner, List<GameObject> side, float originX, float originY, float yaw)
    {
        for (var i = 0; i < UnitsPerSide; i++)
        {
            var (x, y) = BenchmarkUnitSpawner.GridPosition(i, Columns, originX, originY, Spacing);
            var unit = BenchmarkUnitSpawner.SpawnAt(game, UnitDefinition, owner, x, y, yaw);
            if (unit is null)
            {
                throw new InvalidOperationException($"Definition '{UnitDefinition}' not found in the asset store.");
            }

            unit.BodyModule?.SetMaxHealth(UnitHp, MaxHealthChangeType.FullyHeal);
            side.Add(unit);
        }
    }
}
