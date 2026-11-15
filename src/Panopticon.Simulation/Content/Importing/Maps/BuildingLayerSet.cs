#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed generated-building layer of a map interchange: building metadata plus per-chunk mesh geometry. Buildings and mesh chunks are copied at construction.</summary>
public sealed record BuildingLayerSet
{
    public BuildingLayerSet(IReadOnlyList<GeneratedBuilding> buildings, IReadOnlyList<BuildingMeshChunk> meshChunks)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(meshChunks);
        Buildings = buildings.ToArray();
        MeshChunks = meshChunks.ToArray();
    }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<GeneratedBuilding> Buildings { get; }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<BuildingMeshChunk> MeshChunks { get; }
}
