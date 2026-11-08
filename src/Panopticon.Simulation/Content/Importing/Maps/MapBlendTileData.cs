#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for BlendTileData.</summary>
public sealed record MapBlendTileData(
    ushort Version,
    uint NumTiles,
    ushort[,] Tiles,
    uint[,] Blends,
    uint[,] ThreeWayBlends,
    uint[,] CliffTextures,
    bool[,] Impassability,
    uint TextureCellCount,
    IReadOnlyList<MapBlendTextureRecord> Textures,
    uint MagicValue1,
    uint MagicValue2,
    IReadOnlyList<MapBlendDescriptionRecord> BlendDescriptions,
    uint ParsedCliffTextureMappingsCount,
    IReadOnlyList<MapCliffTextureMappingRecord> CliffTextureMappings,
    bool[,]? ImpassabilityToPlayers = null,
    bool[,]? PassageWidths = null,
    bool[,]? Taintability = null,
    bool[,]? ExtraPassability = null,
    byte[,]? Flammability = null,
    bool[,]? Visibility = null,
    bool[,]? ImpassabilityToAirUnits = null,
    bool[,]? Buildability = null,
    bool[,]? TiberiumGrowability = null,
    byte[,]? DynamicShrubberyDensity = null);
