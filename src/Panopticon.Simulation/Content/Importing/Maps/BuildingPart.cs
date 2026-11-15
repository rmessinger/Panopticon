#nullable enable

using System;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One renderable part of a generated building: a stable part identity, the footprint geometry it was extruded from, and its terrain chunk.</summary>
public sealed record BuildingPart
{
    public BuildingPart(string partStableId, VectorGeometry footprint, int chunkX, int chunkY)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partStableId);
        ArgumentNullException.ThrowIfNull(footprint);
        PartStableId = partStableId;
        Footprint = footprint;
        ChunkX = chunkX;
        ChunkY = chunkY;
    }

    public string PartStableId { get; }

    public VectorGeometry Footprint { get; }

    public int ChunkX { get; }

    public int ChunkY { get; }
}
