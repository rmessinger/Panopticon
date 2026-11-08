#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Data.Map;
using Panopticon.Logic.Map;

namespace Panopticon.Tools.H5Migrate;

/// <summary>How a legacy map section is carried into an H3 package.</summary>
public enum MapSourceSectionDisposition
{
    /// <summary>The interchange carries the section in a typed component; no retained entry exists.</summary>
    TypedHome,

    /// <summary>The interchange carries the section as opaque retained bytes.</summary>
    Retained,
}

/// <summary>One legacy section present on a parsed map: its package name and disposition.</summary>
public sealed record MapSourceSection(string Name, MapSourceSectionDisposition Disposition);

/// <summary>
/// Inventories every section present on a parsed <see cref="MapFile"/> with its conversion
/// disposition, in the legacy reader's dispatch order: HeightMapData, ObjectsList, and
/// GlobalVersion are typed homes; every other present section is retained opaque. The
/// inventory is the gap gate's source of truth: a section with no disposition fails conversion.
/// </summary>
public static class MapSourceSectionInventory
{
    private static readonly (string Name, Func<MapFile, Asset?> Present)[] TypedHomes =
    [
        (HeightMapData.AssetName, mapFile => mapFile.HeightMapData),
        (ObjectsList.AssetName, mapFile => mapFile.ObjectsList),
        (GlobalVersion.AssetName, mapFile => mapFile.GlobalVersion),
    ];

    private static readonly (string Name, Func<MapFile, Asset?> Present)[] RetainedSections =
    [
        (AssetList.AssetName, mapFile => mapFile.AssetList),
        (BlendTileData.AssetName, mapFile => mapFile.BlendTileData),
        (WorldInfo.AssetName, mapFile => mapFile.WorldInfo),
        (MPPositionList.AssetName, mapFile => mapFile.MPPositionList),
        (SidesList.AssetName, mapFile => mapFile.SidesList),
        (LibraryMapLists.AssetName, mapFile => mapFile.LibraryMapLists),
        (Teams.AssetName, mapFile => mapFile.Teams),
        (PlayerScriptsList.AssetName, mapFile => mapFile.PlayerScriptsList),
        (BuildLists.AssetName, mapFile => mapFile.BuildLists),
        (PolygonTriggers.AssetName, mapFile => mapFile.PolygonTriggers),
        (TriggerAreas.AssetName, mapFile => mapFile.TriggerAreas),
        (GlobalWaterSettings.AssetName, mapFile => mapFile.GlobalWaterSettings),
        (FogSettings.AssetName, mapFile => mapFile.FogSettings),
        (MissionHotSpots.AssetName, mapFile => mapFile.MissionHotSpots),
        (MissionObjectives.AssetName, mapFile => mapFile.MissionObjectives),
        (StandingWaterAreas.AssetName, mapFile => mapFile.StandingWaterAreas),
        (RiverAreas.AssetName, mapFile => mapFile.RiverAreas),
        (StandingWaveAreas.AssetName, mapFile => mapFile.StandingWaveAreas),
        (GlobalLighting.AssetName, mapFile => mapFile.GlobalLighting),
        (PostEffectsChunk.AssetName, mapFile => mapFile.PostEffectsChunk),
        (EnvironmentData.AssetName, mapFile => mapFile.EnvironmentData),
        (NamedCameras.AssetName, mapFile => mapFile.NamedCameras),
        (CameraAnimationList.AssetName, mapFile => mapFile.CameraAnimationList),
        (CastleTemplates.AssetName, mapFile => mapFile.CastleTemplates),
        (WaypointsList.AssetName, mapFile => mapFile.WaypointsList),
        (SkyboxSettings.AssetName, mapFile => mapFile.SkyboxSettings),
    ];

    /// <summary>Lists the sections present on the parsed map, typed homes first.</summary>
    public static IReadOnlyList<MapSourceSection> Describe(MapFile mapFile)
    {
        ArgumentNullException.ThrowIfNull(mapFile);

        var sections = new List<MapSourceSection>();

        foreach (var (name, present) in TypedHomes)
        {
            if (present(mapFile) is not null)
            {
                sections.Add(new MapSourceSection(name, MapSourceSectionDisposition.TypedHome));
            }
        }

        foreach (var (name, present) in RetainedSections)
        {
            if (present(mapFile) is not null)
            {
                sections.Add(new MapSourceSection(name, MapSourceSectionDisposition.Retained));
            }
        }

        return sections;
    }
}
