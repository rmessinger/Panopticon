using System;
using System.Linq;
using System.Numerics;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ReplaySketchTests
{
    [Fact]
    public void DefaultScenario_UsaActions_AreInExpectedOrder()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var usa = scenario.Players[0];

        Assert.Equal(4, usa.Actions.Count);
        Assert.Equal(ActionType.Construct, usa.Actions[0].Type);
        Assert.Equal(ActionType.GatherResources, usa.Actions[1].Type);
        Assert.Equal(ActionType.RecruitBasicUnit, usa.Actions[2].Type);
        Assert.Equal(ActionType.AttackEnemyBase, usa.Actions[3].Type);
    }

    // ── CommandSpec tests ────────────────────────────────────────────────────────
    // Tests for the new CommandSpec model covering all OrderArgumentType variants
    // and legacy round-trip.
    [Fact]
    public void IntegerArgument_ToOrder_ProducesIntegerArg()
    {
        var cmd = CommandSpec.CreateOpen(OrderType.SetSelection,
            new IntegerCommandArgument(42));
        var ctx = BuildStubContext();

        var order = cmd.ToOrder(1, ctx, new Random());

        Assert.NotEmpty(order.Arguments);
        Assert.Equal(OrderArgumentType.Integer, order.Arguments[0].ArgumentType);
        Assert.Equal(42, order.Arguments[0].Value.Integer);
    }

    [Fact]
    public void FloatArgument_ToOrder_ProducesFloatArg()
    {
        var cmd = CommandSpec.CreateOpen(OrderType.Unknown1,
            new FloatCommandArgument(3.14f));
        var ctx = BuildStubContext();

        var order = cmd.ToOrder(1, ctx, new Random());

        Assert.NotEmpty(order.Arguments);
        Assert.Equal(OrderArgumentType.Float, order.Arguments[0].ArgumentType);
        Assert.Equal(3.14f, order.Arguments[0].Value.Float, 2);
    }

    [Fact]
    public void BooleanArgument_ToOrder_ProducesBooleanArg()
    {
        var cmd = CommandSpec.CreateOpen(OrderType.Unknown1,
            new BooleanCommandArgument(true));
        var ctx = BuildStubContext();

        var order = cmd.ToOrder(1, ctx, new Random());

        Assert.NotEmpty(order.Arguments);
        Assert.Equal(OrderArgumentType.Boolean, order.Arguments[0].ArgumentType);
        Assert.True(order.Arguments[0].Value.Boolean);
    }

    [Fact]
    public void ObjectIdArgument_ToOrder_ProducesObjectIdArg()
    {
        var cmd = CommandSpec.CreateOpen(OrderType.Unknown1,
            new ObjectIdCommandArgument(5));
        var ctx = BuildStubContext();

        var order = cmd.ToOrder(1, ctx, new Random());

        Assert.NotEmpty(order.Arguments);
        Assert.Equal(OrderArgumentType.ObjectId, order.Arguments[0].ArgumentType);
        Assert.Equal(new ObjectId(5), order.Arguments[0].Value.ObjectId);
    }

    [Fact]
    public void PositionArgument_ToOrder_ProducesPositionArg()
    {
        var cmd = CommandSpec.CreateOpen(OrderType.Unknown1,
            new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)));
        var ctx = BuildStubContext();

        var order = cmd.ToOrder(1, ctx, new Random());

        Assert.NotEmpty(order.Arguments);
        Assert.Equal(OrderArgumentType.Position, order.Arguments[0].ArgumentType);
        // Position should be roughly at map center
        Assert.InRange(order.Arguments[0].Value.Position.X, 490, 510);
        Assert.InRange(order.Arguments[0].Value.Position.Y, 490, 510);
    }

    [Fact]
    public void LegacyConstruct_RoundTrip_PreservesType()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var construct = scenario.Players[0].Actions[0];

        Assert.Equal(ActionType.Construct, construct.Type);
    }

    [Fact]
    public void ActionEntry_JSON_RoundTrip_WithOpenCommand()
    {
        var original = new ActionEntry(
            "Test Open Command",
            CommandSpec.CreateOpen(OrderType.Unknown1,
                new IntegerCommandArgument(99),
                new FloatCommandArgument(2.71f)),
            null,
            new FixedTiming(100));

        var json = System.Text.Json.JsonSerializer.Serialize(original,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ActionEntry>(json)!;

        Assert.Equal(original.Label, deserialized.Label);
        Assert.Null(deserialized.Type); // Open commands have no legacy ActionType
        Assert.Equal(OrderType.Unknown1, deserialized.Command.OrderType);
        Assert.Equal(2, deserialized.Command.Arguments.Count);
    }

    private static PositionContext BuildStubContext()
    {
        return new()
        {
            GetLandmark = _ => new Vector3(500, 500, 0),
            BaseRadiusWorldUnits = 120f,
            SampleHeight = (_, _) => 0f,
            ExtentMin = Vector3.Zero,
            ExtentMax = new Vector3(1000, 1000, 0),
        };
    }

    [Fact]
    public void DefaultScenario_GlaActions_AreInExpectedOrder()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var gla = scenario.Players[1];

        Assert.Equal(4, gla.Actions.Count);
        Assert.Equal(ActionType.Construct, gla.Actions[0].Type);
        Assert.Equal(ActionType.GatherResources, gla.Actions[1].Type);
        Assert.Equal(ActionType.RecruitBasicUnit, gla.Actions[2].Type);
        Assert.Equal(ActionType.AttackEnemyBase, gla.Actions[3].Type);
    }

    [Fact]
    public void DefaultScenario_GatherResources_TriggersOnConstructDispatched()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();

        foreach (var player in scenario.Players)
        {
            var construct = player.Actions[0];
            var gather = player.Actions[1];
            Assert.Equal(ActionType.GatherResources, gather.Type);

            var timing = Assert.IsType<AfterActionTiming>(gather.Timing);
            Assert.Equal(construct.Id, timing.TriggerActionId);
            Assert.Equal(ActionPhase.Dispatched, timing.TriggerPhase);
        }
    }

    [Fact]
    public void DefaultScenario_RecruitBasicUnit_TriggersOnConstructCompleted()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();

        foreach (var player in scenario.Players)
        {
            var construct = player.Actions[0];
            var recruit = player.Actions[2];
            Assert.Equal(ActionType.RecruitBasicUnit, recruit.Type);

            var timing = Assert.IsType<AfterActionTiming>(recruit.Timing);
            Assert.Equal(construct.Id, timing.TriggerActionId);
            Assert.Equal(ActionPhase.Completed, timing.TriggerPhase);
        }
    }

    [Fact]
    public void DefaultScenario_RecruitBasicUnit_HasUnitDefinitionName()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();

        var usaRecruit = scenario.Players[0].Actions[2];
        Assert.Equal(ActionType.RecruitBasicUnit, usaRecruit.Type);
        Assert.Equal("AmericaInfantryRanger", usaRecruit.UnitDefinitionName);

        var glaRecruit = scenario.Players[1].Actions[2];
        Assert.Equal(ActionType.RecruitBasicUnit, glaRecruit.Type);
        Assert.Equal("GLAInfantryRebel", glaRecruit.UnitDefinitionName);
    }

    [Fact]
    public void DefaultScenario_FixedTimedActions_HavePositiveFrameOffsets()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var rng = new Random(0);

        foreach (var player in scenario.Players)
        {
            foreach (var action in player.Actions)
            {
                if (action.Timing is FixedTiming ft)
                {
                    Assert.True(ft.Frames > 0,
                        $"{player.Name}: action {action.Type} must have a positive fixed frame offset.");
                }
            }
        }
    }

    [Fact]
    public void DefaultScenario_PrebakedActions_HaveMonotonicallyIncreasingFrames()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var rng = new Random(0);

        foreach (var player in scenario.Players)
        {
            uint cumulative = 0;
            uint previous = 0;
            foreach (var action in player.Actions)
            {
                // Deferred actions (AfterActionTiming) return 0 from Resolve and are injected
                // at runtime rather than baked into the replay — skip them in this check.
                if (action.Timing is AfterActionTiming)
                {
                    continue;
                }

                cumulative += action.Timing.Resolve(rng);
                Assert.True(cumulative > previous,
                    $"{player.Name}: action {action.Type} must fire at a later frame than the previous pre-baked action.");
                previous = cumulative;
            }
        }
    }
}

