#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Per-chunk mesh geometry of the generated building layer; (ChunkX, ChunkY) names the terrain chunk the triangles belong to. Segments are copied at construction.</summary>
public sealed record BuildingMeshChunk
{
    public BuildingMeshChunk(int chunkX, int chunkY, IReadOnlyList<BuildingMeshSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ChunkX = chunkX;
        ChunkY = chunkY;
        Segments = segments.ToArray();
    }

    public int ChunkX { get; }

    public int ChunkY { get; }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<BuildingMeshSegment> Segments { get; }
}
