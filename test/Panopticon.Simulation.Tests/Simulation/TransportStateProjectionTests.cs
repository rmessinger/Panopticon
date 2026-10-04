using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Simulation.Transport;
using Xunit;

namespace Panopticon.Tests.Simulation;

// WI-A4-45: the transport-graph domain is the A4-4 generality proof. A full recorded run of
// the default scenario must keep vehicle-family residuals empty at every sampled frame (the
// occupancy family conserves moving vehicles) and its final edge records must equal the live
// throughput counters.

public class TransportStateProjectionTests : MockedGameTest
{
    [Fact]
    public void TransportGraphDomain_ProjectionConservesVehiclesAndMatchesEdgeThroughput()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);
        var host = new SimulationHost(game.GameEngine, domain);

        var sampledFrames = new Dictionary<uint, DomainStateFrame>();
        RunOutput output;
        using (var recording = RunRecording.Attach(
            host, game, seed: 11u, scenarioId: "a4-transport-projection", sampleInterval: 10))
        {
            recording.Sampler.FrameSampled += (summary, _) =>
                sampledFrames[summary.Frame] = domain.CaptureState(summary.Frame);
            var result = host.Run(600, _ => domain.AllRoutesComplete);
            Assert.True(result.StopPredicateTriggered);
            output = recording.Complete();

            // The recorder picked the projection up through the host's domain model.
            Assert.NotNull(recording.DomainState);
        }

        Assert.Equal(1, output.DomainStateSchemaVersion);

        var orderedFrames = sampledFrames.Values.OrderBy(f => f.Frame).ToList();
        Assert.NotEmpty(orderedFrames);
        var schema = domain.StateSchema;
        var first = orderedFrames[0];

        // Vehicle conservation: occupancy moves between nodes but never appears or disappears.
        foreach (var frame in orderedFrames)
        {
            Assert.Empty(DomainConservation.Residuals(schema, first, frame));
        }

        // Final edge records equal the live counters.
        var final = orderedFrames[^1];
        foreach (var edge in domain.Topology.Edges)
        {
            var record = Assert.Single(final.Records, r => r.Key == edge.Key);
            Assert.Equal((long)domain.EdgeThroughput(edge.Key), record.Integers[0]);
            Assert.Equal([edge.From.Key, edge.To.Key], record.References);
        }

        // Every route terminates at node 5: all three vehicles are counted there at the end.
        var finalNode = Assert.Single(final.Records, r => r.Key == domain.Topology.GetNode(5).Key);
        Assert.Equal(3, finalNode.Integers[0]);
    }
}
