using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// The deterministic output of <see cref="Services.ValidationGroundBuilder"/>: every roster unit
/// placed, plus the target/supply cluster, with target assignment by index. Engine-free; the host
/// applies it through <c>GameLogic.CreateObject</c>. See deep dive P9 §3, §6.1.
/// </summary>
public sealed class SpawnPlan
{
    public IReadOnlyList<UnitPlacement> UnitPlacements { get; init; } = [];
    public IReadOnlyList<TargetPlacement> TargetPlacements { get; init; } = [];
    public IReadOnlyList<TargetPlacement> SupplySourcePlacements { get; init; } = [];
}

public sealed record UnitPlacement(
    string DefinitionName,
    int FactionIndex,
    int PlayerIndex,
    Vector3 Position,
    PrimaryAction Action,
    int TargetIndex,
    int RosterIndex);

public sealed record TargetPlacement(
    string DefinitionName,
    Vector3 Position);
