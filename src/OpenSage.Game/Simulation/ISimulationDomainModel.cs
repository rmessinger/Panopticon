#nullable enable

using System.Collections.Generic;

namespace OpenSage.Simulation;

/// <summary>
/// Domain fidelity policy hook (A4-1). v1 is the identity level everywhere; richer
/// aggregation policies arrive with B7 and extend adapters behind this attachment point
/// instead of a new seam.
/// </summary>
public enum DomainFidelityLevel
{
    /// <summary>Every entity simulated every frame at full detail (today's behavior).</summary>
    Identity = 0,

    /// <summary>Reserved for B7: domain-defined aggregation of entities or interactions.</summary>
    Aggregated = 1,
}

public sealed record DomainFidelityPolicy(DomainFidelityLevel Level)
{
    public static readonly DomainFidelityPolicy Identity = new(DomainFidelityLevel.Identity);
}

/// <summary>
/// The A4-1 Simulation Domain Model seam: what the simulation is, behind one injected
/// collaborator. The host, the recording tiers, and later scale work (B3/B4/B6/B7) consume
/// these contracts; RTS is the first adapter and a minimal transport/graph domain the second.
/// Per the domain-scope Decision, non-RTS domains extend the shared GameObject/module
/// runtime: a domain is modules plus a topology/interaction adapter, not a second engine.
/// Design: docs/roadmap/deep-dives/a4-simulation-domain-model.md.
/// </summary>
public interface ISimulationDomainModel
{
    /// <summary>Stable domain identifier recorded in the run output ("rts", "transport-graph").</summary>
    string DomainId { get; }

    DomainFidelityPolicy Fidelity { get; }

    /// <summary>Called once from the <see cref="SimulationHost"/> constructor; subscribe to host events here.</summary>
    void Initialize(SimulationHost host);

    /// <summary>Called at each <see cref="SimulationHost.Run"/> entry, before the first step.</summary>
    void RunStarting();

    /// <summary>Called at each <see cref="SimulationHost.Run"/> exit, after the last completed step.</summary>
    void RunComplete(in SimulationHostRunResult result);

    /// <summary>
    /// Comparable entity observations. Implementations that share the GameObject runtime
    /// iterate their entities in stable id order.
    /// </summary>
    IReadOnlyList<DomainEntitySummary> CaptureEntities();

    /// <summary>
    /// The per-player money vector persisted in snapshot artifacts (RTS projection);
    /// non-player domains return an empty list.
    /// </summary>
    IReadOnlyList<uint> CapturePlayerMoneyVector();

    /// <summary>
    /// The stable topology bucket key for one entity (RTS: "cell:x,y"; graph: "edge:u->v",
    /// "node:i"). Anchored deterministically so keys are stable across runs regardless of
    /// iteration order or partitioning.
    /// </summary>
    string TopologyKey(in DomainEntitySummary entity);

    /// <summary>Topology keys and adjacency for visualization and B6 partitioning.</summary>
    DomainTopologyDescription DescribeTopology();

    /// <summary>Metric rows sampled by the run-output recorder.</summary>
    DomainMetricsSnapshot SampleMetrics();

    /// <summary>
    /// The entity id of the authority that resolves the interaction. RTS rule: the initiator.
    /// Graph rule: the owner side of the topology key (the tail node's controller entity).
    /// Single-process runs are sequential, so the rule changes nothing today; B6 maps the
    /// returned id to the owning shard so cross-boundary interactions resolve exactly once.
    /// </summary>
    uint ResolveInteractionAuthority(in DomainInteraction interaction);
}
