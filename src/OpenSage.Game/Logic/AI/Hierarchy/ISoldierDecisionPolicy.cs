#nullable enable

using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;

namespace OpenSage.Logic.AI.Hierarchy;

/// <summary>
/// The soldier rank's per-object decision input (A4-3b): the unit, its owner's player
/// index, and the goal its commander issued (null before the first commander tick). Local
/// perception inputs beyond the goal are the A4-3e networks' concern, not the seam's.
/// </summary>
public readonly record struct SoldierDecisionContext(
    GameObject Unit,
    int OwnerPlayerIndex,
    SoldierGoal? Goal);

/// <summary>
/// The leaf rank of the command hierarchy: decides the concrete engine order for one unit
/// from its goal. Must not mutate game state directly; the returned order flows through the
/// single actuation path (AIPlayer.EnqueueOrders -> DispatchOrders -> OrderProcessor).
/// </summary>
public interface ISoldierDecisionPolicy
{
    Order? DecideOrder(in SoldierDecisionContext context);
}
