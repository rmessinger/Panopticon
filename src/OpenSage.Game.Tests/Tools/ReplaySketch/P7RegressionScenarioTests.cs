using System.Linq;
using System.Text.Json;
using OpenSage.Logic;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

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
        ConfigureSkirmish(2);
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
