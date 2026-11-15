#nullable enable

using System;

namespace Panopticon.Terrain;

/// <summary>The runtime chunk store cannot hold or release a chunk without discarding dirty data.</summary>
public sealed class TerrainChunkStoreCapacityException : Exception
{
    public TerrainChunkStoreCapacityException(string message)
        : base(message)
    {
    }

    public TerrainChunkStoreCapacityException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
