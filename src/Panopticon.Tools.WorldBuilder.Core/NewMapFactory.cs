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

    /// <summary>Initial flat ground height (raw sample value: 0–255, or 0–65535 with <see cref="Use16BitElevation"/>).</summary>
    public required ushort InitialHeight { get; init; }

    /// <summary>Number of border tiles on each edge (default 6).</summary>
    public int BorderWidth { get; init; } = 6;

    /// <summary>Stores elevations as 16-bit samples (HeightMapData version 5) instead of 8-bit.</summary>
    public bool Use16BitElevation { get; init; }

    /// <summary>
    /// WI-A5-35: faction PlayerTemplate names, one player slot each. Names are PlayerTemplate
    /// block names in the installed game ("FactionAmerica"); each must resolve there for the
    /// faction to start.
    /// </summary>
    public IReadOnlyList<string> FactionTemplateNames { get; init; } = ["FactionAmerica", "FactionGLA"];
}

/// <summary>Convenience size and elevation choice for the new-map wizard; custom sizes are also accepted.</summary>
public sealed record NewMapPreset(string Name, int PlayableWidth, int PlayableHeight, bool Use16BitElevation);

/// <summary>
/// Factory for creating new blank <see cref="EditorMap"/>s from <see cref="NewMapParameters"/>.
/// </summary>
public static class NewMapFactory
{
    public const int MinimumPlayableSize = 10;
    public const int MinimumBorderWidth = 2;

    /// <summary>
    /// Bytes per lattice sample allocated by a new map: elevations 2, Tiles 2, Blends, ThreeWayBlends and
    /// CliffTextures 4 each, Impassability 1, HeightMap Normals 12.
    /// </summary>
    public const long EstimatedBytesPerLatticeCell = 29;

    public static IReadOnlyList<NewMapPreset> Presets { get; } =
    [
        new("Standard", 200, 200, Use16BitElevation: false),
        new("Large", 512, 512, Use16BitElevation: true),
        new("Very Large", 1024, 1024, Use16BitElevation: true),
        new("Huge", 2048, 2048, Use16BitElevation: true),
    ];

    /// <summary>
    /// Wizard input validation: minimum playable size and border, 8-bit initial height, then the allocation limits.
    /// </summary>
    public static bool TryValidate(NewMapParameters parameters, out string error)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.PlayableWidth < MinimumPlayableSize)
        {
            error = $"Width must be at least {MinimumPlayableSize}.";
            return false;
        }

        if (parameters.PlayableHeight < MinimumPlayableSize)
        {
            error = $"Height must be at least {MinimumPlayableSize}.";
            return false;
        }

        if (parameters.BorderWidth < MinimumBorderWidth)
        {
            error = $"Border width must be at least {MinimumBorderWidth}.";
            return false;
        }

        if (!parameters.Use16BitElevation && parameters.InitialHeight > byte.MaxValue)
        {
            error = $"Initial height must be 0 to {byte.MaxValue} without 16-bit elevation.";
            return false;
        }

        return TryValidateAllocation(parameters, out error);
    }

    /// <summary>
    /// Allocation safety: positive playable sides, non-negative border, and the lattice (playable + 2 * border + 1
    /// per axis) within <see cref="int.MaxValue"/> per axis and in total cells.
    /// </summary>
    private static bool TryValidateAllocation(NewMapParameters parameters, out string error)
    {
        if (parameters.PlayableWidth < 1)
        {
            error = "Playable width must be at least 1.";
            return false;
        }

        if (parameters.PlayableHeight < 1)
        {
            error = "Playable height must be at least 1.";
            return false;
        }

        if (parameters.BorderWidth < 0)
        {
            error = "Border width must not be negative.";
            return false;
        }

        var latticeWidth = LatticeSide(parameters.PlayableWidth, parameters.BorderWidth);
        var latticeHeight = LatticeSide(parameters.PlayableHeight, parameters.BorderWidth);

        if (latticeWidth > int.MaxValue || latticeHeight > int.MaxValue)
        {
            error = $"Lattice {latticeWidth} x {latticeHeight} exceeds the per-axis array limit of {int.MaxValue} samples.";
            return false;
        }

        if (latticeWidth * latticeHeight > int.MaxValue)
        {
            error = $"Lattice {latticeWidth} x {latticeHeight} exceeds the total array limit of {int.MaxValue} samples.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Estimate of the resident bytes a new map allocates for its lattice. Inputs are expected to have passed
    /// <see cref="TryValidate"/>.
    /// </summary>
    public static long EstimateResidentBytes(int playableWidth, int playableHeight, int borderWidth)
    {
        var latticeCells = LatticeSide(playableWidth, borderWidth) * LatticeSide(playableHeight, borderWidth);
        return latticeCells * EstimatedBytesPerLatticeCell;
    }

    public static EditorMap Create(NewMapParameters p)
    {
        if (!TryValidateAllocation(p, out var error))
        {
            throw new ArgumentException(error, nameof(p));
        }

        var mapFile = MapFile.CreateEmpty(
            (uint)p.PlayableWidth,
            (uint)p.PlayableHeight,
            (uint)p.BorderWidth,
            p.InitialHeight,
            p.Use16BitElevation);

        // WI-A5-35: skirmish-playable sides and one start waypoint per player slot, so a new
        // map can host a skirmish once saved.
        mapFile.SidesList = SidesList.CreateSkirmish(p.FactionTemplateNames);
        AddPlayerStartWaypoints(mapFile, p);

        return EditorMap.CreateNew(mapFile);
    }

    private static long LatticeSide(int playable, int borderWidth) => (long)playable + 2L * borderWidth + 1;

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
