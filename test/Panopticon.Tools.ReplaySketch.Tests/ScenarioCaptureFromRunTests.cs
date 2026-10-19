using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon;
using Panopticon.Logic.Orders;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-11: a captured scenario is a documented lossy projection of one recorded frame. These
/// tests run engine-free against a real written bundle (shared TimelineFixture).
/// </summary>
public sealed class ScenarioCaptureFromRunTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-capture-{Guid.NewGuid():N}");

    [Fact]
    public void Capture_AtFrame_ProjectsStateFields()
    {
        var bundleDir = TimelineFixture.WriteBundle(Path.Combine(_root, "bundle"), lastFrame: 30, snapshotInterval: 10);
        using var reader = OpenReader(bundleDir);
        var result = ScenarioCaptureService.Capture(RunBundle.Load(bundleDir), reader, 17, NewOptions());

        var d = result.Diagnostics;
        Assert.Equal(17u, d.ProjectedFrame);
        Assert.Equal(17u, d.RequestedFrame);
        Assert.Equal(3, d.ProjectedPlacements);
        Assert.Equal(0, d.UnmappedOwners);
        Assert.Equal(2, d.DroppedCommands);
        Assert.Equal(new uint[] { 983, 2017 }, d.MoneyByPlayer.ToArray());

        var scenario = result.Scenario;
        Assert.Equal(3, scenario.Placements.Count);
        Assert.All(scenario.Players, p => Assert.Empty(p.Actions));
        Assert.Equal(983u, scenario.Players[0].StartingMoney);
        Assert.Equal(2017u, scenario.Players[1].StartingMoney);

        // Fixture positions at frame 17: (17,1), (34,2), (51,3) against extents (0,0)-(100,200).
        var first = scenario.Placements[0];
        Assert.Equal("Thing1", first.DefinitionName);
        Assert.Equal(0, first.PlayerIndex);
        var position = Assert.IsType<NormalizedPosition>(first.Position);
        Assert.Equal(0.17f, position.NormX, 3);
        Assert.Equal(0.005f, position.NormY, 3);
        Assert.Equal(83f, first.Health);
        Assert.Equal(100f, first.MaxHealth);

        var second = scenario.Placements[1];
        Assert.Equal("Thing2", second.DefinitionName);
        Assert.Equal(1, second.PlayerIndex);
        Assert.Equal(73f, second.Health);
    }

    [Fact]
    public void Capture_BeyondPrefix_ClampsToRecordedFrame()
    {
        var bundleDir = TimelineFixture.WriteBundle(Path.Combine(_root, "clamp"), lastFrame: 30, snapshotInterval: 10);
        using var reader = OpenReader(bundleDir);
        var result = ScenarioCaptureService.Capture(RunBundle.Load(bundleDir), reader, 999, NewOptions());

        Assert.Equal(30u, result.Diagnostics.ProjectedFrame);
        Assert.Equal(999u, result.Diagnostics.RequestedFrame);
        Assert.Equal(4, result.Scenario.Placements.Count);
        Assert.Contains(result.Scenario.Placements, p => p.DefinitionName == "LateJoiner");
    }

    [Fact]
    public void Capture_SerializesValidatesAndReloads()
    {
        var bundleDir = TimelineFixture.WriteBundle(Path.Combine(_root, "roundtrip"), lastFrame: 30, snapshotInterval: 10);
        using var reader = OpenReader(bundleDir);
        var result = ScenarioCaptureService.Capture(RunBundle.Load(bundleDir), reader, 17, NewOptions());

        var path = Path.Combine(_root, "captured-scenario.json");
        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(path, JsonSerializer.Serialize(result.Scenario, options));

        var reloaded = ReplayScenarioLoader.Load(path);
        Assert.Equal(3, reloaded.Placements.Count);
        Assert.Equal("maps/alpine assault/alpine assault.map", reloaded.MapPath);
        Assert.Equal(983u, reloaded.Players[0].StartingMoney);
        Assert.All(reloaded.Players, p => Assert.Empty(p.Actions));

        var placement = reloaded.Placements[0];
        Assert.Equal("Thing1", placement.DefinitionName);
        Assert.Equal(83f, placement.Health);
        Assert.Equal(100f, placement.MaxHealth);
        var position = Assert.IsType<NormalizedPosition>(placement.Position);
        Assert.Equal(0.17f, position.NormX, 3);
    }

    [Fact]
    public void Capture_ReportsLossyProjection()
    {
        var bundleDir = TimelineFixture.WriteBundle(Path.Combine(_root, "lossy"), lastFrame: 30, snapshotInterval: 10);
        using var reader = OpenReader(bundleDir);
        var result = ScenarioCaptureService.Capture(RunBundle.Load(bundleDir), reader, 17, NewOptions());

        var omitted = result.Diagnostics.OmittedTransientFields;
        Assert.Contains("orientation (facing)", omitted);
        Assert.Contains("upgrades", omitted);
        Assert.Contains("veterancy", omitted);
        Assert.Contains("production queues", omitted);
        Assert.Contains("team membership", omitted);
        Assert.Contains("per-module state", omitted);
        Assert.NotEmpty(result.Diagnostics.SourceRunId);
    }

    private WorldStateArtifactReader OpenReader(string bundleDir)
    {
        return new WorldStateArtifactReader(
            Path.Combine(bundleDir, RunBundle.StateFileName),
            SageGame.CncGenerals);
    }

    private ScenarioCaptureOptions NewOptions()
    {
        return new ScenarioCaptureOptions(
            "maps/alpine assault/alpine assault.map",
            [SlotWithOneCommand(0), SlotWithOneCommand(1)],
            new System.Numerics.Vector3(0, 0, 0),
            new System.Numerics.Vector3(100, 200, 50));
    }

    private static PlayerSlotConfig SlotWithOneCommand(int index)
    {
        return new PlayerSlotConfig
        {
            Name = index == 0 ? "USA" : "GLA",
            FactionIndex = index == 0 ? 2 : 4,
            Actions =
            [
                new ActionEntry(
                    label: "probe",
                    command: CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f))),
                    position: null,
                    timing: new FixedTiming(100),
                    id: $"cmd-{index}"),
            ],
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
