#nullable enable

namespace OpenSage.Simulation.Transport;

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
