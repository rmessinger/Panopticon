#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class TopologyEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-topology-{Guid.NewGuid():N}");

    private static ReplayScenario NewGraphScenario()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        scenario.DomainId = "transport-graph";
        return scenario;
    }

    [Fact]
    public void AllOperations_EditNodesAndEdges_WithCascadeAndSelection()
    {
        var scenario = NewGraphScenario();
        var editor = new GraphTopologyEditor(scenario);

        Assert.True(editor.AddNode(10f, 20f, ownerSide: 0));
        Assert.True(editor.AddNode(50f, 60f, ownerSide: 1));
        Assert.True(editor.AddNode(90f, 30f, ownerSide: 0));
        Assert.Equal([1, 2, 3], scenario.Graph!.Nodes.Select(n => n.Id));
        Assert.Equal(3, editor.SelectedNodeId);

        Assert.True(editor.MoveNode(2, 55f, 65f));
        var moved = scenario.Graph.FindNode(2)!;
        Assert.Equal(55f, moved.X);
        Assert.Equal(65f, moved.Y);

        Assert.True(editor.AddEdge(1, 2));
        Assert.True(editor.AddEdge(2, 3));
        Assert.Equal(2, scenario.Graph.Edges.Count);

        Assert.True(editor.DeleteNode(1));
        Assert.Null(scenario.Graph.FindNode(1));
        // Cascade: the edge touching the deleted node is gone, the other survives.
        Assert.Null(scenario.Graph.FindEdge(1, 2));
        Assert.NotNull(scenario.Graph.FindEdge(2, 3));
    }

    [Fact]
    public void StableIds_AreNeverReused_AfterDeletion()
    {
        var scenario = NewGraphScenario();
        var editor = new GraphTopologyEditor(scenario);

        Assert.True(editor.AddNode(0f, 0f, 0));
        Assert.True(editor.AddNode(10f, 0f, 0));
        Assert.True(editor.DeleteNode(2));
        Assert.True(editor.AddNode(20f, 0f, 0));

        var ids = scenario.Graph!.Nodes.Select(n => n.Id).ToList();
        Assert.Equal([1, 3], ids);
    }

    [Fact]
    public void InvalidEdits_ReportFieldPaths_AndDoNotMutate()
    {
        var scenario = NewGraphScenario();
        var editor = new GraphTopologyEditor(scenario);
        editor.AddNode(0f, 0f, 0);
        editor.AddNode(10f, 0f, 0);

        // Duplicate edge is rejected.
        Assert.True(editor.AddEdge(1, 2));
        Assert.False(editor.AddEdge(1, 2));
        Assert.Contains(editor.LastDiagnostics, d => d.StartsWith("graph.edges[1]"));

        // Edges to missing nodes are rejected with connectivity diagnostics.
        Assert.False(editor.AddEdge(1, 99));
        Assert.Contains(editor.LastDiagnostics, d => d.Contains("references missing node 99"));
        Assert.Single(scenario.Graph!.Edges);

        // Self-edges are rejected.
        Assert.False(editor.AddEdge(1, 1));
        Assert.Contains(editor.LastDiagnostics, d => d.Contains("self-edge"));
    }

    [Fact]
    public void PopulationEdits_ReuseTheSharedController()
    {
        var scenario = NewGraphScenario();
        var editor = new GraphTopologyEditor(scenario);

        Assert.True(editor.Population.Add("TestTruck", 0, new NormalizedPosition(0.5f, 0.5f)));
        Assert.True(editor.AddNode(10f, 10f, 0));

        Assert.Single(scenario.Placements);
        Assert.Single(scenario.Graph!.Nodes);

        // Population rejection leaves graph edits intact and vice versa.
        Assert.False(editor.Population.Add("", 0, new NormalizedPosition(0.5f, 0.5f)));
        Assert.Single(scenario.Graph.Nodes);
    }

    [Fact]
    public void GraphRoundTrips_ThroughScenarioSerialization_AndValidates()
    {
        var scenario = NewGraphScenario();
        var editor = new GraphTopologyEditor(scenario);
        editor.AddNode(0f, 0f, 0);
        editor.AddNode(100f, 0f, 1);
        editor.AddEdge(1, 2);

        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "graph-scenario.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(scenario, options));

        var loaded = ReplayScenarioLoader.Load(path);
        Assert.Equal("transport-graph", loaded.DomainId);
        Assert.NotNull(loaded.Graph);
        Assert.Equal([1, 2], loaded.Graph!.Nodes.Select(n => n.Id).ToList());
        Assert.Equal((1, 2), (loaded.Graph.Edges[0].FromNodeId, loaded.Graph.Edges[0].ToNodeId));

        var report = ScenarioValidator.Validate(loaded);
        Assert.True(report.IsValid, string.Join("; ", report.Diagnostics.Select(d => $"{d.FieldPath}: {d.Message}")));
    }

    [Fact]
    public void ScenarioValidation_RejectsBrokenGraph_BeforeSave()
    {
        var scenario = NewGraphScenario();
        scenario.Graph = new ScenarioGraph
        {
            Nodes = [new ScenarioGraphNode { Id = 1, X = 0, Y = 0 }],
            Edges = [new ScenarioGraphEdge { FromNodeId = 1, ToNodeId = 42 }],
        };

        var report = ScenarioValidator.Validate(scenario);
        Assert.Contains(report.Diagnostics, d => d.FieldPath == "graph.edges[0].toNodeId" && d.Message.Contains("42"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