/// <summary>
/// Tests for <see cref="ActionTreeLayoutEngine"/> — the platform-free layout math that drives
/// node placement and hit-test bounds in the ReplaySketch action tree view.
/// These tests ensure that clicking a node in the UI will correctly hit-test to the right
/// action, which is the precondition for the selection gesture to work.
/// </summary>
public class ActionTreeLayoutEngineTests
{
    private static ActionEntry MakeFixed(ActionType type, uint frames = 90) =>
        new(type.ToString(), type, null, new FixedTiming(frames));

    private static ActionEntry MakeAfter(ActionType type, ActionEntry parent, ActionPhase phase = ActionPhase.Completed) =>
        new(type.ToString(), type, null, new AfterActionTiming(parent.Id, phase));

    [Fact]
    public void EmptyActions_ReturnsEmptyLayout()
    {
        var result = ActionTreeLayoutEngine.ComputeLayout([]);
        Assert.Empty(result);
    }

    [Fact]
    public void SingleRootAction_IsInRow0()
    {
        var action = MakeFixed(ActionType.Construct);
        var result = ActionTreeLayoutEngine.ComputeLayout([action]);

        Assert.Single(result);
        Assert.Equal(0, result[0].Row);
    }

    [Fact]
    public void ChildAction_IsInRowBelowParent()
    {
        var parent = MakeFixed(ActionType.Construct);
        var child = MakeAfter(ActionType.GatherResources, parent);
        var result = ActionTreeLayoutEngine.ComputeLayout([parent, child]);

        var parentNode = result.Single(n => n.Action == parent);
        var childNode = result.Single(n => n.Action == child);

        Assert.Equal(0, parentNode.Row);
        Assert.Equal(1, childNode.Row);
    }

