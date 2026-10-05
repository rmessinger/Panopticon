using System;
using Panopticon.Logic;
using Panopticon.Network;
using Panopticon.Simulation;

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
            OneThousandMoveBenchmarkScenario.ScenarioId => new OneThousandMoveBenchmarkScenario(),
            MixedCombatBenchmarkScenario.ScenarioId => new MixedCombatBenchmarkScenario(),
            PathQueryBenchmarkScenario.ScenarioId => new PathQueryBenchmarkScenario(),
            TenThousandScaleAnchorBenchmarkScenario.ScenarioId => new TenThousandScaleAnchorBenchmarkScenario(),
            _ => throw new ArgumentException($"Unknown benchmark scenario '{scenarioId}'."),
        };
    }

    /// <summary>
    /// Common boot: skirmish start with passive owners, inert victory rule, and skirmish AI
    /// nulled, so scenarios only differ in map, teams, spawn layout, and orders. Hostility
    /// comes from the team parameters, not from AI or victory rules.
    /// </summary>
    protected static SimulationBenchmarkRun BootSkirmish(
        SimulationBenchmarkPlan plan,
        string mapPath,
        int teamA = 0,
        int teamB = 0)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapPath);

        var installation = BenchmarkInstallationResolver.Resolve()
            ?? throw new InvalidOperationException(
                "No Command & Conquer Generals installation found; set CNC_GENERALS_PATH to the install root.");

        var game = new Game(
            installation,
            preferredBackend: null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true },
            window: null);

        game.StartSkirmishOrMultiPlayerGame(
            mapPath,
            new EchoConnection(),
            BenchmarkPlayerSettings.TwoSides(game, teamA, teamB),
            (int)plan.Seed,
            isMultiPlayer: false);
        game.StartRun();

        // Benchmark endings are frame budgets, not victory states, and map slots get skirmish
        // AI players at boot whose per-tick thinking would pollute measurements.
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        return new SimulationBenchmarkRun(host, game);
    }
}
