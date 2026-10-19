using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Card WI-A5-03, engine-free: the declarative scenario artifact round-trips through the
/// loader and structural validation rejects broken artifacts before any host construction.
/// </summary>
public class ReplayScenarioLoaderTests
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string WriteScenario(ReplayScenario scenario)
    {
        var path = Path.Combine(Path.GetTempPath(), "a5-03-scenario-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(scenario, WriteOptions));
        return path;
    }

    private static ReplayScenario CompleteScenario() => new()
    {
        MapPath = "maps/alpine assault/alpine assault.map",
        DomainId = RtsSimulationDomainModel.DefaultDomainId,
        Seed = 4242u,
        Players =
        [
            new PlayerSlotConfig { Name = "USA", FactionIndex = 2, StartPosition = 1 },
            new PlayerSlotConfig { Name = "GLA", FactionIndex = 4, StartPosition = 2 },
        ],
        Placements =
        [
            new ScenarioPlacement
            {
                DefinitionName = "AmericaInfantryRanger",
                PlayerIndex = 0,
                Position = new NormalizedPosition(0.25f, 0.75f),
                Quantity = 3,
            },
            new ScenarioPlacement
            {
                DefinitionName = "AmericaTankPaladin",
                PlayerIndex = 1,
                Position = new LandmarkRelativePosition(
                    LandmarkType.EnemyBase, 2f, new FixedAngle(90)),
                Quantity = 1,
            },
        ],
        Parameters = new() { ["difficulty"] = "hard", ["unitCount"] = "1000" },
        StopConditions = [new FrameCapStopCondition(600)],
        RecordedMetricSeries = ["player[0].money"],
        Recording = new ScenarioRecordingOptions
        {
            Mode = RecordingMode.Full,
            SampleInterval = 15,
            SnapshotInterval = 300,
        },
    };

    [Fact]
    public void Scenario_WithAllNewFields_RoundTrips()
    {
        var scenario = CompleteScenario();
        var path = WriteScenario(scenario);

        var loaded = ReplayScenarioLoader.Load(path);

        Assert.Equal(ReplayScenario.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(RtsSimulationDomainModel.DefaultDomainId, loaded.DomainId);
        Assert.Equal(4242u, loaded.Seed);
        Assert.Equal(2, loaded.Placements.Count);
        Assert.Equal("AmericaInfantryRanger", loaded.Placements[0].DefinitionName);
        Assert.Equal(3, loaded.Placements[0].Quantity);
        Assert.IsType<NormalizedPosition>(loaded.Placements[0].Position);
        Assert.IsType<LandmarkRelativePosition>(loaded.Placements[1].Position);
        Assert.Equal("hard", loaded.Parameters["difficulty"]);
        var frameCap = Assert.Single(loaded.StopConditions.OfType<FrameCapStopCondition>());
        Assert.Equal(600u, frameCap.MaxFrames);
        Assert.Equal(RecordingMode.Full, loaded.Recording!.Mode);
        Assert.Equal(15u, loaded.Recording.SampleInterval);
        Assert.Equal(300u, loaded.Recording.SnapshotInterval);
    }

    [Fact]
    public void Load_RejectsUnknownSchemaVersion()
    {
        var scenario = CompleteScenario();
        scenario.SchemaVersion = ReplayScenario.CurrentSchemaVersion + 1;
        var path = WriteScenario(scenario);

        var error = Assert.Throws<InvalidOperationException>(() => ReplayScenarioLoader.Load(path));
        Assert.Contains("schema version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsEmptyMapPath()
    {
        var scenario = CompleteScenario();
        scenario.MapPath = "";
        var path = WriteScenario(scenario);

        Assert.Throws<InvalidOperationException>(() => ReplayScenarioLoader.Load(path));
    }

    [Fact]
    public void Load_RejectsScenarioWithoutPlayers()
    {
        var scenario = CompleteScenario();
        scenario.Players.Clear();
        var path = WriteScenario(scenario);

        Assert.Throws<InvalidOperationException>(() => ReplayScenarioLoader.Load(path));
    }

    [Fact]
    public void Load_RejectsPlacementForUnknownPlayerSlot()
    {
        var scenario = CompleteScenario();
        scenario.Placements.Add(new ScenarioPlacement
        {
            DefinitionName = "AmericaInfantryRanger",
            PlayerIndex = scenario.Players.Count,
            Position = new NormalizedPosition(0.5f, 0.5f),
        });
        var path = WriteScenario(scenario);

        Assert.Throws<InvalidOperationException>(() => ReplayScenarioLoader.Load(path));
    }

    [Fact]
    public void Load_RejectsNonPositiveQuantity()
    {
        var scenario = CompleteScenario();
        scenario.Placements.Add(new ScenarioPlacement
        {
            DefinitionName = "AmericaInfantryRanger",
            PlayerIndex = 0,
            Position = new NormalizedPosition(0.5f, 0.5f),
            Quantity = 0,
        });
        var path = WriteScenario(scenario);

        Assert.Throws<InvalidOperationException>(() => ReplayScenarioLoader.Load(path));
    }

    [Fact]
    public void Load_RejectsZeroSampleInterval()
    {
        var scenario = CompleteScenario();
        scenario.Recording = new ScenarioRecordingOptions { SampleInterval = 0 };
        var path = WriteScenario(scenario);

        Assert.Throws<InvalidOperationException>(() => ReplayScenarioLoader.Load(path));
    }
}
