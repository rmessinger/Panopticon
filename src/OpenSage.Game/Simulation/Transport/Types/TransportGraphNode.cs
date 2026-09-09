#nullable enable

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
