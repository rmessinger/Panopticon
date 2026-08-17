using System.Collections.Generic;
using System.Numerics;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// Configuration for the unit validation ground: the map, roster source, deterministic layout
/// parameters, the target/supply definitions, and the settle/seed values. Engine-free; the host
/// applies it to a live game. See deep dive P9 §2.1, §2.9, §3. JSON persistence lives in
/// <see cref="Services.ValidationGroundScenarioStore"/> so layouts hand-edited in the ReplaySketch
/// map editor can be consumed by the TestHarness run.
/// </summary>
public sealed class ValidationGroundScenario
{
    public string MapPath { get; set; } = "";

    /// <summary>
    /// SAGE game the run targets (a <c>SageGame</c> value name, e.g. <c>CncGenerals</c>). Set by the
    /// TestHarness from its config so the builder can skip roster entries that do not exist in that
    /// game (see <see cref="UnitOption.ZeroHourOnly"/>) instead of reporting resolution gaps.
    /// </summary>
    public string Game { get; set; } = "CncGenerals";

    /// <summary>Faction indices (per <see cref="UnitCatalog"/>) to stand up, one row each.</summary>
    public IReadOnlyList<int> FactionIndices { get; set; } = [2, 3, 4];

    public LayoutConfig Layout { get; set; } = new();

    /// <summary>
    /// Destructible target definition names placed around the shared target cluster. The builder
    /// cycles this list to give every attacking unit its own target (one target per attacker), so
    /// each unit's first-damage completion reflects its own attack rather than a sibling's.
    /// </summary>
    public IReadOnlyList<string> TargetDefinitions { get; set; } = ["CarAsian1"];

    /// <summary>Supply-source definition names placed for gather-action units.</summary>
    public IReadOnlyList<string> SupplySourceDefinitions { get; set; } = ["AmericaSupplyCenter"];

    /// <summary>
    /// HP each attack target is standardized to at spawn. Attack completion is target destruction,
    /// so a target at its native HP (a structure's is ~1000) makes the metric HP-dominated and
    /// unfinishable for low-DPS units within the frame budget. A standardized low HP keeps the
    /// measurement weapon-differentiated (shots-to-kill from real INI damage and fire rate) and
    /// feasible for the whole roster. Zero/null leaves the target at its native HP.
    /// </summary>
    public float TargetHp { get; set; } = 30f;

    public int SettleFrames { get; set; } = 30;

    public int Seed { get; set; } = 1;

    /// <summary>
    /// Per-object position overrides. <see cref="Layout"/> derives every unit and target position;
    /// an entry here replaces that derived position for a single placement, so the knobs keep driving
    /// the bulk of the layout while individual objects can be hand-placed. Consulted by
    /// <see cref="Services.ValidationGroundBuilder"/>. Empty by default (pure knob-derived layout).
    /// </summary>
    public List<PlacementOverride> PlacementOverrides { get; set; } = new();
}

/// <summary>
/// Planar layout parameters for the interleaved row formation. The host samples terrain height;
/// the builder is 2-D. The formation is six parallel rows: for each faction a unit row then its
/// target row (units, targets, units, targets, ...), running east-west and centred on
/// <see cref="TargetClusterCenter"/>. Each unit's target sits directly ahead (perpendicular to the
/// row) so units never interleave with their own or a neighbour faction's targets.
/// </summary>
/// <remarks>
/// These are pure POCO slots; the standard values are NOT defined here. The authoritative
/// configuration lives in <c>Profiles/unit-validation-ground-scenario.json</c>, loaded via
/// <see cref="Services.ValidationGroundScenarioStore.Standard"/>. The zero defaults exist only so an
/// empty <see cref="LayoutConfig"/> is not mistaken for a real formation.
/// </remarks>
public sealed class LayoutConfig
{
    /// <summary>Centre of the formation; the interleaved row stack is centred on this point.</summary>
    public Vector3 TargetClusterCenter { get; set; }

    /// <summary>Offset of the supply sources east of the formation centre.</summary>
    public float TargetClusterRadius { get; set; }

    /// <summary>Spacing between units within a row, and between targets within a target row.</summary>
    public float UnitSpacing { get; set; }

    /// <summary>Gap between one faction's target row and the next faction's unit row.</summary>
    public float FactionRowOffset { get; set; }

    /// <summary>
    /// Gap between a faction's unit row and its target row, i.e. the unit-to-target distance. Kept
    /// short so the measured dispatch-to-first-damage duration is dominated by the unit's weapon
    /// delay, not a long travel leg, and within weapon range. See deep dive P9 §2.5.
    /// </summary>
    public float TargetApproachDistance { get; set; }
}