    [Fact]
    public void ChainOfThree_HasThreeDistinctRows()
    {
        var a = MakeFixed(ActionType.Construct);
        var b = MakeAfter(ActionType.GatherResources, a);
        var c = MakeAfter(ActionType.RecruitBasicUnit, b);

        var result = ActionTreeLayoutEngine.ComputeLayout([a, b, c]);

        var rows = result.Select(n => n.Row).OrderBy(r => r).ToArray();
        Assert.Equal([0, 1, 2], rows);
    }

    [Fact]
    public void DefaultScenarioLayout_AllFourActionsPlaced()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var result = ActionTreeLayoutEngine.ComputeLayout(scenario.Players[0].Actions);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void DefaultScenarioLayout_Construct_IsRoot()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var result = ActionTreeLayoutEngine.ComputeLayout(scenario.Players[0].Actions);

        var barracks = result.Single(n => n.Action.Type == ActionType.Construct);
        Assert.Equal(0, barracks.Row);
    }

    [Fact]
    public void NodeCenters_AreInsideTheirOwnHitBounds()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var result = ActionTreeLayoutEngine.ComputeLayout(scenario.Players[0].Actions);

        foreach (var node in result)
        {
            Assert.True(
                ActionTreeLayoutEngine.HitTest(node, node.CenterX, node.CenterY),
                $"Node {node.Action.Type}: centre ({node.CenterX},{node.CenterY}) must be inside its own hit bounds.");
        }
    }

    [Fact]
    public void HitTest_NodeEdgePoints_AreInside()
    {
        var node = ActionTreeLayoutEngine.ComputeLayout(
            [MakeFixed(ActionType.Construct)])[0];

        var hw = ActionTreeLayoutEngine.NodeW / 2f - 1f;
        var hh = ActionTreeLayoutEngine.NodeH / 2f - 1f;

        Assert.True(ActionTreeLayoutEngine.HitTest(node, node.CenterX - hw, node.CenterY));
        Assert.True(ActionTreeLayoutEngine.HitTest(node, node.CenterX + hw, node.CenterY));
        Assert.True(ActionTreeLayoutEngine.HitTest(node, node.CenterX, node.CenterY - hh));
        Assert.True(ActionTreeLayoutEngine.HitTest(node, node.CenterX, node.CenterY + hh));
    }

    [Fact]
    public void HitTest_PointOutsideNode_ReturnsFalse()
    {
        var node = ActionTreeLayoutEngine.ComputeLayout(
            [MakeFixed(ActionType.Construct)])[0];

        Assert.False(ActionTreeLayoutEngine.HitTest(node, node.CenterX - ActionTreeLayoutEngine.NodeW, node.CenterY));
        Assert.False(ActionTreeLayoutEngine.HitTest(node, node.CenterX, node.CenterY - ActionTreeLayoutEngine.NodeH));
    }

    [Fact]
    public void AdjacentNodes_DoNotOverlapHitBounds()
    {
        var a = MakeFixed(ActionType.Construct);
        var b = MakeFixed(ActionType.AttackEnemyBase);
        var result = ActionTreeLayoutEngine.ComputeLayout([a, b]);

        var nodeA = result[0];
        var nodeB = result[1];

        // Centre of nodeA must not hit nodeB and vice-versa.
        Assert.False(ActionTreeLayoutEngine.HitTest(nodeB, nodeA.CenterX, nodeA.CenterY));
        Assert.False(ActionTreeLayoutEngine.HitTest(nodeA, nodeB.CenterX, nodeB.CenterY));
    }

    [Fact]
    public void ToggleSelection_NullCurrent_SelectsClicked()
    {
        var node = ActionTreeLayoutEngine.ComputeLayout(
            [MakeFixed(ActionType.Construct)])[0];

        var result = ActionTreeLayoutEngine.ToggleSelection(current: null, clicked: node);
        Assert.Equal(node, result);
    }

    [Fact]
    public void ToggleSelection_SameNode_KeepsSelected()
    {
        var node = ActionTreeLayoutEngine.ComputeLayout(
            [MakeFixed(ActionType.Construct)])[0];

        var result = ActionTreeLayoutEngine.ToggleSelection(current: node, clicked: node);
        Assert.Equal(node, result);
    }

    [Fact]
    public void ToggleSelection_DifferentNode_SelectsNew()
    {
        var nodes = ActionTreeLayoutEngine.ComputeLayout(
            [MakeFixed(ActionType.Construct), MakeFixed(ActionType.AttackEnemyBase)]);

        var result = ActionTreeLayoutEngine.ToggleSelection(current: nodes[0], clicked: nodes[1]);
        Assert.Equal(nodes[1], result);
    }
}

