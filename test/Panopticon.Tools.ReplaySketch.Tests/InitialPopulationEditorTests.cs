using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Card WI-A5-14: the initial-population edit controller, headless. Covers all five operations,
/// save/reload fidelity, invalid-edit diagnostics without mutation, and domain-agnostic editing
/// for graph-domain scenarios.
/// </summary>
public sealed class InitialPopulationEditorTests
{
    private static ReplayScenario NewScenario(string domainId = "rts")
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        scenario.DomainId = domainId;
        scenario.Placements = [];
        return scenario;
    }

    [Fact]
    public void AllFiveOperations_EditPlacementsHeadlessly()
    {
        var scenario = NewScenario();
        var editor = new InitialPopulationEditor(scenario);

        Assert.True(editor.Add("AmericaTankCrusader", 0, new NormalizedPosition(0.25f, 0.25f), quantity: 2, health: 80, maxHealth: 100));
        Assert.True(editor.Add("GLATankScorpion", 1, new NormalizedPosition(0.75f, 0.75f)));
        Assert.Equal(2, editor.Placements.Count);

        editor.Select(0);
        Assert.Same(editor.Placements[0], editor.Selected);

        Assert.True(editor.MoveSelected(new NormalizedPosition(0.4f, 0.6f)));
        var moved = Assert.IsType<NormalizedPosition>(editor.Selected!.Position);
        Assert.Equal(0.4f, moved.NormX);
        Assert.Equal(0.6f, moved.NormY);

        Assert.True(editor.ChangeOwnerSelected(1));
        Assert.Equal(1, editor.Selected.PlayerIndex);

        Assert.True(editor.DeleteSelected());
        Assert.Single(editor.Placements);
        Assert.Equal("GLATankScorpion", editor.Placements[0].DefinitionName);
        Assert.Null(editor.Selected);
    }

    [Fact]
    public void SaveReload_PreservesEditsExactly()
    {
        var scenario = NewScenario();
        var editor = new InitialPopulationEditor(scenario);
        Assert.True(editor.Add("AmericaTankCrusader", 0, new NormalizedPosition(0.25f, 0.25f), quantity: 2, health: 80, maxHealth: 100));
        Assert.True(editor.Add("GLATankScorpion", 1, new NormalizedPosition(0.75f, 0.75f)));

        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        var path = Path.Combine(Path.GetTempPath(), $"rs-population-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(scenario, options));
        try
        {
            var reloaded = ReplayScenarioLoader.Load(path);
            Assert.Equal(2, reloaded.Placements.Count);
            Assert.Equal("AmericaTankCrusader", reloaded.Placements[0].DefinitionName);
            Assert.Equal(2, reloaded.Placements[0].Quantity);
            Assert.Equal(80, reloaded.Placements[0].Health);
            Assert.Equal(100, reloaded.Placements[0].MaxHealth);
            var position = Assert.IsType<NormalizedPosition>(reloaded.Placements[0].Position);
            Assert.Equal(0.25f, position.NormX);
            Assert.Equal(0.25f, position.NormY);
            Assert.Equal(1, reloaded.Placements[1].PlayerIndex);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidEdits_ReportDiagnosticsWithoutMutation()
    {
        var scenario = NewScenario();
        var editor = new InitialPopulationEditor(scenario);
        Assert.True(editor.Add("AmericaTankCrusader", 0, new NormalizedPosition(0.25f, 0.25f)));
        var before = editor.Placements[0];

        Assert.False(editor.Add("", 0, new NormalizedPosition(0.5f, 0.5f)));
        Assert.NotEmpty(editor.LastDiagnostics);
        Assert.Contains(editor.LastDiagnostics, d => d.Contains("definitionName", StringComparison.Ordinal));
        Assert.Single(editor.Placements);

        Assert.False(editor.Add("AmericaTankCrusader", 9, new NormalizedPosition(0.5f, 0.5f)));
        Assert.Contains(editor.LastDiagnostics, d => d.Contains("playerIndex", StringComparison.Ordinal));
        Assert.Single(editor.Placements);

        editor.Select(0);
        var ownerCount = editor.Placements.Count;
        Assert.False(editor.ChangeOwnerSelected(-1));
        Assert.Contains(editor.LastDiagnostics, d => d.Contains("playerIndex", StringComparison.Ordinal));
        Assert.Equal(ownerCount, editor.Placements.Count);
        Assert.Equal(0, editor.Placements[0].PlayerIndex);

        Assert.Same(before, editor.Placements[0]);
    }

    [Fact]
    public void GraphDomainScenario_EditsThroughTheSameController()
    {
        var scenario = NewScenario("transport-graph");
        var editor = new InitialPopulationEditor(scenario);

        Assert.True(editor.Add("ConvoyTruck", 0, new NormalizedPosition(0.1f, 0.1f)));
        editor.Select(0);
        Assert.True(editor.MoveSelected(new NormalizedPosition(0.9f, 0.9f)));
        Assert.True(editor.ChangeOwnerSelected(1));

        Assert.Single(editor.Placements);
        Assert.Equal(1, editor.Placements[0].PlayerIndex);
        var position = Assert.IsType<NormalizedPosition>(editor.Placements[0].Position);
        Assert.Equal(0.9f, position.NormX);
    }
}
