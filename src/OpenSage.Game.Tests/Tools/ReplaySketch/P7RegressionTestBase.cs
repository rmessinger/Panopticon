using System.Collections.Generic;
using System.Linq;
using OpenSage;
using OpenSage.Data.Map;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;
using Player = OpenSage.Logic.Player;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// Shared base for the P7 regression scenario tests. Loads checked-in scenario JSON, drives the
/// real <see cref="VictorySystem"/> to each scenario's terminal <c>GameOutcome</c> headlessly
/// (no game install), and evaluates the outcome assertion plus the universal invariants.
/// </summary>
public abstract class P7RegressionTestBase : MockedGameTest
{
    protected static RegressionScenario Descriptor(EndState endState) => RegressionSuite.Get(endState);

    protected OpenSage.Logic.GameOutcome CurrentOutcome => Generals.VictorySystem.CurrentOutcome;

    /// <summary>
    /// Configures a skirmish with the given number of competitive players (indices 2..), all marked
    /// human so the AI-progress invariant vacuously passes in the headless driver. The invariant's
    /// failure path is covered by <c>P7RegressionMatrixTests</c>.
    /// </summary>
    protected void ConfigureSkirmish(int competitivePlayers, bool aiIsHuman = true)
    {
        // Map players: index 0 = neutral, 1 = civilian, then competitive players at 2..
        var mapPlayers = new List<OpenSage.Data.Map.Player>
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
        };

        // PlayerSettings apply starting at competitive index 2 (OnNewGame offsets them), so the
        // settings list contains ONLY the competitive players, not neutral/civilian.
        var settings = new List<PlayerSetting>();
        var factions = new[] { "FactionAmerica", "FactionGLA", "FactionChina", "FactionAmerica" };

        for (var i = 0; i < competitivePlayers; i++)
        {
            var idx = i + 2;
            // First half of competitive players on team 1, second half on team 2, so 2-player and
            // 4-player setups both form two opposing teams.
            var team = i < competitivePlayers / 2 ? 1 : 2;
            var owner = aiIsHuman ? PlayerOwner.Player : (i == 0 ? PlayerOwner.Player : PlayerOwner.EasyAi);
            mapPlayers.Add(new OpenSage.Data.Map.Player
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

        Generals.PlayerManager.OnNewGame(mapPlayers.ToArray(), GameType.Skirmish, settings.ToArray());

        // Reset any prior outcome so each test starts from InProgress.
        Generals.VictorySystem.CurrentOutcome.Reset();
    }

    protected Player HumanPlayer => Generals.PlayerManager.Players[RegressionSuite.HumanPlayerIndex];
    protected Player AiPlayer => Generals.PlayerManager.Players[RegressionSuite.AiPlayerIndex];

    /// <summary>Drives the VictorySystem to the scenario's terminal outcome and returns the result of its outcome assertion.</summary>
    protected AssertionResult DriveAndAssertOutcome(EndState endState)
    {
        RegressionInvariants.EnsureRegistered();
        switch (endState)
        {
            case EndState.PlayerEliminated:
                ConfigureSkirmish(2);
                Generals.VictorySystem.ConditionRule = new DefeatPlayerRule(HumanPlayer);
                Generals.VictorySystem.QueuePlayerForEvaluation(HumanPlayer);
                Generals.VictorySystem.Tick(new LogicFrame(30));
                break;
            case EndState.PlayerSurrenders:
                ConfigureSkirmish(2);
                Generals.VictorySystem.Surrender(HumanPlayer);
                break;
            case EndState.AISurrenders:
                ConfigureSkirmish(2);
                Generals.VictorySystem.Surrender(AiPlayer);
                break;
            case EndState.TeamWin:
                // Four players: team 1 = indices 2,3 ; team 2 = indices 4,5. Defeat team 2.
                ConfigureSkirmish(4);
                Generals.VictorySystem.ConditionRule = new DefeatPlayersRule(
                    Generals.PlayerManager.Players[4],
                    Generals.PlayerManager.Players[5]);
                Generals.VictorySystem.QueuePlayerForEvaluation(Generals.PlayerManager.Players[4]);
                Generals.VictorySystem.QueuePlayerForEvaluation(Generals.PlayerManager.Players[5]);
                Generals.VictorySystem.Tick(new LogicFrame(30));
                break;
            case EndState.MutualElimination:
                ConfigureSkirmish(2);
                // Both sides eliminated simultaneously -> SuddenDeath.
                Generals.VictorySystem.ConditionRule = new DefeatPlayersRule(HumanPlayer, AiPlayer);
                Generals.VictorySystem.QueuePlayerForEvaluation(HumanPlayer);
                Generals.VictorySystem.QueuePlayerForEvaluation(AiPlayer);
                Generals.VictorySystem.Tick(new LogicFrame(30));
                // Then one side is re-eliminated -> Decided.
                Generals.VictorySystem.ConditionRule = new DefeatPlayerRule(HumanPlayer);
                Generals.VictorySystem.QueuePlayerForEvaluation(HumanPlayer);
                Generals.VictorySystem.Tick(new LogicFrame(60));
                break;
            case EndState.ScoreDecision:
                ConfigureSkirmish(2);
                Generals.VictorySystem.ConditionRule = new DefeatNobodyRule();
                Generals.VictorySystem.FrameLimit = 60;
                Generals.VictorySystem.ScoreMetric = p => p == HumanPlayer ? 10 : 5;
                Generals.VictorySystem.Tick(new LogicFrame(60));
                break;
        }

        var descriptor = Descriptor(endState);
        return descriptor.BuildOutcomeAssertion().Evaluate(Generals, 0);
    }

    protected static IEnumerable<AssertionResult> EvaluateInvariants(IGame game, uint frame)
    {
        RegressionInvariants.EnsureRegistered();
        foreach (var invariant in RegressionInvariants.Assertions)
        {
            yield return invariant.Evaluate(game, frame);
        }
    }

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
