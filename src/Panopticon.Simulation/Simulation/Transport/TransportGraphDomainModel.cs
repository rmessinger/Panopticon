#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic.Object;
using Panopticon.Recording;

namespace Panopticon.Simulation.Transport;

/// <summary>
/// The minimal transport/graph A4-1 domain adapter: proves the seam is topology-general by
/// running graph nodes, edges, edge-coupled crossings with tail-owner authority, and boundary
/// metrics on the shared <see cref="GameObject"/>/module runtime and the same
/// <see cref="SimulationHost"/> the RTS adapter uses. Not a transport game (the non-RTS
/// completeness Decision). Design: docs/roadmap/deep-dives/a4-simulation-domain-model.md.
/// Also the A4-4 generality proof: projects its state through <see cref="IDomainStateProjection"/>
/// with vehicle conservation on the node occupancy family. Design:
/// docs/roadmap/deep-dives/a4-4-domain-state-projection.md.
/// </summary>
public sealed class TransportGraphDomainModel : ISimulationDomainModel, IDomainStateProjection, IReconstructableDomain
{
    private static readonly DomainStateSchema ProjectionSchema = new(
        "transport-graph",
        1,
        new DomainRecordKind[]
        {
            new(
                "node",
                "graph",
                new DomainStateField[]
                {
                    new("occupancy", "vehicles", DomainQuantityType.Integer, DomainConservationRole.Holding, "vehicle"),
                },
                []),
            new(
                "edge",
                "graph",
                new DomainStateField[]
                {
                    new("throughput", "crossings", DomainQuantityType.Integer, DomainConservationRole.None, null),
                },
                new[] { "from", "to" }),
            new(
                "vehicle",
                "graph",
                new DomainStateField[]
                {
                    new("edge_progress", "ratio", DomainQuantityType.Real, DomainConservationRole.None, null),
                },
                new[] { "edge" }),
        });

    private static readonly IReadOnlySet<string> GranularLayersValue = new HashSet<string>(["graph"]);

    private readonly IGameEngine _gameEngine;
    private readonly TransportGraphTopology _topology;
    private ObjectDefinition? _vehicleTemplate;

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

    public DomainStateSchema StateSchema => ProjectionSchema;

    public TransportGraphTopology Topology => _topology;

    public bool AllRoutesComplete => _vehicles.Count > 0 && _vehicles.All(v => v.RouteComplete);

    /// <summary>Operational-recording cadence; tests tighten it to short intervals (WI-A1-24).</summary>
    public RecordingCadence Cadence { get; set; } = RecordingOptions.CreateDefaultCadence();

    IReadOnlySet<string> IReconstructableDomain.GranularLayers => GranularLayersValue;

    private TransportGraphDomainModel(IGameEngine gameEngine, TransportGraphTopology topology)
    {
        _gameEngine = gameEngine;
        _topology = topology;
    }

    /// <summary>
    /// Builds the default validation scenario: the default topology, one stationary node
    /// controller entity per node (owned by the node's side), and three vehicles whose fixed
    /// routes all terminate at node 5 across the partition cut. Object definitions are
    /// registered in the game's asset store so checkpoints can recreate the objects by name
    /// (WI-A1-24).
    /// </summary>
    public static TransportGraphDomainModel CreateDefault(IGameEngine gameEngine)
    {
        var topology = TransportGraphTopology.CreateDefault();
        var domain = new TransportGraphDomainModel(gameEngine, topology);

        var controllerDefinition = new ObjectDefinition("TransportNodeController");
        gameEngine.Game.AssetStore.ObjectDefinitions.Add(controllerDefinition);
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
        _vehicleTemplate ??= BuildVehicleTemplate();

        var owner = _gameEngine.Game.PlayerManager.GetPlayerByIndex(0u);
        var vehicle = _gameEngine.GameLogic.CreateObject(_vehicleTemplate, owner);
        vehicle.UpdateTransform(GetNodePosition(route[0]), null);
        vehicle.FindBehavior<TransportVehicleUpdate>()!.InitializeRoute(route);
        _entityTopologyKeys[vehicle.Id.Index] = $"edge:{route[0]}->{route[1]}";
        _nodeOccupancy[route[0]]++;
        _vehicles.Add(vehicle.FindBehavior<TransportVehicleUpdate>()!);
        return vehicle;
    }

