using System;
using Panopticon.Data.Map;
using Panopticon.Logic.Map;
using Panopticon.Scripting;
using Panopticon.Terrain;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Parameters for creating a new blank map.
/// </summary>
public sealed class NewMapParameters
{
    /// <summary>Playable width in tiles (excludes border).</summary>
    public required int PlayableWidth { get; init; }

    /// <summary>Playable height in tiles (excludes border).</summary>
    public required int PlayableHeight { get; init; }

    /// <summary>Initial flat ground height (0–255 raw sample value).</summary>
    public required byte InitialHeight { get; init; }

    /// <summary>Number of border tiles on each edge (default 6).</summary>
    public int BorderWidth { get; init; } = 6;

    /// <summary>
    /// WI-A5-35: faction PlayerTemplate names, one player slot each. Names are PlayerTemplate
    /// block names in the installed game ("FactionAmerica"); each must resolve there for the
    /// faction to start.
    /// </summary>
    public IReadOnlyList<string> FactionTemplateNames { get; init; } = ["FactionAmerica", "FactionGLA"];
}

/// <summary>
/// Factory for creating new blank <see cref="EditorMap"/>s from <see cref="NewMapParameters"/>.
/// </summary>
public static class NewMapFactory
{
    public static EditorMap Create(NewMapParameters p)
    {
        var mapFile = MapFile.CreateEmpty(
            (uint)p.PlayableWidth,
            (uint)p.PlayableHeight,
            (uint)p.BorderWidth,
            p.InitialHeight);

        // WI-A5-35: skirmish-playable sides and one start waypoint per player slot, so a new
        // map can host a skirmish once saved.
        mapFile.SidesList = SidesList.CreateSkirmish(p.FactionTemplateNames);
        AddPlayerStartWaypoints(mapFile, p);

        return EditorMap.CreateNew(mapFile);
    }

    private static void AddPlayerStartWaypoints(MapFile mapFile, NewMapParameters p)
    {
        // One start waypoint per faction slot; the neutral and civilian players never start.
        var slotCount = p.FactionTemplateNames.Count;
        var heightMap = new HeightMap(mapFile.HeightMapData);
        for (var slot = 1; slot <= slotCount; slot++)
        {
            var fraction = slotCount == 1 ? 0.5 : 0.25 + 0.5 * (slot - 1) / (slotCount - 1);
            var tileX = (uint)Math.Round(p.BorderWidth + fraction * p.PlayableWidth);
            var tileY = (uint)Math.Round(p.BorderWidth + fraction * p.PlayableHeight);
            var worldX = tileX * HeightMap.HorizontalScale;
            var worldZ = tileY * HeightMap.HorizontalScale;
            var worldY = heightMap.GetHeight((int)tileX, (int)tileY);
            mapFile.ObjectsList.AddObject(
                Waypoint.CreateWaypointMapObject(new System.Numerics.Vector3(worldX, worldY, worldZ), slot, $"Player_{slot}_Start"));
        }
    }
}
