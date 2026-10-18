#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// WI-A5-33: authored graph topology for non-spatial domains. Nodes carry stable integer ids the
/// editor assigns monotonically and never reuses; edges reference nodes by id. Serialized with
/// the scenario when <see cref="ReplayScenario.DomainId"/> selects a graph domain; absent means
/// the domain default topology applies.
/// </summary>
public sealed class ScenarioGraph
{
    public List<ScenarioGraphNode> Nodes { get; set; } = [];

    public List<ScenarioGraphEdge> Edges { get; set; } = [];

    /// <summary>Next node id to assign; only moves forward, so deleted ids are never reused.</summary>
    public int NextNodeId { get; set; } = 1;

    public ScenarioGraphNode? FindNode(int id) => Nodes.FirstOrDefault(n => n.Id == id);

    public ScenarioGraphEdge? FindEdge(int fromNodeId, int toNodeId) =>
        Edges.FirstOrDefault(e => e.FromNodeId == fromNodeId && e.ToNodeId == toNodeId);
}

/// <summary>Stable identity is the <see cref="Id"/>; position is a domain-neutral 2-D layout.</summary>
public sealed class ScenarioGraphNode
{
    public int Id { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public int OwnerSide { get; set; }
}

/// <summary>Directed edge between two <see cref="ScenarioGraphNode"/> ids.</summary>
public sealed class ScenarioGraphEdge
{
    public int FromNodeId { get; set; }

    public int ToNodeId { get; set; }
}
