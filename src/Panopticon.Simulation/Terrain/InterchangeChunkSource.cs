#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Terrain;

/// <summary>Serves chunks from an in-memory <see cref="TerrainLayout"/>.</summary>
public sealed class InterchangeChunkSource : ITerrainChunkSource
{
    private readonly TerrainLayout _layout;

    private readonly Dictionary<ChunkKey, Chunk> _chunks;

    public InterchangeChunkSource(TerrainLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _chunks = layout.Chunks.ToDictionary(chunk => chunk.Key);
    }

    public int ChunksX => _layout.ChunksX;

    public int ChunksY => _layout.ChunksY;

    public int LatticeWidth => _layout.LatticeWidth;

    public int LatticeHeight => _layout.LatticeHeight;

    public TerrainChunkData LoadChunk(ChunkKey key)
    {
        if (key.Cx < 0 || key.Cx >= ChunksX || key.Cy < 0 || key.Cy >= ChunksY)
        {
            throw new TerrainChunkLoadException(
                $"Chunk ({key.Cx}, {key.Cy}) lies outside the {ChunksX} x {ChunksY} chunk grid.");
        }

        if (!_chunks.TryGetValue(key, out var chunk))
        {
            throw new TerrainChunkLoadException($"Chunk ({key.Cx}, {key.Cy}) is missing from the terrain layout.");
        }

        var coreWidth = Math.Min(MapChunkGeometry.CorePointsPerAxis, _layout.LatticeWidth - (key.Cx * MapChunkGeometry.IntervalsPerChunkAxis));
        var coreHeight = Math.Min(MapChunkGeometry.CorePointsPerAxis, _layout.LatticeHeight - (key.Cy * MapChunkGeometry.IntervalsPerChunkAxis));

        try
        {
            return TerrainChunkData.FromChunk(chunk, coreWidth, coreHeight);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainChunkLoadException($"Chunk ({key.Cx}, {key.Cy}) failed validation: {ex.Message}", ex);
        }
    }
}
