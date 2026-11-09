#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Terrain texture cell definition for BlendTileData.</summary>
public sealed record MapBlendTextureRecord(
    string Name,
    uint CellStart,
    uint CellCount,
    uint CellSize,
    uint MagicValue);
