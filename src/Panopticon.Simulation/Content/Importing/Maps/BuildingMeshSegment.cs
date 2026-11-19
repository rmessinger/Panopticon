#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One indexed triangle-list draw of a building mesh chunk: a material variant with its vertices, indices, and exact vertex bounds.</summary>
public sealed record BuildingMeshSegment
{
    public BuildingMeshSegment(
        int materialVariant,
        IReadOnlyList<BuildingVertex> vertices,
        IReadOnlyList<uint> indices,
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        MaterialVariant = materialVariant;
        Vertices = vertices.ToArray();
        Indices = indices.ToArray();
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    public int MaterialVariant { get; }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<BuildingVertex> Vertices { get; }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<uint> Indices { get; }

    public double MinX { get; }

    public double MinY { get; }

    public double MinZ { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    public double MaxZ { get; }
}
