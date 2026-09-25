using System.Linq;
using System.Text.Json;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

// WI-P7-01: PlayerEliminated regression scenario + the P7 regression framework.
// The scenario JSON is a static checked-in input; the [Fact] loads it, validates authoring, drives
// the real VictorySystem to the terminal outcome headlessly, and proves the outcome assertion
// passes for the real outcome and fails for the default/sentinel InProgress outcome.

public class P7RegressionScenarioTests : P7RegressionTestBase
{
    private const EndState ScenarioEndState = EndState.PlayerEliminated;

    [Fact]
    public void PlayerEliminated_ScenarioFile_LoadsAsStaticData()
    {
        var scenario = RegressionSuite.LoadScenario(ScenarioEndState);

        Assert.NotNull(scenario);
        Assert.NotEmpty(scenario.MapPath);
        // Two players; the AI carries the attack that eliminates the human.
        Assert.Equal(2, scenario.Players.Count);
        var ai = scenario.Players.Single(p => p.Name == "P2-AI");
        Assert.NotEmpty(ai.Actions);
        var attack = ai.Actions.Single();
        Assert.Equal(OrderType.ForceAttackGround, attack.Command.OrderType);
    }

    [Fact]
    public void PlayerEliminated_ScenarioFile_MatchesFactoryAuthoring()
    {
        // The checked-in JSON must round-trip against the programmatic authoring so the static
        // input never drifts from the typed factory.
        var loaded = RegressionSuite.LoadScenario(ScenarioEndState);
        var authored = RegressionScenarioFactory.Build(ScenarioEndState);

        Assert.Equal(authored.MapPath, loaded.MapPath);
        Assert.Equal(authored.Players.Count, loaded.Players.Count);
        for (var i = 0; i < authored.Players.Count; i++)
        {
            Assert.Equal(authored.Players[i].Actions.Count, loaded.Players[i].Actions.Count);
        }
    }

    [Fact]
    public void PlayerEliminated_Outcome_PassesWhenHumanIsEliminated()
    {
        var result = DriveAndAssertOutcome(ScenarioEndState);

        Assert.True(result.Passed, result.Message ?? "(no message)");
        Assert.Equal(OutcomeStatus.Decided, CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, CurrentOutcome.Reason);
        Assert.Equal(AiPlayer, CurrentOutcome.WinningPlayer);
        Assert.Equal(HumanPlayer, CurrentOutcome.LosingPlayer);
    }

    [Fact]
    public void PlayerEliminated_Outcome_FailsAgainstDefaultSentinelOutcome()
    {
        // An unset GameOutcome (InProgress / None) must NOT satisfy the assertion.
        ConfigureSkirmish(Generals, 2);
        var assertion = Descriptor(ScenarioEndState).BuildOutcomeAssertion();

        var result = assertion.Evaluate(Generals, 0);

        Assert.False(result.Passed);
        Assert.Equal(OutcomeStatus.InProgress, CurrentOutcome.Status);
    }

    [Fact]
    public void PlayerEliminated_UniversalInvariants_PassAgainstDrivenState()
    {
        DriveAndAssertOutcome(ScenarioEndState);

        var results = EvaluateInvariants(Generals, 0).ToList();
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Passed, r.Message ?? "(no message)"));
    }
}

// ── WI-P7-02: PlayerSurrenders ──────────────────────────────────────────────

public class P7PlayerSurrendersTests : P7RegressionTestBase
{
    private const EndState ScenarioEndState = EndState.PlayerSurrenders;

    [Fact]
    public void PlayerSurrenders_ScenarioFile_LoadsAsStaticData()
    {
        var scenario = RegressionSuite.LoadScenario(ScenarioEndState);

        Assert.NotNull(scenario);
        var human = scenario.Players.Single(p => p.Name == "P1-Human");
        var endGame = Assert.Single(human.Actions);
        Assert.Equal(OrderType.EndGame, endGame.Command.OrderType);
    }

    [Fact]
    public void PlayerSurrenders_Outcome_PassesWhenHumanSurrenders()
    {
        var result = DriveAndAssertOutcome(ScenarioEndState);

        Assert.True(result.Passed, result.Message ?? "(no message)");
        Assert.Equal(OutcomeStatus.Decided, CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Surrendered, CurrentOutcome.Reason);
        Assert.Equal(AiPlayer, CurrentOutcome.WinningPlayer);
        Assert.Equal(HumanPlayer, CurrentOutcome.LosingPlayer);
    }

    [Fact]
    public void PlayerSurrenders_Outcome_FailsAgainstDefaultSentinelOutcome()
    {
        ConfigureSkirmish(Generals, 2);
        var assertion = Descriptor(ScenarioEndState).BuildOutcomeAssertion();

        var result = assertion.Evaluate(Generals, 0);

        Assert.False(result.Passed);
    }
}

// ── WI-P7-03: AISurrenders ──────────────────────────────────────────────────

public class P7AISurrendersTests : P7RegressionTestBase
{
    private const EndState ScenarioEndState = EndState.AISurrenders;

    [Fact]
    public void AISurrenders_ScenarioFile_LoadsAsStaticData()
    {
        var scenario = RegressionSuite.LoadScenario(ScenarioEndState);

        Assert.NotNull(scenario);
        var ai = scenario.Players.Single(p => p.Name == "P2-AI");
        var endGame = Assert.Single(ai.Actions);
        Assert.Equal(OrderType.EndGame, endGame.Command.OrderType);
    }

