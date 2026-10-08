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
            AiPolicyBenchmarkScenario.ScenarioId => new AiPolicyBenchmarkScenario(),
            _ => throw new ArgumentException($"Unknown benchmark scenario '{scenarioId}'."),
        };
    }

    /// <summary>
    /// Player settings for the boot. The default yields passive owners on the first two
    /// playable sides; scenarios override to promote a slot to an AI owner or attach an
    /// experiment-selected policy (WI-A4-41).
    /// </summary>
    protected virtual PlayerSetting[] CreatePlayerSettings(Game game, int teamA, int teamB)
    {
        return BenchmarkPlayerSettings.TwoSides(game, teamA, teamB);
    }

    /// <summary>
    /// Player post-boot configuration. The default disables all skirmish AI so per-tick
    /// thinking never pollutes measurements; scenarios that keep an AI slot override this.
    /// </summary>
    protected virtual void ConfigurePlayers(Game game)
    {
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }
    }

    /// <summary>
    /// Common boot: skirmish start with the scenario's player settings, an inert victory
    /// rule, and skirmish AI nulled by default, so scenarios only differ in map, teams,
    /// spawn layout, orders, and player configuration. Hostility comes from the team
    /// parameters, not from AI or victory rules.
    /// </summary>
    protected SimulationBenchmarkRun BootSkirmish(
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
            CreatePlayerSettings(game, teamA, teamB),
            (int)plan.Seed,
            isMultiPlayer: false);
        game.StartRun();

        // Benchmark endings are frame budgets, not victory states.
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        ConfigurePlayers(game);

        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        return new SimulationBenchmarkRun(host, game);
    }
}
