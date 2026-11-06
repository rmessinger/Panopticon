#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>One chunk's layers. <c>Imagery</c> is optional.</summary>
public sealed record Chunk(
    ChunkKey Key,
    HeightLayer Heights,
    MaterialLayer Materials,
    MaskLayer Masks,
    ImageryLayer? Imagery);
