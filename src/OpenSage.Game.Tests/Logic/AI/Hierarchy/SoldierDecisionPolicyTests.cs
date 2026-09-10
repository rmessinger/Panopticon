using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.AI.Hierarchy;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using Xunit;

namespace OpenSage.Tests.Logic.AI.Hierarchy;

/// <summary>
/// WI-A4-20: the reference soldier policy maps goals to order-level intents
/// deterministically, and only produces orders; it never mutates state.
/// </summary>
public sealed class SoldierDecisionPolicyTests : MockedGameTest
{
    private static GameObject SpawnUnit(TestGame game)
    {
        var definition = new ObjectDefinition("A4SoldierPolicyUnit");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        return game.GameLogic.CreateObject(definition, game.PlayerManager.Players[0]);
    }

    [Fact]
    public void GoalWithTarget_EmitsAttackOrder()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game);
        var policy = new RuleBasedSoldierDecisionPolicy();
        var target = new ObjectId(777u);

        var order = policy.DecideOrder(new SoldierDecisionContext(unit, 3, new SoldierGoal(TargetId: target)));

        Assert.NotNull(order);
        Assert.Equal(OrderType.ForceAttackObject, order.OrderType);
        Assert.Equal(3, order.PlayerIndex);
    }

    [Fact]
    public void GoalWithDestination_EmitsMoveOrder()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game);
        var policy = new RuleBasedSoldierDecisionPolicy();
        var destination = new Vector3(120f, 40f, 0f);

        var order = policy.DecideOrder(
            new SoldierDecisionContext(unit, 5, new SoldierGoal(Destination: destination)));

        Assert.NotNull(order);
        Assert.Equal(OrderType.MoveTo, order.OrderType);
        Assert.Equal(5, order.PlayerIndex);
    }

    [Fact]
    public void TargetGoalWinsOverDestination()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game);
        var policy = new RuleBasedSoldierDecisionPolicy();

        var order = policy.DecideOrder(new SoldierDecisionContext(
            unit,
            2,
            new SoldierGoal(TargetId: new ObjectId(9u), Destination: new Vector3(10f, 10f, 0f))));

        Assert.Equal(OrderType.ForceAttackObject, order!.OrderType);
    }

    [Fact]
    public void NoGoalOrNullGoal_Holds_NoOrder()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game);
        var policy = new RuleBasedSoldierDecisionPolicy();

        Assert.Null(policy.DecideOrder(new SoldierDecisionContext(unit, 1, null)));
        Assert.Null(policy.DecideOrder(new SoldierDecisionContext(unit, 1, new SoldierGoal())));
    }

    [Fact]
    public void SameContext_ProducesIdenticalOrders()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var unit = SpawnUnit(game);
        var policy = new RuleBasedSoldierDecisionPolicy();
        var context = new SoldierDecisionContext(unit, 4, new SoldierGoal(TargetId: new ObjectId(6u)));

        var first = policy.DecideOrder(context);
        var second = policy.DecideOrder(context);

        Assert.Equal(first!.OrderType, second!.OrderType);
        Assert.Equal(first.PlayerIndex, second.PlayerIndex);
    }
}
