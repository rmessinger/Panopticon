#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenSage.Simulation.Transport;

/// <summary>One node of the transport graph validation topology.</summary>
public sealed class TransportGraphNode
{
    public int Index { get; }

    public string Key => $"node:{Index}";

    /// <summary>Owner side used for partition-cut and authority decisions (player index).</summary>
    public int OwnerSide { get; }

    /// <summary>2-D layout position for movement and visualization.</summary>
    public Vector3 Position { get; }

    public TransportGraphNode(int index, int ownerSide, Vector3 position)
    {
        Index = index;
        OwnerSide = ownerSide;
        Position = position;
    }
}

/// <summary>One directed edge; an edge between different owner sides is a partition cut.</summary>
public sealed class TransportGraphEdge
{
    public TransportGraphNode From { get; }

    public TransportGraphNode To { get; }

    public string Key => $"edge:{From.Index}->{To.Index}";

    public bool IsPartitionCut => From.OwnerSide != To.OwnerSide;

    public TransportGraphEdge(TransportGraphNode from, TransportGraphNode to)
    {
        From = from;
        To = to;
    }
}

/// <summary>
/// The transport/graph validation topology (A4-1): a small directed graph with stable node
/// and edge keys. Deliberately minimal (the non-RTS completeness Decision): this exists to
/// exercise graph topology, edge locality, owner authority, and boundary metrics, not to be
/// a transport game.
/// </summary>
public sealed class TransportGraphTopology
{
    public IReadOnlyList<TransportGraphNode> Nodes { get; }

    public IReadOnlyList<TransportGraphEdge> Edges { get; }

    public TransportGraphTopology(IReadOnlyList<TransportGraphNode> nodes, IReadOnlyList<TransportGraphEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
    }

    public TransportGraphNode GetNode(int index) => Nodes[index];

    public TransportGraphEdge? FindEdge(int from, int to) =>
        Edges.FirstOrDefault(e => e.From.Index == from && e.To.Index == to);

    /// <summary>
    /// The default validation graph: 6 nodes on a 3x2 grid (200-unit spacing), 7 edges,
    /// owner sides split at the last column so edges into it are partition cuts.
    /// Routes [0,1,2,5], [0,1,4,5], and [3,4,5] all terminate at node 5 across the cut.
    /// </summary>
    public static TransportGraphTopology CreateDefault()
    {
        var nodes = new TransportGraphNode[6];
        for (var i = 0; i < 6; i++)
        {
            var column = i % 3;
            var row = i / 3;
            var ownerSide = column == 2 ? 1 : 0;
            nodes[i] = new TransportGraphNode(i, ownerSide, new Vector3(column * 200f, row * 200f, 0f));
        }

        var edges = new[]
        {
            new TransportGraphEdge(nodes[0], nodes[1]),
            new TransportGraphEdge(nodes[1], nodes[2]),
            new TransportGraphEdge(nodes[3], nodes[4]),
            new TransportGraphEdge(nodes[4], nodes[5]),
            new TransportGraphEdge(nodes[0], nodes[3]),
            new TransportGraphEdge(nodes[1], nodes[4]),
            new TransportGraphEdge(nodes[2], nodes[5]),
        };

        return new TransportGraphTopology(nodes, edges);
    }
}
