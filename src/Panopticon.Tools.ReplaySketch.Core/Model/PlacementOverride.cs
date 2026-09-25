using System.Numerics;

namespace Panopticon.Tools.ReplaySketch.Model;

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

    /// <summary>
    /// Planar world position, or null to keep the layout-derived position. Z is ignored
    /// (terrain-sampled at spawn). Null lets an override carry only
    /// <see cref="TargetDistance"/> (e.g. a proximity pin for a contact-range unit whose cluster
    /// position is terrain-derived and cannot be pre-pinned by hand).
    /// </summary>
    public float? X { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public float? Y { get; set; }

    /// <summary>
    /// Overrides <see cref="LayoutConfig.TargetApproachDistance"/> for this unit's linked target
    /// only (unit-side override). Null keeps the layout standoff.
    /// </summary>
    public float? TargetDistance { get; set; }

    /// <summary>True when this override pins a position (rather than only a target distance).</summary>
    public bool HasPosition => X.HasValue && Y.HasValue;

    /// <summary>Convenience view over <see cref="X"/>/<see cref="Y"/> as a planar vector.</summary>
    public Vector2 PlanarPosition => new(X ?? 0f, Y ?? 0f);
}
