#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Card WI-A5-33: graph-topology edit controller over a scenario's <see cref="ScenarioGraph"/>.
/// Every mutation builds a candidate clone, validates stable ids and edge connectivity, and only
/// then replaces the scenario's graph; invalid edits report field-path diagnostics and change
/// nothing. Population edits route through the shared WI-A5-14 controller over the same scenario.
/// Spatial map and terrain topology stay in WorldBuilder (WI-A5-30); nothing here touches them.
/// </summary>
public sealed class GraphTopologyEditor
{
    private readonly ReplayScenario _scenario;
    private readonly InitialPopulationEditor _population;

    public GraphTopologyEditor(ReplayScenario scenario)
    {
        _scenario = scenario;
        _population = new InitialPopulationEditor(scenario);
    }

    public IReadOnlyList<string> LastDiagnostics { get; private set; } = [];

    public ScenarioGraph? Graph => _scenario.Graph;

    /// <summary>Shared WI-A5-14 controller over the same scenario.</summary>
    public InitialPopulationEditor Population => _population;

    public int SelectedNodeId { get; private set; }

    public bool Select(int nodeId)
    {
        if (_scenario.Graph?.FindNode(nodeId) is null)
        {
            SelectedNodeId = 0;
            return false;
        }

        SelectedNodeId = nodeId;
        return true;
    }

    /// <summary>Creates the graph on first use, mirroring how the population controller works
    /// against an otherwise default scenario.</summary>
    public bool AddNode(float x, float y, int ownerSide)
    {
        var graph = CloneGraphOrNew();
        var node = new ScenarioGraphNode { Id = graph.NextNodeId, X = x, Y = y, OwnerSide = ownerSide };
        graph.Nodes.Add(node);
        graph.NextNodeId++;
        return Commit(graph, () => SelectedNodeId = node.Id);
    }

    public bool MoveNode(int nodeId, float x, float y)
    {
        if (_scenario.Graph?.FindNode(nodeId) is null)
        {
            LastDiagnostics = [$"graph.nodes: no node with id {nodeId}"];
            return false;
        }

        var graph = CloneGraph();
        var moved = graph.FindNode(nodeId)!;
        moved.X = x;
        moved.Y = y;
        return Commit(graph, () => { });
    }

    public bool DeleteNode(int nodeId)
    {
        if (_scenario.Graph?.FindNode(nodeId) is null)
        {
            LastDiagnostics = [$"graph.nodes: no node with id {nodeId}"];
            return false;
        }

        var graph = CloneGraph();
        graph.Nodes.RemoveAll(n => n.Id == nodeId);
        graph.Edges.RemoveAll(e => e.FromNodeId == nodeId || e.ToNodeId == nodeId);
        return Commit(graph, () =>
        {
            if (SelectedNodeId == nodeId)
            {
                SelectedNodeId = 0;
            }
        });
    }

    public bool AddEdge(int fromNodeId, int toNodeId)
    {
        var graph = CloneGraphOrNew();
        graph.Edges.Add(new ScenarioGraphEdge { FromNodeId = fromNodeId, ToNodeId = toNodeId });
        return Commit(graph, () => { });
    }

    public bool DeleteEdge(int fromNodeId, int toNodeId)
    {
        if (_scenario.Graph?.FindEdge(fromNodeId, toNodeId) is null)
        {
            LastDiagnostics = [$"graph.edges: no edge {fromNodeId}->{toNodeId}"];
            return false;
        }

        var graph = CloneGraph();
        graph.Edges.RemoveAll(e => e.FromNodeId == fromNodeId && e.ToNodeId == toNodeId);
        return Commit(graph, () => { });
    }

    private ScenarioGraph CloneGraphOrNew()
    {
        if (_scenario.Graph is null)
        {
            return new ScenarioGraph();
        }

        var clone = CloneGraph();
        clone.NextNodeId = _scenario.Graph.NextNodeId;
        return clone;
    }

    private ScenarioGraph CloneGraph() => new()
    {
        Nodes = _scenario.Graph?.Nodes.Select(n => new ScenarioGraphNode { Id = n.Id, X = n.X, Y = n.Y, OwnerSide = n.OwnerSide }).ToList() ?? [],
        Edges = _scenario.Graph?.Edges.Select(e => new ScenarioGraphEdge { FromNodeId = e.FromNodeId, ToNodeId = e.ToNodeId }).ToList() ?? [],
        NextNodeId = _scenario.Graph?.NextNodeId ?? 1,
    };

    private bool Commit(ScenarioGraph candidate, System.Action onAccepted)
    {
        var diagnostics = ValidateGraph(candidate);
        LastDiagnostics = diagnostics;
        if (diagnostics.Count > 0)
        {
            return false;
        }

        _scenario.Graph = candidate;
        onAccepted();
        return true;
    }

    /// <summary>Connectivity and stable-identifier validation shared by the editor and the
    /// scenario validator; runs before any serialization.</summary>
    public static IReadOnlyList<string> ValidateGraph(ScenarioGraph graph)
    {
        var diagnostics = new List<string>();
        var seen = new HashSet<int>();
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            if (!seen.Add(node.Id))
            {
                diagnostics.Add($"graph.nodes[{i}].id: duplicate node id {node.Id}");
            }

            if (graph.NextNodeId <= node.Id)
            {
                diagnostics.Add($"graph.nodes[{i}].id: id {node.Id} is not below nextNodeId {graph.NextNodeId}");
            }
        }

        for (var i = 0; i < graph.Edges.Count; i++)
        {
            var edge = graph.Edges[i];
            if (edge.FromNodeId == edge.ToNodeId)
            {
                diagnostics.Add($"graph.edges[{i}]: self-edge on node {edge.FromNodeId}");
            }

            if (graph.Nodes.All(n => n.Id != edge.FromNodeId))
            {
                diagnostics.Add($"graph.edges[{i}].fromNodeId: references missing node {edge.FromNodeId}");
            }

            if (graph.Nodes.All(n => n.Id != edge.ToNodeId))
            {
                diagnostics.Add($"graph.edges[{i}].toNodeId: references missing node {edge.ToNodeId}");
            }
        }

        for (var i = 0; i < graph.Edges.Count; i++)
        {
            var edge = graph.Edges[i];
            for (var j = i + 1; j < graph.Edges.Count; j++)
            {
                var other = graph.Edges[j];
                if (edge.FromNodeId == other.FromNodeId && edge.ToNodeId == other.ToNodeId)
                {
                    diagnostics.Add($"graph.edges[{j}]: duplicate edge {edge.FromNodeId}->{edge.ToNodeId}");
                }
            }
        }

        return diagnostics;
    }
}
