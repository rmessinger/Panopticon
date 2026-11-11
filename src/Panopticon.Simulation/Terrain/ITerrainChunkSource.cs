#nullable enable

using Panopticon.Content.Importing.Maps;

namespace Panopticon.Terrain;

/// <summary>Supplies chunks on demand for <see cref="RuntimeTerrainChunkStore"/>.</summary>
public interface ITerrainChunkSource
{
    int ChunksX { get; }

    int ChunksY { get; }

    int LatticeWidth { get; }

    int LatticeHeight { get; }

    /// <summary>Loads one chunk. Throws <see cref="TerrainChunkLoadException"/> on any failure.</summary>
    TerrainChunkData LoadChunk(ChunkKey key);
}
