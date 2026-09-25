using System.Text.Json;
using Panopticon.Tools.ReplaySketch.Assertions;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class AssertionLayerTests
{
    [Fact]
    public void ScenarioAssertion_PolymorphicJson_RoundTripsKinds()
    {
        ScenarioAssertion state = new StateAssertion
        {
            Id = "state-1",
            Path = "player[0].money",
            Op = ">=",
            Value = 0,
        };
        ScenarioAssertion invariant = new InvariantAssertion
        {
            Id = "inv-1",
            Path = "player[0].money",
            Op = ">=",
            Value = 0,
        };
        ScenarioAssertion outcome = new OutcomeAssertion
        {
            Id = "out-1",
            ExpectedStatus = "Decided",
        };

        var stateJson = JsonSerializer.Serialize(state);
        var invariantJson = JsonSerializer.Serialize(invariant);
        var outcomeJson = JsonSerializer.Serialize(outcome);

        Assert.Contains("\"kind\":\"state\"", stateJson);
        Assert.Contains("\"kind\":\"invariant\"", invariantJson);
        Assert.Contains("\"kind\":\"outcome\"", outcomeJson);

        Assert.IsType<StateAssertion>(JsonSerializer.Deserialize<ScenarioAssertion>(stateJson));
        Assert.IsType<InvariantAssertion>(JsonSerializer.Deserialize<ScenarioAssertion>(invariantJson));
        Assert.IsType<OutcomeAssertion>(JsonSerializer.Deserialize<ScenarioAssertion>(outcomeJson));
    }

    [Fact]
    public void ReplayScenario_RoundTrips_WithAssertionsArray()
    {
        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            Assertions =
            [
                new StateAssertion
                {
                    Id = "money-non-negative",
                    Path = "player[0].money",
                    Op = ">=",
                    Value = 0,
                },
            ],
        };

        var json = JsonSerializer.Serialize(scenario);
        var roundTrip = JsonSerializer.Deserialize<ReplayScenario>(json);

        Assert.NotNull(roundTrip);
        Assert.Single(roundTrip!.Assertions);
        Assert.IsType<StateAssertion>(roundTrip.Assertions[0]);
    }

    [Fact]
    public void AssertionReport_ToJson_EmitsExpectedShape()
    {
        var report = new AssertionReport(
        [
            new AssertionResult
            {
                AssertionId = "a-1",
                Passed = true,
            },
            new AssertionResult
            {
                AssertionId = "a-2",
                Passed = false,
                ActualValue = "12",
                FailFrame = 42,
                Message = "expected >= 20",
            },
        ]);

        var json = report.ToJson();

        Assert.Contains("\"allPassed\":false", json);
        Assert.Contains("\"id\":\"a-1\"", json);
        Assert.Contains("\"id\":\"a-2\"", json);
        Assert.Contains("\"failFrame\":42", json);
    }
}
