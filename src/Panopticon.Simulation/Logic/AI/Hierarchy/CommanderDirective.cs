#nullable enable

using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>The posture a commander's squad should adopt.</summary>
public enum DirectivePosture
{
    Attack = 0,
    Defend = 1,
    Regroup = 2,
}

/// <summary>
/// The general's directive to one squad's commander (A4-3): data, not an order. A target
/// reference when one exists, otherwise a target region; the commander translates it into a
/// per-soldier <see cref="SoldierGoal"/>.
/// </summary>
public sealed record CommanderDirective(
    DirectivePosture Posture = DirectivePosture.Attack,
    ObjectId? TargetId = null,
    Vector3? TargetRegion = null);
