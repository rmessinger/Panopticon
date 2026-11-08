#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One standing water area polygon and shader settings.</summary>
public sealed record MapStandingWaterAreaRecord(
    uint UniqueId,
    string Name,
    string LayerName,
    float UvScrollSpeed,
    bool UseAdditiveBlending,
    string BumpMapTexture,
    string SkyTexture,
    IReadOnlyList<Vector2> Points,
    uint WaterHeight,
    string FxShader,
    string DepthColors);