    [Fact]
    public void AISurrenders_Outcome_PassesWhenAISurrenders()
    {
        var result = DriveAndAssertOutcome(ScenarioEndState);

        Assert.True(result.Passed, result.Message ?? "(no message)");
        Assert.Equal(OutcomeStatus.Decided, CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Surrendered, CurrentOutcome.Reason);
        Assert.Equal(HumanPlayer, CurrentOutcome.WinningPlayer);
        Assert.Equal(AiPlayer, CurrentOutcome.LosingPlayer);
    }
}

// ── WI-P7-04: TeamWin ───────────────────────────────────────────────────────

public class P7TeamWinTests : P7RegressionTestBase
{
    private const EndState ScenarioEndState = EndState.TeamWin;

    [Fact]
    public void TeamWin_ScenarioFile_LoadsAsStaticData()
    {
        var scenario = RegressionSuite.LoadScenario(ScenarioEndState);

        Assert.NotNull(scenario);
        // Four players across two teams.
        Assert.Equal(4, scenario.Players.Count);
        var attacker = scenario.Players.Single(p => p.Name == "P1-Human");
        Assert.Equal(1, attacker.Team);
        Assert.NotEmpty(attacker.Actions);
    }

    [Fact]
    public void TeamWin_Outcome_PassesWhenLosingTeamEliminated()
    {
        var result = DriveAndAssertOutcome(ScenarioEndState);

        Assert.True(result.Passed, result.Message ?? "(no message)");
        Assert.Equal(OutcomeStatus.Decided, CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, CurrentOutcome.Reason);
        // The human (team 1) is on the winning side; team wins resolve multiple WinningPlayers.
        Assert.Contains(HumanPlayer, CurrentOutcome.WinningPlayers);
    }
}

// ── WI-P7-05: MutualElimination ─────────────────────────────────────────────

public class P7MutualEliminationTests : P7RegressionTestBase
{
    private const EndState ScenarioEndState = EndState.MutualElimination;

    [Fact]
    public void MutualElimination_ScenarioFile_LoadsAsStaticData()
    {
        var scenario = RegressionSuite.LoadScenario(ScenarioEndState);

        Assert.NotNull(scenario);
        // Both players attack each other in the same window.
        foreach (var player in scenario.Players)
        {
            Assert.NotEmpty(player.Actions);
        }
    }

    [Fact]
    public void MutualElimination_Outcome_ReachesSuddenDeathThenDecided()
    {
        // Drive the full two-stage path: simultaneous elimination -> SuddenDeath, then one side
        // re-eliminated -> Decided/Eliminated. The outcome assertion targets the terminal Decided.
        var result = DriveAndAssertOutcome(ScenarioEndState);

        Assert.True(result.Passed, result.Message ?? "(no message)");
        Assert.Equal(OutcomeStatus.Decided, CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.Eliminated, CurrentOutcome.Reason);
        Assert.Equal(AiPlayer, CurrentOutcome.WinningPlayer);
        Assert.Equal(HumanPlayer, CurrentOutcome.LosingPlayer);
    }

    [Fact]
    public void MutualElimination_SimultaneousElimination_EntersSuddenDeath()
    {
        // The SuddenDeath transition is a required acceptance point, not only the final Decided.
        ConfigureSkirmish(Generals, 2);
        Generals.VictorySystem.ConditionRule = new DefeatPlayersRule(HumanPlayer, AiPlayer);
        Generals.VictorySystem.QueuePlayerForEvaluation(HumanPlayer);
        Generals.VictorySystem.QueuePlayerForEvaluation(AiPlayer);
        Generals.VictorySystem.Tick(new LogicFrame(30));

        Assert.Equal(OutcomeStatus.SuddenDeath, CurrentOutcome.Status);
    }
}

// ── WI-P7-06: ScoreDecision ─────────────────────────────────────────────────

public class P7ScoreDecisionTests : P7RegressionTestBase
{
    private const EndState ScenarioEndState = EndState.ScoreDecision;

    [Fact]
    public void ScoreDecision_ScenarioFile_LoadsAsStaticData()
    {
        var scenario = RegressionSuite.LoadScenario(ScenarioEndState);

        Assert.NotNull(scenario);
        var human = scenario.Players.Single(p => p.Name == "P1-Human");
        Assert.NotEmpty(human.Actions);
    }

    [Fact]
    public void ScoreDecision_Outcome_PassesAtFrameLimitByScore()
    {
        var result = DriveAndAssertOutcome(ScenarioEndState);

        Assert.True(result.Passed, result.Message ?? "(no message)");
        Assert.Equal(OutcomeStatus.Decided, CurrentOutcome.Status);
        Assert.Equal(OutcomeReason.ScoreDecision, CurrentOutcome.Reason);
        Assert.Equal(HumanPlayer, CurrentOutcome.WinningPlayer);
    }

    [Fact]
    public void ScoreDecision_Outcome_NotReachedBeforeFrameLimit()
    {
        // Sentinel: the default NoFrameLimit value must not produce a ScoreDecision.
        ConfigureSkirmish(Generals, 2);
        Generals.VictorySystem.ConditionRule = new DefeatNobodyRule();
        Generals.VictorySystem.Tick(new LogicFrame(30));

        Assert.Equal(OutcomeStatus.InProgress, CurrentOutcome.Status);
        Assert.False(Descriptor(ScenarioEndState).BuildOutcomeAssertion().Evaluate(Generals, 0).Passed);
    }
}
