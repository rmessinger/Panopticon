#nullable enable

using System.Collections.Frozen;
using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>The 29 audited legacy section names.</summary>
public static class MapSectionNames
{
    public static IReadOnlySet<string> All { get; } = new[]
    {
        "AssetList",
        "GlobalVersion",
        "HeightMapData",
        "BlendTileData",
        "WorldInfo",
        "MPPositionList",
        "SidesList",
        "LibraryMapLists",
        "Teams",
        "PlayerScriptsList",
        "BuildLists",
        "ObjectsList",
        "PolygonTriggers",
        "TriggerAreas",
        "GlobalWaterSettings",
        "FogSettings",
        "MissionHotSpots",
        "MissionObjectives",
        "StandingWaterAreas",
        "RiverAreas",
        "StandingWaveAreas",
        "GlobalLighting",
        "PostEffectsChunk",
        "EnvironmentData",
        "NamedCameras",
        "CameraAnimationList",
        "CastleTemplates",
        "WaypointsList",
        "SkyboxSettings",
    }.ToFrozenSet();

    public static bool IsKnownSection(string name) => All.Contains(name);
}
