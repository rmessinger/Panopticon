using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Orders;
using Xunit;

namespace Panopticon.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-M4-03: the hierarchy actuates its orders through selections. A squad with squad-uniform
/// goals (the reference commander) emits one SetSelection of its decided members followed by
/// the single order; a soldier rank that decides per member gets one selection per member, so
/// every order reaches exactly the units that decided it. End-to-end dispatch is proven by the
/// scaled army-AI probe test (MassBattleCapacityProbeScenarioTests).
/// </summary>
public sealed class SoldierOrderActuationTests : MockedGameTest
{
    [Fact]
    public void UniformSquadOrder_IsPrecededByOneSetSelectionOfItsMembers()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var unitA = HierarchyTestPlayers.SpawnUnit(game, player, "M4ActuationUnitA", buildCost: 100f);
        var unitB = HierarchyTestPlayers.SpawnUnit(game, player, "M4ActuationUnitB", buildCost: 200f);

        var policy = new HierarchicalAIDecisionPolicy(
            new RuleBasedGeneralPolicy(),
            new RuleBasedCommanderDecisionPolicy(),
            new RuleBasedSoldierDecisionPolicy());

        var orders = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 1));

        // Both units share one squad whose goals are squad-uniform (defend: move to centroid),
        // so exactly one selection covers the decided members, followed by the one order.
        var selection = Assert.Single(orders, order => order.OrderType == OrderType.SetSelection);
        var selectedIds = selection.Arguments.Skip(1).Select(argument => argument.Value.ObjectId).ToHashSet();
        Assert.Equal(
            new[] { unitA.Id, unitB.Id }.ToHashSet(),
            selectedIds);

        var move = Assert.Single(orders, order => order.OrderType == OrderType.MoveTo);
        var selectionIndex = orders.ToList().IndexOf(selection);
        Assert.Equal(selectionIndex + 1, orders.ToList().IndexOf(move));
    }

    [Fact]
    public void PerMemberDecisions_GetOneSetSelectionPerMember()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var unitA = HierarchyTestPlayers.SpawnUnit(game, player, "M4ActuationUnitC", buildCost: 100f);
        var unitB = HierarchyTestPlayers.SpawnUnit(game, player, "M4ActuationUnitD", buildCost: 200f);

        var policy = new HierarchicalAIDecisionPolicy(
            new RuleBasedGeneralPolicy(),
            new RuleBasedCommanderDecisionPolicy(),
            new PerMemberSoldierPolicy());

        var orders = policy.DecideNextOrders(new AIDecisionContext(player, ownerPlayerIndex: 2, decisionTickCount: 1));

        var selections = orders.Where(order => order.OrderType == OrderType.SetSelection).ToList();
        Assert.Equal(2, selections.Count);
        var selectedIds = selections
            .SelectMany(selection => selection.Arguments.Skip(1).Select(argument => argument.Value.ObjectId))
            .ToHashSet();
        Assert.Equal(
            new[] { unitA.Id, unitB.Id }.ToHashSet(),
            selectedIds);

        // Each selection is immediately followed by that member's own order.
        for (var i = 0; i < orders.Count - 1; i++)
        {
            if (orders[i].OrderType != OrderType.SetSelection)
            {
                continue;
            }

            Assert.Equal(OrderType.MoveTo, orders[i + 1].OrderType);
        }
    }

    private sealed class PerMemberSoldierPolicy : ISoldierDecisionPolicy
    {
        public Order? DecideOrder(in SoldierDecisionContext context)
        {
            return Order.CreateMoveOrder(
                context.OwnerPlayerIndex,
                new System.Numerics.Vector3(context.Unit.Id.Index, 0f, 0f));
        }
    }
}
