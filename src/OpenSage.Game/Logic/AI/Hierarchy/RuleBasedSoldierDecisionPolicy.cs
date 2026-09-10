#nullable enable

using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;

namespace OpenSage.Logic.AI.Hierarchy;

/// <summary>
/// Reference soldier policy (A4-3b): translates a goal into the order-level intent the
/// per-unit AI states would pick — attack the goal's target when it has one, otherwise move
/// toward the goal's destination, otherwise hold. Locomotion, targeting, and weapon
/// actuation stay in the engine's per-unit update path.
/// </summary>
public sealed class RuleBasedSoldierDecisionPolicy : ISoldierDecisionPolicy
{
    public Order? DecideOrder(in SoldierDecisionContext context)
    {
        if (context.Goal is not { } goal)
        {
            return null;
        }

        if (goal.TargetId is { IsValid: true } target)
        {
            return Order.CreateAttackObject(context.OwnerPlayerIndex, target, force: true);
        }

        if (goal.Destination is { } destination)
        {
            return Order.CreateMoveOrder(context.OwnerPlayerIndex, destination);
        }

        return null;
    }
}
