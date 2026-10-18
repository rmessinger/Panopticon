using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Simulation.Transport;
using Xunit;

namespace Panopticon.Tests.Simulation;

// WI-A5-12: both domain adapters project visualization primitives through
// IDomainVisualization with bucket keys aligned to T1 signature recording, explicit overlay
// absence, and topology validation through the same adapter.

[Collection("SerialGameTests")]
public class VisualizationProjectionTests : MockedGameTest
{
    private static void SpawnUnit(TestGame game, string name, Vector3 position)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Infantry, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));
        var player = game.PlayerManager.GetPlayerByIndex(0);
        var obj = game.GameLogic.CreateObject(definition, player);
        obj.UpdateTransform(position, null);
    }

    [Fact]
    public void RtsPoints_CarryTheSameBucketKeysAsT1Signatures()
    {
        var game = new TestGame(SageGame.CncGenerals);
        SpawnUnit(game, "A5VizUnitA", new Vector3(50, 50, 0));
        SpawnUnit(game, "A5VizUnitB", new Vector3(150, 50, 0));
        var domain = new RtsSimulationDomainModel(game.GameEngine);

        var visualization = (IDomainVisualization)domain;
        var points = visualization.CaptureVisualization().OfType<DomainVisualPoint>().ToList();

        Assert.Equal(2, points.Count);
        Assert.Contains(points, p => p.BucketKey == "cell:0,0");
        Assert.Contains(points, p => p.BucketKey == "cell:1,0");

        var summary = WorldStateSummary.Capture(domain, 0);
        var tree = SignatureTreeBuilder.Build(0, summary, domain);
        var level1Keys = tree.Nodes.Where(n => n.Level == 1).Select(n => n.Key).ToList();
        Assert.Equal(points.Select(p => p.BucketKey).Order(), level1Keys.Order());
    }

    [Fact]
    public void RtsOffers_OverlayCategories_AndEmptyTopologyValidation()
    {
        var game = new TestGame(SageGame.CncGenerals);
        SpawnUnit(game, "A5VizOverlayUnit", new Vector3(50, 50, 0));
        var visualization = (IDomainVisualization)new RtsSimulationDomainModel(game.GameEngine);

        var overlays = visualization.DescribeOverlays();

        Assert.Equal(2, overlays.Count);
        var owner = Assert.Single(overlays, o => o.Id == "owner");
        Assert.NotEmpty(owner.Categories);
        var kind = Assert.Single(overlays, o => o.Id == "kind");
        Assert.Contains("A5VizOverlayUnit", kind.Categories);
        Assert.Empty(visualization.ValidateTopology());
    }

    [Fact]
    public void Graph_ProducesDistinctNodeEdgeAndVehiclePrimitives()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);
        var visualization = (IDomainVisualization)domain;

        var primitives = visualization.CaptureVisualization();

        var nodes = primitives.OfType<DomainVisualPoint>().Where(p => p.KindName == "node").ToList();
        var vehicles = primitives.OfType<DomainVisualPoint>().Where(p => p.KindName == "TransportVehicle").ToList();
        var edges = primitives.OfType<DomainVisualPolyline>().ToList();

        Assert.Equal(domain.Topology.Nodes.Count, nodes.Count);
        Assert.Equal(3, vehicles.Count);
        Assert.Equal(domain.Topology.Edges.Count, edges.Count);
        Assert.All(nodes, p => Assert.StartsWith("node:", p.BucketKey));
        Assert.All(edges, e => Assert.StartsWith("edge:", e.BucketKey));
        Assert.All(vehicles, v => Assert.StartsWith("edge:", v.BucketKey));
        Assert.All(edges, e => Assert.Equal(2, e.Points.Count));
    }

    [Fact]
    public void GraphDeclaresMetricsExplicitlyHasNoOverlayAndValidatesTopology()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);
        var visualization = (IDomainVisualization)domain;

        var metrics = visualization.DescribeMetrics();
        Assert.Equal(2, metrics.Count);
        Assert.Contains(metrics, m => m.Series == "edge-throughput");
        Assert.Contains(metrics, m => m.Series == "node-occupancy");

        // Absence is explicit: an empty list, never null.
        Assert.NotNull(visualization.DescribeOverlays());
        Assert.Empty(visualization.DescribeOverlays());

        Assert.Empty(visualization.ValidateTopology());
    }
}
