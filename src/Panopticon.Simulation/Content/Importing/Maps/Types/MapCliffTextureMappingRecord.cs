#nullable enable

using System.Numerics;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Cliff texture mapping coordinates and tile index.</summary>
public sealed record MapCliffTextureMappingRecord(
    uint TextureTile,
    Vector2 BottomLeftCoords,
    Vector2 BottomRightCoords,
    Vector2 TopRightCoords,
    Vector2 TopLeftCoords,
    ushort Unknown2);
