#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>
/// One chunk's layers. <c>Imagery</c> is optional. <c>Materials</c> and <c>Masks</c> are optional per
/// chunk, but layers-consistency requires them on every chunk or on none: legacy sources produce
/// heights-only chunks where both are null.
/// </summary>
public sealed record Chunk(
    ChunkKey Key,
    HeightLayer Heights,
    MaterialLayer? Materials,
    MaskLayer? Masks,
    ImageryLayer? Imagery);
