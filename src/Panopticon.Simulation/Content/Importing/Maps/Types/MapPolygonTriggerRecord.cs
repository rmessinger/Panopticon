#nullable enable

using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One authored polygon trigger area.</summary>
public sealed record MapPolygonTriggerRecord(
    string Name,
    string? LayerName,
    uint TriggerId,
    bool IsWater,
    bool IsRiver,
    uint RiverStart,
    IReadOnlyList<Vector3> Points,
    bool UseAdditiveBlending,
    Vector2 UvScrollSpeed,
    string? RiverTexture,
    string? NoiseTexture,
    string? AlphaEdgeTexture,
    string? SparkleTexture,
    string? BumpMapTexture,
    string? SkyTexture,
    byte Unknown,
    byte ColorR,
    byte ColorG,
    byte ColorB,
    float RiverAlpha);
