#nullable enable

using System.Collections.Generic;
using Panopticon.Logic.Orders;

namespace Panopticon.Logic.AI;

/// <summary>
/// A player's strategic decision making, extracted behind a seam so implementations are
/// swappable (A4 experiment seam). Called at the AI's decision cadence
/// (<see cref="AIPlayer.DefaultDecisionCadenceFrames"/>); reads state via
/// <see cref="AIDecisionContext"/> and returns orders. Must not mutate game state directly:
/// actuation is always orders through the host's dispatch path, keeping every policy
/// record/replay compatible.
/// </summary>
public interface IAIDecisionPolicy
{
    IReadOnlyList<Order> DecideNextOrders(AIDecisionContext context);
}
