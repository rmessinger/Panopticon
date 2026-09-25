using System.Linq;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Simulation.Transport;
using Xunit;

namespace Panopticon.Tests.Simulation;

/// <summary>
/// WI-A4-09: the minimal transport/graph domain runs on the shared SimulationHost and entity
/// runtime, crosses the partition boundary, and emits owner-authority resolutions, boundary
/// metrics, and stable graph recording keys.
/// </summary>
public sealed class TransportGraphDomainTests : MockedGameTest
{
    [Fact]
    public void GraphScenario_CrossesPartitionBoundary_AndRecordsStableKeysAndMetrics()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);
        var host = new SimulationHost(game.GameEngine, domain);

        using var recording = RunRecording.Attach(host, game, seed: 7u, scenarioId: "a4-graph", sampleInterval: 10);
        var result = host.Run(400, h => domain.AllRoutesComplete);
        // A few extra steps so the next T1 sample lands after completion (node:5 keys).
        host.Run(10);

        // Terminating: every vehicle finished its route through the host.
        Assert.True(result.StopPredicateTriggered);
        Assert.True(domain.AllRoutesComplete);

        // Edge-coupled crossings resolved exactly once each, on the tail node controller.
        Assert.Equal(8, domain.AuthorityResolutions.Count);
        foreach (var (edgeKey, authorityId) in domain.AuthorityResolutions)
        {
            var tail = int.Parse(edgeKey["edge:".Length..edgeKey.IndexOf("->", System.StringComparison.Ordinal)]);
            Assert.Equal(domain.NodeControllerId(tail), authorityId);
        }

        // Boundary metrics: the cut edges are 1->2 and 4->5 (into the far column). Routes
        // [0,1,2,5] crosses 1->2 once; [0,1,4,5] and [3,4,5] cross 4->5 once each.
        Assert.Equal(2u, domain.EdgeThroughput("edge:4->5"));
        var cutEdges = domain.Topology.Edges.Where(e => e.IsPartitionCut).Select(e => e.Key).ToList();
        Assert.Equal(3u, cutEdges.Sum(e => domain.EdgeThroughput(e)));

        // Occupancy conservation: all three vehicles end at node 5, none remain at starts.
        Assert.Equal(3, domain.NodeOccupancy(5));
        Assert.Equal(0, domain.NodeOccupancy(0));
        Assert.Equal(0, domain.NodeOccupancy(3));

        var output = recording.Complete();

        // Output portability: graph run produces the same run-output shape with its domain id.
        Assert.Equal("transport-graph", output.DomainId);
        Assert.All(output.MetricSamples, s => Assert.Empty(s.Players));
        Assert.Contains(
            output.MetricSamples.SelectMany(s => s.DomainMetrics),
            m => m.Series == "edge-throughput" && m.Key == "edge:4->5" && m.Value == 2);
        Assert.Contains(
            output.MetricSamples.SelectMany(s => s.DomainMetrics),
            m => m.Series == "node-occupancy" && m.Key == "node:5");

        // Stable graph keys appear in T1: edge buckets while moving, node:5 after arrival.
        Assert.Contains(output.SignatureTrees, t => t.Nodes.Any(n => n.Key.StartsWith("edge:")));
        Assert.Contains(output.SignatureTrees, t => t.Nodes.Any(n => n.Key == "node:5"));
    }

    [Fact]
    public void DescribeTopology_ListsNodesAndEdges()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);

        var topology = domain.DescribeTopology();

        Assert.Equal(6, topology.NodeKeys.Count);
        Assert.Equal(7, topology.Adjacencies.Count);
        Assert.Contains("node:5", topology.NodeKeys);
        Assert.Contains(("node:1", "node:4"), topology.Adjacencies);
    }

    [Fact]
    public void ResolveInteractionAuthority_ReturnsTailNodeController()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);

        var authority = domain.ResolveInteractionAuthority(new DomainInteraction(99u, 98u, "edge:4->5"));

        Assert.Equal(domain.NodeControllerId(4), authority);
        Assert.NotEqual(domain.NodeControllerId(5), authority);
    }
}
