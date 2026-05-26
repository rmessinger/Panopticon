using System;
using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

public sealed class PlayerSlotConfig
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Index into the game's PlayerTemplates list.
    /// USA = 2, GLA = 4 (Generals defaults).
    /// </summary>
    public int FactionIndex { get; set; }

    public sbyte Color { get; set; }

    /// <summary>1-based start position on the map (matches Player_N_Start waypoint naming).</summary>
    public int StartPosition { get; set; }

    /// <summary>1-based team number.</summary>
    public int Team { get; set; }

    public List<ActionEntry> Actions { get; set; } = new();

    /// <summary>
    /// Moves the action at <paramref name="fromIndex"/> to <paramref name="toIndex"/>,
    /// shifting intervening actions to fill the gap.
    /// </summary>
    public void MoveAction(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        if (fromIndex < 0 || fromIndex >= Actions.Count) return;
        toIndex = Math.Clamp(toIndex, 0, Actions.Count - 1);

        var action = Actions[fromIndex];
        Actions.RemoveAt(fromIndex);
        Actions.Insert(toIndex, action);
    }
}
