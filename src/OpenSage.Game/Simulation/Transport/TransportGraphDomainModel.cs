#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Logic.Object;

namespace OpenSage.Simulation.Transport;

/// <summary>
/// The minimal transport/graph A4-1 domain adapter: proves the seam is topology-general by
/// running graph nodes, edges, edge-coupled crossings with tail-owner authority, and boundary
/// metrics on the shared <see cref="GameObject"/>/module runtime and the same
/// <see cref="SimulationHost"/> the RTS adapter uses. Not a transport game (the non-RTS
/// completeness Decision). Design: docs/roadmap/deep-dives/a4-simulation-domain-model.md.
/// </summary>
public sealed class TransportGraphDomainModel : ISimulationDomainModel
{
    private readonly IGameEngine _gameEngine;
    private readonly TransportGraphTopology _topology;

    private readonly Dictionary<int, GameObject> _nodeControllers = new();
    private readonly Dictionary<string, uint> _edgeThroughput = new();
    private readonly Dictionary<int, int> _nodeOccupancy = new();
    private readonly Dictionary<uint, string> _entityTopologyKeys = new();
    private readonly List<TransportVehicleUpdate> _vehicles = new();

    // Authority resolutions: one entry per edge crossing, recording the resolving tail
    // controller id (the documented owner rule).
    public IReadOnlyList<(string EdgeKey, uint AuthorityId)> AuthorityResolutions { get; } = new List<(string, uint)>();

    public string DomainId => "transport-graph";

    public DomainFidelityPolicy Fidelity => DomainFidelityPolicy.Identity;

    public TransportGraphTopology Topology => _topology;

    public bool AllRoutesComplete => _vehicles.Count > 0 && _vehicles.All(v => v.RouteComplete);

    private TransportGraphDomainModel(IGameEngine gameEngine, TransportGraphTopology topology)
    {
        _gameEngine = gameEngine;
        _topology = topology;
    }

    /// <summary>
    /// Builds the default validation scenario: the default topology, one stationary node
    /// controller entity per node (owned by the node's side), and three vehicles whose fixed
    /// routes all terminate at node 5 across the partition cut.
    /// </summary>
    public static TransportGraphDomainModel CreateDefault(IGameEngine gameEngine)
    {
        var topology = TransportGraphTopology.CreateDefault();
        var domain = new TransportGraphDomainModel(gameEngine, topology);

        var controllerDefinition = new ObjectDefinition("TransportNodeController");
        foreach (var node in topology.Nodes)
        {
            var owner = gameEngine.Game.PlayerManager.GetPlayerByIndex((uint)node.OwnerSide);
            var controller = gameEngine.GameLogic.CreateObject(controllerDefinition, owner);
            controller.UpdateTransform(node.Position, null);
            domain._nodeControllers[node.Index] = controller;
            domain._nodeOccupancy[node.Index] = 0;
            domain._entityTopologyKeys[controller.Id.Index] = node.Key;
        }

        domain.SpawnVehicle([0, 1, 2, 5]);
        domain.SpawnVehicle([0, 1, 4, 5]);
        domain.SpawnVehicle([3, 4, 5]);

        return domain;
    }

    /// <summary>Spawns one vehicle following <paramref name="route"/> (node indices, edge-connected).</summary>
    public GameObject SpawnVehicle(IReadOnlyList<int> route)
    {
        var definition = new ObjectDefinition("TransportVehicle");
        definition.Behaviors.Add("ModuleTag_TransportVehicle",
            new ModuleDataContainer(
                "ModuleTag_TransportVehicle",
                new TransportVehicleUpdateModuleData { Domain = this, Route = route },
                ModuleInheritanceMode.Default,
                inherited: false));

        var owner = _gameEngine.Game.PlayerManager.GetPlayerByIndex(0u);
        var vehicle = _gameEngine.GameLogic.CreateObject(definition, owner);
        vehicle.UpdateTransform(GetNodePosition(route[0]), null);
        _entityTopologyKeys[vehicle.Id.Index] = $"edge:{route[0]}->{route[1]}";
        _nodeOccupancy[route[0]]++;
        _vehicles.Add(vehicle.FindBehavior<TransportVehicleUpdate>()!);
        return vehicle;
    }

