#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Lighting configuration for one time of day, holding sun, accent, and optional infantry lights.</summary>
public sealed record MapLightingConfigurationRecord(
    MapGlobalLightRecord? TerrainSun,
    MapGlobalLightRecord? TerrainAccent1,
    MapGlobalLightRecord? TerrainAccent2,
    MapGlobalLightRecord? ObjectSun,
    MapGlobalLightRecord? ObjectAccent1,
    MapGlobalLightRecord? ObjectAccent2,
    MapGlobalLightRecord? InfantrySun,
    MapGlobalLightRecord? InfantryAccent1,
    MapGlobalLightRecord? InfantryAccent2);
