using System;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Network;
using Panopticon.Simulation;

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

        var installation = BenchmarkInstallationResolver.Resolve()
            ?? throw new InvalidOperationException(
                "No Command & Conquer Generals installation found; set CNC_GENERALS_PATH to the install root.");

        var game = new Game(
            installation,
            preferredBackend: null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true },
            window: null);

        var sides = game.GetPlayableSides().ToArray();
        var playerSettings = new[]
        {
            new PlayerSetting(
                0,
                sides[0].Name,
                game.AssetStore.MultiplayerColors.GetByIndex(0).RgbColor,
                0,
                PlayerOwner.None,
                "bench-a"),
            new PlayerSetting(
                1,
                sides[sides.Length > 1 ? 1 : 0].Name,
                game.AssetStore.MultiplayerColors.GetByIndex(1).RgbColor,
                0,
                PlayerOwner.None,
                "bench-b"),
        };

        game.StartSkirmishOrMultiPlayerGame(
            plan.MapPath ?? DefaultMapPath,
            new EchoConnection(),
            playerSettings,
            (int)plan.Seed,
            isMultiPlayer: false);
        game.StartRun();

        // The empty host must neither end early nor run skirmish AI: no units exist to satisfy
        // a victory rule, and map slots get AI players at boot whose per-tick thinking would
        // pollute the host-overhead measurement.
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        return new SimulationBenchmarkRun(host, game);
    }
}
