#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace OpenSage.Simulation;

/// <summary>
/// One comparable entity observation, the unit all recording tiers consume (A4-1).
/// <see cref="ScalarA"/>/<see cref="ScalarB"/> are the two domain-quantity slots the
/// recording tolerances quantize (RTS: health and max health; transport: edge progress and
/// zero). Two slots, not a vector, keeps the persisted <c>WorldObjectState</c> artifact
/// format unchanged.
/// </summary>
public readonly record struct DomainEntitySummary(
    uint EntityId,
    string KindName,
    int OwnerIndex,
    Vector3 Position,
    float ScalarA,
    float ScalarB);

/// <summary>
/// Per-player metric rows for the T0 run-output section (RTS projection; non-player domains
/// leave this empty).
/// </summary>
public sealed record DomainPlayerMetrics(string Player, uint Money, int OwnedObjects);

/// <summary>One value in a named, keyed domain metric series.</summary>
public sealed record DomainMetricValue(string Series, string Key, double Value);

/// <summary>
/// The metrics snapshot a domain supplies on request: player rows plus domain-specific
/// series (transport: edge throughput, node occupancy).
/// </summary>
public sealed class DomainMetricsSnapshot
{
    public List<DomainPlayerMetrics> Players { get; } = new();

    public List<DomainMetricValue> Series { get; } = new();
}

/// <summary>
/// The domain's topology as keys and adjacency pairs, consumed by visualization and B6
/// partitioning. The RTS adapter returns an empty description (position-driven); the graph
/// adapter lists its nodes and edges.
/// </summary>
public sealed record DomainTopologyDescription(
    IReadOnlyList<string> NodeKeys,
    IReadOnlyList<(string From, string To)> Adjacencies);

/// <summary>
/// One interaction between two entities at a topology key, submitted to the domain's
/// authority rule.
/// </summary>
public readonly record struct DomainInteraction(
    uint InitiatorId,
    uint CounterpartId,
    string TopologyKey);
