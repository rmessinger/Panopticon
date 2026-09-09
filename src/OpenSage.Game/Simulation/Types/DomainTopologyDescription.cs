#nullable enable

using System.Collections.Generic;

namespace OpenSage.Simulation;

/// <summary>
/// The domain's topology as keys and adjacency pairs, consumed by visualization and B6
/// partitioning. The RTS adapter returns an empty description (position-driven); the graph
/// adapter lists its nodes and edges.
/// </summary>
public sealed record DomainTopologyDescription(
    IReadOnlyList<string> NodeKeys,
    IReadOnlyList<(string From, string To)> Adjacencies);
