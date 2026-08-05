using System.Numerics;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// A per-object position override for one generated validation-ground placement. The
/// <see cref="Services.ValidationGroundBuilder"/> derives every unit and target position from
/// <see cref="LayoutConfig"/>; an entry here replaces that derived position for a single placement,
/// letting the layout be hand-edited (e.g. via the ReplaySketch map editor) while the knobs continue
/// to drive the rest. Z is planar: the host samples terrain height at spawn.
/// </summary>
public sealed class PlacementOverride
{
    /// <summary>Faction index (per <see cref="UnitCatalog"/>) of the row the placement belongs to.</summary>
    public int FactionIndex { get; set; }

    /// <summary>Index of the unit within <c>UnitCatalog.ForFaction(FactionIndex)</c>.</summary>
    public int RosterIndex { get; set; }

    /// <summary>
    /// False to override the unit itself; true to override that unit's linked attack target (the one
    /// at <see cref="UnitPlacement.TargetIndex"/>).
    /// </summary>
    public bool IsTarget { get; set; }

    /// <summary>Planar world position. Z is ignored (terrain-sampled at spawn).</summary>
    public float X { get; set; }

    /// <summary>Planar world position. Z is ignored (terrain-sampled at spawn).</summary>
    public float Y { get; set; }

    /// <summary>Convenience view over <see cref="X"/>/<see cref="Y"/> as a planar vector.</summary>
    public Vector2 PlanarPosition => new(X, Y);
}
