using System.Collections.Generic;

namespace Panopticon.Tools.ReplaySketch.Model.Capture;

/// <summary>
/// Unit content imported through the shared H2a layer before actors spawn. Actors reference the
/// published units as <c>&lt;Source&gt;:&lt;name&gt;</c>, for example
/// <c>ee2:HeavyInfantry1UpgradeEpoch1</c>. Source files resolve against the operator install named by
/// the source's environment variable (<c>EMPIRE_EARTH_II_PATH</c> for <c>ee2</c>).
/// </summary>
public class CaptureContentImport
{
    /// <summary>Content source id; only <c>ee2</c> is supported.</summary>
    public string Source { get; set; } = "ee2";

    /// <summary>
    /// Exact tech-tree row keys published (WI-M2-03), for example
    /// <c>LightMounted1UpgradeEpoch15</c>. The row's UNIT cell names the UnitType. When set, the
    /// full <c>db/Units</c> inventory is discovered and <see cref="UnitTypes"/>,
    /// <see cref="Epoch"/>, and <see cref="DdfPaths"/> are ignored.
    /// </summary>
    public List<string> RowKeys { get; set; } = [];

    /// <summary>Source unit type names to publish.</summary>
    public List<string> UnitTypes { get; set; } = [];

    /// <summary>Tech-tree epoch whose stat row is published.</summary>
    public int Epoch { get; set; } = 1;

    /// <summary>Regional model variant suffix, for example <c>we</c>.</summary>
    public string Region { get; set; } = "we";

    /// <summary>Declared scale from one source distance unit (an EE2 tile) to game units.</summary>
    public double GameUnitsPerSourceDistance { get; set; }

    /// <summary>
    /// Card WI-M3-01: declared scale from one source model unit (an EE2 NIF decimeter) to game
    /// units, applied to the imported draw's world matrix. Null or 0 means no model scaling.
    /// </summary>
    public double? GameUnitsPerSourceModelUnit { get; set; }

    /// <summary>DDF files read for the closure, in load order.</summary>
    public List<string> DdfPaths { get; set; } = [];

    /// <summary>Unsupported gameplay mechanics the run accepts leaving out; each is reported.</summary>
    public List<string> ExcludedMechanics { get; set; } = [];

    /// <summary>Opt-in Generals presentation compatibility: exact imported vehicle name to installed DamageFX name.</summary>
    public Dictionary<string, string> DamageEffects { get; set; } = [];

    /// <summary>
    /// Opt-in Generals presentation compatibility: exact imported unit name to the damage type its
    /// weapons use only for target-effect selection. Damage itself is unchanged.
    /// </summary>
    public Dictionary<string, string> ImpactEffectDamageTypes { get; set; } = [];

    /// <summary>
    /// Card WI-M3-02: authored EE2 rps class to Generals damage type mapping. While non-empty,
    /// every published unit's rps class needs an entry (unmapped classes fail load), weapons
    /// carry the mapped type, and each class materializes an armor set from dbcombat.
    /// </summary>
    public Dictionary<string, string> RpsDamageTypes { get; set; } = [];
}
