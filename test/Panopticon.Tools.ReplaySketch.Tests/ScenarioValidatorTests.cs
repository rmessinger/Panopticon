using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-10: declarative scenario validation. Diagnostics carry stable field paths, report in a
/// stable order, and a valid fixture produces none.
/// </summary>
public sealed class ScenarioValidatorTests
{
    [Fact]
    public void ValidScenario_HasNoDiagnostics()
    {
        var report = ScenarioValidator.Validate(BuildValidScenario());

        Assert.True(report.IsValid);
        Assert.Empty(report.Diagnostics);
    }

    [Fact]
    public void FieldGroupFailures_ReportStablePathsInOrder()
    {
        var scenario = BuildValidScenario();
        scenario.MapPath = "";
        scenario.DomainId = "";
        scenario.Players[0].Name = "";
        scenario.Players[0].FactionIndex = -1;
        scenario.Placements.Add(new ScenarioPlacement
        {
            DefinitionName = "",
            PlayerIndex = 5,
            Position = new NormalizedPosition(0.5f, 0.5f),
            Quantity = 0,
        });
        scenario.Parameters[""] = "value";
        scenario.StopConditions.Add(new FrameCapStopCondition(0));
        scenario.RecordedMetricSeries.Add("");

        var report = ScenarioValidator.Validate(scenario);

        Assert.False(report.IsValid);
        var paths = report.Diagnostics.Select(d => d.FieldPath).ToArray();
        Assert.Equal(
        [
            "mapPath",
            "domainId",
            "players[0].name",
            "players[0].factionIndex",
            "placements[1].definitionName",
            "placements[1].playerIndex",
            "placements[1].quantity",
            "parameters",
            "stopConditions[1].maxFrames",
            "recordedMetricSeries[1]",
        ], paths);
    }

    [Fact]
    public void DuplicateActionId_IsReportedAtSecondOccurrence()
    {
        var scenario = BuildValidScenario();
        var first = BuildAction("dup");
        var second = BuildAction("dup");
        scenario.Players[0].Actions.Add(first);
        scenario.Players[1].Actions.Add(second);

        var report = ScenarioValidator.Validate(scenario);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "players[1].actions[1].id");
    }

    [Fact]
    public void AfterActionTrigger_UnknownIdIsReported_KnownIdPasses()
    {
        var scenario = BuildValidScenario();
        scenario.Players[0].Actions.Add(BuildAction("anchor"));
        scenario.Players[0].Actions.Add(new ActionEntry(
            "after",
            CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
            position: null,
            timing: new AfterActionTiming("anchor", ActionPhase.Completed, 10)));

        Assert.True(ScenarioValidator.Validate(scenario).IsValid);

        scenario.Players[0].Actions.Add(new ActionEntry(
            "after-unknown",
            CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
            position: null,
            timing: new AfterActionTiming("missing", ActionPhase.Completed, 10)));

        var report = ScenarioValidator.Validate(scenario);
        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "players[0].actions[3].timing.triggerActionId");
    }

    [Fact]
    public void HealthAboveMaxHealth_IsReportedForPlacementAndGenerator()
    {
        var scenario = BuildValidScenario();
        scenario.Placements[0].Health = 90;
        scenario.Placements[0].MaxHealth = 50;
        scenario.PopulationGenerators.Add(new GridPopulationGenerator(
            PlayerIndex: 0,
            DefinitionName: "AmericaInfantryRanger",
            Rows: 2,
            Columns: 2,
            OriginX: 0.5f,
            OriginY: 0.5f,
            SpacingX: 0.01f,
            SpacingY: 0.01f)
        {
            Health = 80,
            MaxHealth = 40,
        });

        var report = ScenarioValidator.Validate(scenario);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "placements[0].health");
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "populationGenerators[0].health");
    }

    [Fact]
    public void Recording_WithZeroIntervals_IsReported()
    {
        var scenario = BuildValidScenario();
        scenario.Recording = new ScenarioRecordingOptions
        {
            SampleInterval = 0,
            SnapshotInterval = 300,
        };

        var report = ScenarioValidator.Validate(scenario);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "recording.sampleInterval");

        scenario.Recording = new ScenarioRecordingOptions
        {
            SampleInterval = 30,
            SnapshotInterval = 0,
        };

        report = ScenarioValidator.Validate(scenario);

        Assert.Contains(report.Diagnostics, d => d.FieldPath == "recording.snapshotInterval");
    }

    [Fact]
    public void GeneratorBounds_AreReported()
    {
        var scenario = BuildValidScenario();
        scenario.PopulationGenerators.Add(new FormationPopulationGenerator(
            PlayerIndex: 0,
            DefinitionName: "AmericaInfantryRanger",
            Shape: FormationShape.Line,
            Quantity: 0,
            OriginX: 0.5f,
            OriginY: 0.5f,
            Spacing: 0.01f));
        scenario.PopulationGenerators.Add(new GridPopulationGenerator(1, "AmericaTankCrusader", 0, 4, 0.5f, 0.5f, 0.01f, 0.01f));
        scenario.PopulationGenerators.Add(new ScatteredPopulationGenerator(1, "AmericaTankCrusader", 10, 0.5f, 0.5f, Radius: 0f));
        scenario.CommandGenerators.Add(new CommandTimelineGenerator(
            PlayerIndex: 0,
            Template: [],
            RepeatCount: 0,
            StartFrame: 100,
            IntervalFrames: 50));

        var report = ScenarioValidator.Validate(scenario);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "populationGenerators[0].quantity");
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "populationGenerators[1].rows");
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "populationGenerators[2].radius");
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "commandGenerators[0].repeatCount");
    }

    internal static ReplayScenario BuildValidScenario()
    {
        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            DomainId = "rts",
            Seed = 42,
        };

        scenario.Players.Add(BuildPlayer("USA", factionIndex: 2));
        scenario.Players.Add(BuildPlayer("GLA", factionIndex: 4));
        scenario.Players[0].Actions.Add(BuildAction("open-0"));
        scenario.Players[1].Actions.Add(BuildAction("open-1"));
        scenario.Placements.Add(new ScenarioPlacement
        {
            DefinitionName = "AmericaTankCrusader",
            PlayerIndex = 0,
            Position = new NormalizedPosition(0.4f, 0.4f),
            Quantity = 1,
        });
        scenario.Parameters["difficulty"] = "hard";
        scenario.StopConditions.Add(new FrameCapStopCondition(600));
        scenario.RecordedMetricSeries.Add("unitCount");
        return scenario;
    }

    internal static ActionEntry BuildAction(string id) => new(
        label: $"action-{id}",
        command: CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
        position: null,
        timing: new FixedTiming(100),
        id: id);

    private static PlayerSlotConfig BuildPlayer(string name, int factionIndex) => new()
    {
        Name = name,
        FactionIndex = factionIndex,
        StartPosition = name == "USA" ? 1 : 2,
    };
}
