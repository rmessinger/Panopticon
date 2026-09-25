using Panopticon.Data.Map;

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

        return EditorMap.CreateNew(mapFile);
    }
}
