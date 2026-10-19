using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-23: procedural scenario generators. Expansion is seed-deterministic, scales to 10k
/// entities and commands without hand-authored entries, emits P0 command-model entries, and
/// fails closed on invalid bounds before producing partial output.
/// </summary>
public sealed class ProceduralScenarioTests
{
    private static readonly JsonSerializerOptions RoundTripOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void SameSeed_ProducesIdenticalExpandedScenarios()
    {
        var first = ScenarioGeneratorService.Expand(BuildGeneratorScenario(seed: 77));
        var second = ScenarioGeneratorService.Expand(BuildGeneratorScenario(seed: 77));

        var firstJson = JsonSerializer.Serialize(first, RoundTripOptions);
        var secondJson = JsonSerializer.Serialize(second, RoundTripOptions);
        Assert.Equal(firstJson, secondJson);
    }

    [Fact]
    public void DifferentSeed_ScattersDifferently()
    {
        var first = ScenarioGeneratorService.Expand(BuildGeneratorScenario(seed: 77));
        var second = ScenarioGeneratorService.Expand(BuildGeneratorScenario(seed: 78));

        var firstScatter = first.Placements.Where(p => p.DefinitionName == "ScatterProbe").ToList();
        var secondScatter = second.Placements.Where(p => p.DefinitionName == "ScatterProbe").ToList();
        Assert.NotEmpty(firstScatter);
        Assert.NotEmpty(secondScatter);
        Assert.NotEqual(
            JsonSerializer.Serialize(firstScatter, RoundTripOptions),
            JsonSerializer.Serialize(secondScatter, RoundTripOptions));
    }

    [Fact]
    public void TenThousand_PlacementsAndCommands_ExpandWithoutHandEntry()
    {
        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            DomainId = "rts",
            Seed = 5,
        };
        scenario.Players.Add(new PlayerSlotConfig { Name = "USA", FactionIndex = 2, StartPosition = 1 });
        scenario.Players.Add(new PlayerSlotConfig { Name = "GLA", FactionIndex = 4, StartPosition = 2 });
        scenario.PopulationGenerators.Add(new GridPopulationGenerator(0, "AmericaInfantryRanger", 100, 100, 0.5f, 0.5f, 0.002f, 0.002f));
        scenario.CommandGenerators.Add(new CommandTimelineGenerator(
            PlayerIndex: 0,
            Template: Enumerable.Range(0, 10)
                .Select(i => ScenarioValidatorTests.BuildAction($"template-{i}"))
                .ToList(),
            RepeatCount: 1000,
            StartFrame: 100,
            IntervalFrames: 1500));

        var stopwatch = Stopwatch.StartNew();
        var expanded = ScenarioGeneratorService.Expand(scenario);
        stopwatch.Stop();

        Assert.Equal(10_000, expanded.Placements.Count);
        Assert.Equal(10_000, expanded.Players[0].Actions.Count);
        Assert.Empty(expanded.PopulationGenerators);
        Assert.Empty(expanded.CommandGenerators);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Expansion took {stopwatch.Elapsed}");
    }

    [Fact]
    public void ExpandedCommands_AreP0CommandSpecEntries()
    {
        var expanded = ScenarioGeneratorService.Expand(BuildGeneratorScenario(seed: 9));

        var generated = expanded.Players[0].Actions.Where(a => a.Id.EndsWith("-g1t0", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(generated);
        Assert.All(generated, action => Assert.Equal(OrderType.MoveTo, action.Command.OrderType));
        Assert.All(generated, action => Assert.IsType<FixedTiming>(action.Timing));
    }

    [Fact]
    public void InvalidBounds_ThrowBeforeOutput()
    {
        var zeroRows = BuildGeneratorScenario(seed: 1);
        zeroRows.PopulationGenerators[0] = new GridPopulationGenerator(0, "AmericaInfantryRanger", 0, 4, 0.5f, 0.5f, 0.01f, 0.01f);
        Assert.Throws<InvalidOperationException>(() => ScenarioGeneratorService.Expand(zeroRows));

        var nonFixedTemplate = BuildGeneratorScenario(seed: 1);
        nonFixedTemplate.CommandGenerators[0] = new CommandTimelineGenerator(
            PlayerIndex: 0,
            Template: [new ActionEntry(
                "label",
                CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                position: null,
                timing: new RandomTiming(10, 20))],
            RepeatCount: 2,
            StartFrame: 100,
            IntervalFrames: 50);
        Assert.Throws<InvalidOperationException>(() => ScenarioGeneratorService.Expand(nonFixedTemplate));

        var overlapping = BuildGeneratorScenario(seed: 1);
        overlapping.CommandGenerators[0] = new CommandTimelineGenerator(
            PlayerIndex: 0,
            Template: [ScenarioValidatorTests.BuildAction("template-0"), ScenarioValidatorTests.BuildAction("template-1")],
            RepeatCount: 3,
            StartFrame: 100,
            IntervalFrames: 10);
        Assert.Throws<InvalidOperationException>(() => ScenarioGeneratorService.Expand(overlapping));
    }

    [Fact]
    public void Generators_SerializeAndReloadIntact()
    {
        var scenario = BuildGeneratorScenario(seed: 31);

        var json = JsonSerializer.Serialize(scenario, RoundTripOptions);
        var reloaded = JsonSerializer.Deserialize<ReplayScenario>(json, RoundTripOptions);

        Assert.NotNull(reloaded);
        var grid = Assert.IsType<GridPopulationGenerator>(Assert.Single(reloaded.PopulationGenerators, g => g is GridPopulationGenerator));
        Assert.Equal(4u, grid.Rows);
        Assert.Equal(4u, grid.Columns);
        var generator = Assert.Single(reloaded.CommandGenerators);
        Assert.Equal(2u, generator.RepeatCount);
        Assert.Single(generator.Template);
    }

    [Fact]
    public void ExpandedScenario_PassesStructuralValidation()
    {
        var expanded = ScenarioGeneratorService.Expand(BuildGeneratorScenario(seed: 13));

        var report = ScenarioValidator.Validate(expanded);

        Assert.True(report.IsValid);
    }

    private static ReplayScenario BuildGeneratorScenario(uint seed)
    {
        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            DomainId = "rts",
            Seed = seed,
        };
        scenario.Players.Add(new PlayerSlotConfig { Name = "USA", FactionIndex = 2, StartPosition = 1 });
        scenario.Players.Add(new PlayerSlotConfig { Name = "GLA", FactionIndex = 4, StartPosition = 2 });
        scenario.Players[0].Actions.Add(ScenarioValidatorTests.BuildAction("existing-0"));
        scenario.PopulationGenerators.Add(new GridPopulationGenerator(0, "AmericaInfantryRanger", 4, 4, 0.5f, 0.3f, 0.01f, 0.01f));
        scenario.PopulationGenerators.Add(new ScatteredPopulationGenerator(1, "ScatterProbe", 8, 0.5f, 0.6f, 0.05f));
        scenario.CommandGenerators.Add(new CommandTimelineGenerator(
            PlayerIndex: 0,
            Template: [ScenarioValidatorTests.BuildAction("template-0")],
            RepeatCount: 2,
            StartFrame: 100,
            IntervalFrames: 500));
        return scenario;
    }
}
