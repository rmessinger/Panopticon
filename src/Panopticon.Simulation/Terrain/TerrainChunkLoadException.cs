#nullable enable

using System;

namespace Panopticon.Terrain;

/// <summary>A terrain chunk could not be loaded from its source.</summary>
public sealed class TerrainChunkLoadException : Exception
{
    public TerrainChunkLoadException(string message)
        : base(message)
    {
    }

    public TerrainChunkLoadException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
