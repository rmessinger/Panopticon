#nullable enable

using System.Linq;
using Panopticon.Simulation;
using Panopticon.Simulation.Transport;
using Panopticon.Tests;
using Xunit;

namespace Panopticon.Tests.Simulation;

/// <summary>
/// WI-A5-33: the transport-graph domain accepts authored topologies through the public factory
/// (the scenario-authoring path builds them from <c>ScenarioGraph</c> in HarnessRunner).
/// </summary>
[Collection("SerialGameTests")]
public sealed class TopologyDomainModelTests : MockedGameTest
{
    // Game boots must not run in parallel with other game-booting classes (shared MapCache.ini
    // and translation state); SerialGameTests is the established collection for that.

    [GameFact(SageGame.CncGenerals)]
    public void AuthoredTopology_BuildsDomainModel_AndProjectsThroughVisualization()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var nodes = new[]
        {
            new TransportGraphNode(1, 0, new System.Numerics.Vector3(0f, 0f, 0f)),
            new TransportGraphNode(2, 0, new System.Numerics.Vector3(100f, 0f, 0f)),
            new TransportGraphNode(3, 1, new System.Numerics.Vector3(200f, 0f, 50f)),
        };
        var edges = new[]
        {
            new TransportGraphEdge(nodes[0], nodes[1]),
            new TransportGraphEdge(nodes[1], nodes[2]),
        };
        var domain = TransportGraphDomainModel.Create(game.GameEngine, new TransportGraphTopology(nodes, edges));

        Assert.Equal(3, domain.Topology.Nodes.Count);
        Assert.Equal(2, domain.Topology.Edges.Count);
        Assert.Equal("node:1", domain.Topology.Nodes[0].Key);
        Assert.Equal("edge:1->2", domain.Topology.Edges[0].Key);

        var description = domain.DescribeTopology();
        Assert.Contains("node:3", description.NodeKeys);
        Assert.Contains(("node:2", "node:3"), description.Adjacencies);

        var visualization = domain.CaptureVisualization();
        Assert.Equal(3, visualization.Count(p => p is DomainVisualPoint point && point.KindName == "node"));
    }
}
