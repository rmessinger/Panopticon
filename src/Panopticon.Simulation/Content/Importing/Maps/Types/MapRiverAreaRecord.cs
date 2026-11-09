#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One authored river segment and visual configuration.</summary>
public sealed record MapRiverAreaRecord(
    uint UniqueId,
    string Name,
    string LayerName,
    float UvScrollSpeed,
    bool UseAdditiveBlending,
    string RiverTexture,
    string NoiseTexture,
    string AlphaEdgeTexture,
    string SparkleTexture,
    byte ColorR,
    byte ColorG,
    byte ColorB,
    float Alpha,
    uint WaterHeight,
    string RiverType,
    string MinimumWaterLod,
    IReadOnlyList<MapRiverLineRecord> Lines);