    private ObjectDefinition BuildVehicleTemplate()
    {
        var definition = new ObjectDefinition("TransportVehicle");
        definition.Behaviors.Add("ModuleTag_TransportVehicle",
            new ModuleDataContainer(
                "ModuleTag_TransportVehicle",
                new TransportVehicleUpdateModuleData { Domain = this },
                ModuleInheritanceMode.Default,
                inherited: false));
        _gameEngine.Game.AssetStore.ObjectDefinitions.Add(definition);
        return definition;
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

    /// <summary>
    /// Projects the graph state: node occupancy (the conserved vehicle family), edge throughput
    /// with endpoint references, and one record per vehicle keyed by entity id referencing its
    /// current edge (node key once the route completes).
    /// </summary>
    public DomainStateFrame CaptureState(uint frame)
    {
        var result = new DomainStateFrame { Frame = frame };

        foreach (var node in _topology.Nodes)
        {
            result.Records.Add(new DomainStateRecord
            {
                Key = node.Key,
                Kind = "node",
                Integers = [_nodeOccupancy[node.Index]],
            });
        }

        foreach (var edge in _topology.Edges)
        {
            result.Records.Add(new DomainStateRecord
            {
                Key = edge.Key,
                Kind = "edge",
                Integers = [EdgeThroughput(edge.Key)],
                References = [edge.From.Key, edge.To.Key],
            });
        }

        foreach (var vehicle in _vehicles)
        {
            var gameObject = vehicle.ParentGameObject;
            result.Records.Add(new DomainStateRecord
            {
                Key = $"vehicle:{gameObject.Id.Index}",
                Kind = "vehicle",
                Reals = [vehicle.EdgeProgress],
                References = [_entityTopologyKeys[gameObject.Id.Index]],
            });
        }

        result.Records.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return result;
    }

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

    public IReadOnlyList<DomainStateRecord> CaptureRollups(uint frame) => [];

    public IReadOnlyList<DomainEvent> DrainOperationalEvents() => [];

    /// <summary>
    /// Persists the domain's non-object runtime state (occupancy, throughput, topology keys,
    /// authority resolutions, controller bindings) so a checkpoint restore can rebuild it after
    /// the engine section recreates the GameObjects (WI-A1-24).
    /// </summary>
    public void PersistRuntime(StatePersister persister)
    {
        persister.PersistVersion(1);

        PersistOccupancy(persister);
        PersistThroughput(persister);
        PersistTopologyKeys(persister);
        PersistAuthorityResolutions(persister);
        PersistControllers(persister);

        if (persister.Mode == StatePersistMode.Read)
        {
            RebuildVehicleList();
        }
    }

    private void PersistOccupancy(StatePersister persister)
    {
        var entries = persister.Mode == StatePersistMode.Read
            ? null
            : _nodeOccupancy.OrderBy(kv => kv.Key).ToList();
        var count = (uint)(entries?.Count ?? 0);
        persister.PersistUInt32(ref count, "OccupancyCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            _nodeOccupancy.Clear();
        }

        for (var i = 0; i < count; i++)
        {
            var nodeIndex = entries?[i].Key ?? 0;
            var occupancy = entries?[i].Value ?? 0;
            persister.PersistInt32(ref nodeIndex, "NodeIndex");
            persister.PersistInt32(ref occupancy, "Occupancy");
            if (persister.Mode == StatePersistMode.Read)
            {
                _nodeOccupancy[nodeIndex] = occupancy;
            }
        }
    }

    private void PersistThroughput(StatePersister persister)
    {
        var entries = persister.Mode == StatePersistMode.Read
            ? null
            : _edgeThroughput.OrderBy(kv => kv.Key, System.StringComparer.Ordinal).ToList();
        var count = (uint)(entries?.Count ?? 0);
        persister.PersistUInt32(ref count, "ThroughputCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            _edgeThroughput.Clear();
        }

        for (var i = 0; i < count; i++)
        {
            var edgeKey = entries?[i].Key ?? "";
            var throughput = entries?[i].Value ?? 0u;
            persister.PersistAsciiString(ref edgeKey, "EdgeKey");
            persister.PersistUInt32(ref throughput, "Throughput");
            if (persister.Mode == StatePersistMode.Read)
            {
                _edgeThroughput[edgeKey] = throughput;
            }
        }
    }

    private void PersistTopologyKeys(StatePersister persister)
    {
        var entries = persister.Mode == StatePersistMode.Read
            ? null
            : _entityTopologyKeys.OrderBy(kv => kv.Key).ToList();
        var count = (uint)(entries?.Count ?? 0);
        persister.PersistUInt32(ref count, "TopologyKeyCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            _entityTopologyKeys.Clear();
        }

        for (var i = 0; i < count; i++)
        {
            var entityId = entries?[i].Key ?? 0u;
            var topologyKey = entries?[i].Value ?? "";
            persister.PersistUInt32(ref entityId, "EntityId");
            persister.PersistAsciiString(ref topologyKey, "TopologyKey");
            if (persister.Mode == StatePersistMode.Read)
            {
                _entityTopologyKeys[entityId] = topologyKey;
            }
        }
    }

    private void PersistAuthorityResolutions(StatePersister persister)
    {
        var resolutions = (List<(string EdgeKey, uint AuthorityId)>)AuthorityResolutions;
        var count = (uint)resolutions.Count;
        persister.PersistUInt32(ref count, "ResolutionCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            resolutions.Clear();
        }

        for (var i = 0; i < count; i++)
        {
            var reading = persister.Mode == StatePersistMode.Read;
            var edgeKey = reading ? "" : resolutions[i].EdgeKey;
            var authorityId = reading ? 0u : resolutions[i].AuthorityId;
            persister.PersistAsciiString(ref edgeKey, "EdgeKey");
            persister.PersistUInt32(ref authorityId, "AuthorityId");
            if (reading)
            {
                resolutions.Add((edgeKey, authorityId));
            }
        }
    }

    private void PersistControllers(StatePersister persister)
    {
        var entries = persister.Mode == StatePersistMode.Read
            ? null
            : _nodeControllers
                .OrderBy(kv => kv.Key)
                .Select(kv => (NodeIndex: kv.Key, ObjectId: kv.Value.Id.Index))
                .ToList();
        var count = (uint)(entries?.Count ?? 0);
        persister.PersistUInt32(ref count, "ControllerCount");
        if (persister.Mode == StatePersistMode.Read)
        {
            _nodeControllers.Clear();
        }

        for (var i = 0; i < count; i++)
        {
            var nodeIndex = entries?[i].NodeIndex ?? 0;
            var objectId = entries?[i].ObjectId ?? 0u;
            persister.PersistInt32(ref nodeIndex, "NodeIndex");
            persister.PersistUInt32(ref objectId, "ObjectId");
            if (persister.Mode == StatePersistMode.Read)
            {
                _nodeControllers[nodeIndex] = _gameEngine.GameLogic.GetObjectById(new ObjectId(objectId));
            }
        }
    }

    private void RebuildVehicleList()
    {
        _vehicles.Clear();
        foreach (var gameObject in _gameEngine.Game.Scene3D.GameObjects.Objects.OrderBy(o => o.Id.Index))
        {
            var vehicle = gameObject.FindBehavior<TransportVehicleUpdate>();
            if (vehicle != null)
            {
                _vehicles.Add(vehicle);
            }
        }
    }
}
