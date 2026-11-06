#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Chunk grid position, counted from the southwest origin with +X east and +Y north.</summary>
public readonly record struct ChunkKey(int Cx, int Cy);