/// <summary>
/// Tests for <see cref="PlayerSlotConfig.MoveAction"/> — the pure model operation that
/// backs the drag-and-drop reorder gesture in the ActionSequenceView.
/// </summary>
public class ActionSequenceDragTests
{
    private static ActionEntry MakeAction(string label, ActionType type) =>
        new(label, type, null, new FixedTiming(90));

    private static PlayerSlotConfig MakePlayer(params string[] labels)
    {
        var player = new PlayerSlotConfig();
        foreach (var label in labels)
            player.Actions.Add(MakeAction(label, ActionType.Construct));
        return player;
    }

    [Fact]
    public void MoveAction_ForwardOneStep_CorrectOrder()
    {
        var player = MakePlayer("A", "B", "C");
        player.MoveAction(0, 1);
        Assert.Equal(["B", "A", "C"], player.Actions.Select(a => a.Label));
    }

    [Fact]
    public void MoveAction_ForwardToEnd_CorrectOrder()
    {
        var player = MakePlayer("A", "B", "C");
        player.MoveAction(0, 2);
        Assert.Equal(["B", "C", "A"], player.Actions.Select(a => a.Label));
    }

    [Fact]
    public void MoveAction_BackwardOneStep_CorrectOrder()
    {
        var player = MakePlayer("A", "B", "C");
        player.MoveAction(2, 1);
        Assert.Equal(["A", "C", "B"], player.Actions.Select(a => a.Label));
    }

    [Fact]
    public void MoveAction_BackwardToStart_CorrectOrder()
    {
        var player = MakePlayer("A", "B", "C");
        player.MoveAction(2, 0);
        Assert.Equal(["C", "A", "B"], player.Actions.Select(a => a.Label));
    }

    [Fact]
    public void MoveAction_SameIndex_NoChange()
    {
        var player = MakePlayer("A", "B", "C");
        player.MoveAction(1, 1);
        Assert.Equal(["A", "B", "C"], player.Actions.Select(a => a.Label));
    }

    [Fact]
    public void MoveAction_OutOfRangeFrom_Ignored()
    {
        var player = MakePlayer("A", "B");
        player.MoveAction(-1, 0);
        player.MoveAction(5, 0);
        Assert.Equal(["A", "B"], player.Actions.Select(a => a.Label));
    }

    [Fact]
    public void MoveAction_ToIndexClampedToEnd_MovesToLast()
    {
        var player = MakePlayer("A", "B", "C");
        player.MoveAction(0, 99);
        Assert.Equal(["B", "C", "A"], player.Actions.Select(a => a.Label));
    }

    [Fact]
    public void MoveAction_ToIndexClampedToStart_MovesToFirst()
    {
        var player = MakePlayer("A", "B", "C");
        player.MoveAction(2, -99);
        Assert.Equal(["C", "A", "B"], player.Actions.Select(a => a.Label));
    }

    [Fact]
    public void MoveAction_PreservesActionIdentity()
    {
        // The same ActionEntry instance should survive a move, not be replaced.
        var player = new PlayerSlotConfig();
        var a0 = MakeAction("A", ActionType.Construct);
        var a1 = MakeAction("B", ActionType.GatherResources);
        var a2 = MakeAction("C", ActionType.RecruitBasicUnit);
        player.Actions.AddRange([a0, a1, a2]);

        player.MoveAction(0, 2);

        Assert.Same(a1, player.Actions[0]);
        Assert.Same(a2, player.Actions[1]);
        Assert.Same(a0, player.Actions[2]);
    }
}
