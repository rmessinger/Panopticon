using System.Collections.Generic;
using System.Linq;
using Panopticon;
using Panopticon.Data.Map;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;
using Player = Panopticon.Logic.Player;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Shared base for the P7 regression scenario tests. Loads checked-in scenario JSON, drives the
/// real <see cref="VictorySystem"/> to each scenario's terminal <c>GameOutcome</c> headlessly
/// (no game install) on a per-cell game instance, and evaluates the outcome assertion plus the
/// universal invariants.
/// </summary>
public abstract class P7RegressionTestBase : MockedGameTest
{
    protected static RegressionScenario Descriptor(EndState endState) => RegressionSuite.Get(endState);

    private protected static TestGame CreateGame() => new(SageGame.CncGenerals);

    private protected static Player HumanPlayerOf(TestGame game) =>
        game.PlayerManager.Players[RegressionSuite.HumanPlayerIndex];

    private protected static Player AiPlayerOf(TestGame game) =>
        game.PlayerManager.Players[RegressionSuite.AiPlayerIndex];

    /// <summary>
    /// Configures a skirmish with the given number of competitive players (indices 2..), all marked
    /// human so the AI-progress invariant vacuously passes in the headless driver. The invariant's
    /// failure path is covered by <c>P7RegressionMatrixTests</c>.
    /// </summary>
    private protected static void ConfigureSkirmish(TestGame game, int competitivePlayers, bool aiIsHuman = true)
    {
        var mapPlayers = new List<Panopticon.Data.Map.Player>
        {
            Panopticon.Data.Map.Player.CreateNeutralPlayer(),
            Panopticon.Data.Map.Player.CreateCivilianPlayer(),
        };

        var settings = new List<PlayerSetting>();
        var factions = new[] { "FactionAmerica", "FactionGLA", "FactionChina", "FactionAmerica" };

        for (var i = 0; i < competitivePlayers; i++)
        {
            var idx = i + 2;
            // First half of competitive players on team 1, second half on team 2.
            var team = i < competitivePlayers / 2 ? 1 : 2;
            var owner = aiIsHuman ? PlayerOwner.Player : (i == 0 ? PlayerOwner.Player : PlayerOwner.EasyAi);
            mapPlayers.Add(new Panopticon.Data.Map.Player
            {
                Name = $"player{idx}",
                Faction = factions[i % factions.Length],
                DisplayName = $"Player {idx}",
                IsHuman = aiIsHuman || i == 0,
            });
            settings.Add(new PlayerSetting(
                idx,
                factions[i % factions.Length],
                new ColorRgb(255, (byte)(64 + i * 40), 64),
                team,
                owner,
                $"player{idx}"));
        }

        game.PlayerManager.OnNewGame(mapPlayers.ToArray(), GameType.Skirmish, settings.ToArray());
        game.VictorySystem.CurrentOutcome.Reset();
    }

    // Instance convenience wrappers for the per-scenario tests (single shared Generals game).
    protected Player HumanPlayer => HumanPlayerOf(Generals);
    protected Player AiPlayer => AiPlayerOf(Generals);
    protected Panopticon.Logic.GameOutcome CurrentOutcome => Generals.VictorySystem.CurrentOutcome;

    protected AssertionResult DriveAndAssertOutcome(EndState endState) =>
        DriveAndAssertOutcome(endState, Generals);