    internal Vector3 GetNodePosition(int nodeIndex) => _topology.GetNode(nodeIndex).Position;

    internal void NotifyNodeArrived(TransportVehicleUpdate vehicle, int fromNode, int toNode)
    {
        var edge = _topology.FindEdge(fromNode, toNode)
            ?? throw new System.InvalidOperationException($"Route uses non-existent edge {fromNode}->{toNode}.");
        var gameObject = vehicle.ParentGameObject;

        // Edge-coupled interaction: resolved exactly once, by the tail node's controller
        // (owner-authority rule). B6 maps the returned authority id to the owning shard.
        var interaction = new DomainInteraction(gameObject.Id.Index, _nodeControllers[fromNode].Id.Index, edge.Key);
        var authorityId = ResolveInteractionAuthority(in interaction);
        ((List<(string, uint)>)AuthorityResolutions).Add((edge.Key, authorityId));

        _edgeThroughput.TryGetValue(edge.Key, out var throughput);
        _edgeThroughput[edge.Key] = throughput + 1;
        _nodeOccupancy[fromNode]--;
        _nodeOccupancy[toNode]++;

        _entityTopologyKeys[gameObject.Id.Index] = vehicle.RouteComplete
            ? _topology.GetNode(toNode).Key
            : vehicle.CurrentEdgeKey;
    }

    public uint EdgeThroughput(string edgeKey)
    {
        _edgeThroughput.TryGetValue(edgeKey, out var value);
        return value;
    }

    public int NodeOccupancy(int nodeIndex) => _nodeOccupancy[nodeIndex];

    public uint NodeControllerId(int nodeIndex) => _nodeControllers[nodeIndex].Id.Index;

    public void Initialize(SimulationHost host)
    {
    }

    public void RunStarting()
    {
    }

    public void RunComplete(in SimulationHostRunResult result)
    {
    }

    public IReadOnlyList<DomainEntitySummary> CaptureEntities()
    {
        var playerManager = _gameEngine.Game.PlayerManager;
        var entities = new List<DomainEntitySummary>();

        foreach (var (nodeIndex, controller) in _nodeControllers)
        {
            entities.Add(new DomainEntitySummary(
                controller.Id.Index,
                controller.Definition.Name,
                playerManager.GetPlayerIndex(controller.Owner),
                controller.Translation,
                _nodeOccupancy[nodeIndex],
                0f));
        }

        foreach (var vehicle in _vehicles)
        {
            var gameObject = vehicle.ParentGameObject;
            entities.Add(new DomainEntitySummary(
                gameObject.Id.Index,
                gameObject.Definition.Name,
                playerManager.GetPlayerIndex(gameObject.Owner),
                gameObject.Translation,
                vehicle.EdgeProgress,
                0f));
        }

        entities.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
        return entities;
    }

    public IReadOnlyList<uint> CapturePlayerMoneyVector() => [];

    public string TopologyKey(in DomainEntitySummary entity)
    {
        return _entityTopologyKeys.TryGetValue(entity.EntityId, out var key)
            ? key
            : "node:?";
    }

    public DomainTopologyDescription DescribeTopology()
    {
        return new DomainTopologyDescription(
            _topology.Nodes.Select(n => n.Key).ToArray(),
            _topology.Edges.Select(e => (e.From.Key, e.To.Key)).ToArray());
    }

    public DomainMetricsSnapshot SampleMetrics()
    {
        var snapshot = new DomainMetricsSnapshot();
        foreach (var (edgeKey, throughput) in _edgeThroughput.OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
        {
            snapshot.Series.Add(new DomainMetricValue("edge-throughput", edgeKey, throughput));
        }

        foreach (var (nodeIndex, occupancy) in _nodeOccupancy.OrderBy(kv => kv.Key))
        {
            snapshot.Series.Add(new DomainMetricValue("node-occupancy", $"node:{nodeIndex}", occupancy));
        }

        return snapshot;
    }

    public uint ResolveInteractionAuthority(in DomainInteraction interaction)
    {
        // Owner rule: the tail node's controller entity resolves interactions on its edges.
        var separator = interaction.TopologyKey.IndexOf("->", System.StringComparison.Ordinal);
        var from = int.Parse(interaction.TopologyKey["edge:".Length..separator]);
        return _nodeControllers[from].Id.Index;
    }
}
