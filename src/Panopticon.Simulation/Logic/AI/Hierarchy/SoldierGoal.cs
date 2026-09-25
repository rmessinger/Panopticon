#nullable enable

using System.Numerics;
using Panopticon.Logic.Object;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>The stance a soldier should hold while pursuing its goal.</summary>
public enum GoalStance
{
    Hold = 0,
    Advance = 1,
    Retreat = 2,
}

/// <summary>
/// A commander's goal for one soldier (A4-3): data, not an order. At most one of
/// <see cref="TargetId"/>/<see cref="Destination"/> is set; the soldier rank decides the
/// concrete <see cref="Order"/> from the goal.
/// </summary>
public sealed record SoldierGoal(
    ObjectId? TargetId = null,
    Vector3? Destination = null,
    GoalStance Stance = GoalStance.Advance);