    /// <summary>Drives the VictorySystem to the scenario's terminal outcome and returns its assertion result.</summary>
    private protected static AssertionResult DriveAndAssertOutcome(EndState endState, TestGame game)
    {
        RegressionInvariants.EnsureRegistered();
        switch (endState)
        {
            case EndState.PlayerEliminated:
                ConfigureSkirmish(game, 2);
                game.VictorySystem.ConditionRule = new DefeatPlayerRule(HumanPlayerOf(game));
                game.VictorySystem.QueuePlayerForEvaluation(HumanPlayerOf(game));
                game.VictorySystem.Tick(new LogicFrame(30));
                break;
            case EndState.PlayerSurrenders:
                ConfigureSkirmish(game, 2);
                game.VictorySystem.Surrender(HumanPlayerOf(game));
                break;
            case EndState.AISurrenders:
                ConfigureSkirmish(game, 2);
                game.VictorySystem.Surrender(AiPlayerOf(game));
                break;
            case EndState.TeamWin:
                ConfigureSkirmish(game, 4);
                game.VictorySystem.ConditionRule = new DefeatPlayersRule(
                    game.PlayerManager.Players[4],
                    game.PlayerManager.Players[5]);
                game.VictorySystem.QueuePlayerForEvaluation(game.PlayerManager.Players[4]);
                game.VictorySystem.QueuePlayerForEvaluation(game.PlayerManager.Players[5]);
                game.VictorySystem.Tick(new LogicFrame(30));
                break;
            case EndState.MutualElimination:
                ConfigureSkirmish(game, 2);
                game.VictorySystem.ConditionRule = new DefeatPlayersRule(HumanPlayerOf(game), AiPlayerOf(game));
                game.VictorySystem.QueuePlayerForEvaluation(HumanPlayerOf(game));
                game.VictorySystem.QueuePlayerForEvaluation(AiPlayerOf(game));
                game.VictorySystem.Tick(new LogicFrame(30));
                game.VictorySystem.ConditionRule = new DefeatPlayerRule(HumanPlayerOf(game));
                game.VictorySystem.QueuePlayerForEvaluation(HumanPlayerOf(game));
                game.VictorySystem.Tick(new LogicFrame(60));
                break;
            case EndState.ScoreDecision:
                ConfigureSkirmish(game, 2);
                game.VictorySystem.ConditionRule = new DefeatNobodyRule();
                game.VictorySystem.FrameLimit = 60;
                game.VictorySystem.ScoreMetric = p => p == HumanPlayerOf(game) ? 10 : 5;
                game.VictorySystem.Tick(new LogicFrame(60));
                break;
        }

        return Descriptor(endState).BuildOutcomeAssertion().Evaluate(game, 0);
    }

    private protected static IEnumerable<AssertionResult> EvaluateInvariants(TestGame game, uint frame)
    {
        RegressionInvariants.EnsureRegistered();
        foreach (var invariant in RegressionInvariants.Assertions)
        {
            yield return invariant.Evaluate(game, frame);
        }
    }

    // ── Matrix helpers shared by P7RegressionMatrixTests and the canonical gate ─

    private static AssertionResult WithId(AssertionResult result, string id) => new()
    {
        AssertionId = id,
        Passed = result.Passed,
        ActualValue = result.ActualValue,
        FailFrame = result.FailFrame,
        Message = result.Message,
    };

    /// <summary>Runs one end-state cell (outcome + three invariants) on a fresh game.</summary>
    private protected static CellResult RunRegressionCell(EndState endState)
    {
        var game = CreateGame();
        var bindings = new Dictionary<string, string> { ["endState"] = endState.ToString() };
        var results = new List<AssertionResult>
        {
            WithId(DriveAndAssertOutcome(endState, game), $"{endState}.outcome"),
        };
        foreach (var invariantResult in EvaluateInvariants(game, 0))
        {
            results.Add(WithId(invariantResult, $"{endState}.{invariantResult.AssertionId}"));
        }

        return new CellResult(bindings, new AssertionReport(results));
    }

    /// <summary>Runs all six end-state cells and aggregates into the harness report.</summary>
    private protected static MatrixReport RunRegressionMatrix() =>
        new(RegressionSuite.BuildMatrix().Expand()
            .Select(cell => RunRegressionCell(ParseEndState(cell.Bindings["endState"])))
            .ToList());

    private protected static EndState ParseEndState(string value) =>
        System.Enum.Parse<EndState>(value, ignoreCase: true);

    // ── VictoryConditionRule stubs for deterministic headless defeat ──────────

    internal sealed class DefeatPlayerRule : VictoryConditionRule
    {
        private readonly Player _defeated;
        public DefeatPlayerRule(Player defeated) => _defeated = defeated;
        internal override bool IsPlayerDefeated(Player player, VictorySystem vs) => player == _defeated;
    }

    internal sealed class DefeatPlayersRule : VictoryConditionRule
    {
        private readonly HashSet<Player> _defeated;
        public DefeatPlayersRule(params Player[] defeated) => _defeated = [.. defeated];
        internal override bool IsPlayerDefeated(Player player, VictorySystem vs) => _defeated.Contains(player);
    }

    internal sealed class DefeatNobodyRule : VictoryConditionRule
    {
        internal override bool IsPlayerDefeated(Player player, VictorySystem vs) => false;
    }
}
