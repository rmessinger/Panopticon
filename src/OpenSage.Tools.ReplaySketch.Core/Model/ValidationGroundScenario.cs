using System.Collections.Generic;
using System.Numerics;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// Configuration for the unit validation ground: the map, roster source, deterministic layout
/// parameters, the target/supply definitions, and the settle/seed values. Engine-free; the host
/// applies it to a live game. See deep dive P9 §2.1, §2.9, §3.
/// </summary>
public sealed class ValidationGroundScenario
{
    public string MapPath { get; set; } = "";

    /// <summary>Faction indices (per <see cref="UnitCatalog"/>) to stand up, one row each.</summary>
    public IReadOnlyList<int> FactionIndices { get; set; } = [2, 3, 4];

    public LayoutConfig Layout { get; set; } = new();

    /// <summary>
    /// Destructible target definition names placed around the shared target cluster. The builder
    /// cycles this list to give every attacking unit its own target (one target per attacker), so
    /// each unit's first-damage completion reflects its own attack rather than a sibling's.
    /// </summary>
    public IReadOnlyList<string> TargetDefinitions { get; set; } = ["AmericaBarracks"];

    /// <summary>Supply-source definition names placed for gather-action units.</summary>
    public IReadOnlyList<string> SupplySourceDefinitions { get; set; } = ["AmericaSupplyCenter"];

    public int SettleFrames { get; set; } = 30;

    public int Seed { get; set; } = 1;
}

/// <summary>Planar layout parameters. The host samples terrain height; the builder is 2-D.</summary>
public sealed class LayoutConfig
{
    public Vector3 TargetClusterCenter { get; set; } = new(500f, 500f, 0f);
    public float TargetClusterRadius { get; set; } = 40f;
    public float UnitSpacing { get; set; } = 25f;
    public float FactionRowOffset { get; set; } = 80f;
    public float FactionRowDistance { get; set; } = 250f;

    /// <summary>
    /// Distance each unit's assigned target is placed in front of it (toward the cluster centre).
    /// Kept short so the measured dispatch-to-first-damage duration is dominated by the unit's
    /// weapon delay, not a long travel leg. See deep dive P9 §2.5.
    /// </summary>
    public float TargetApproachDistance { get; set; } = 90f;
}
