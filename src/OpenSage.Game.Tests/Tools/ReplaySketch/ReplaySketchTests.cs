using System;
using System.Linq;
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
            var gather = player.Actions[1];
            Assert.Equal(ActionType.GatherResources, gather.Type);

            var timing = Assert.IsType<AfterActionTiming>(gather.Timing);
            Assert.Equal(ActionType.Construct, timing.TriggerAction);
            Assert.Equal(ActionPhase.Dispatched, timing.TriggerPhase);
        }
    }

    [Fact]
    public void DefaultScenario_RecruitBasicUnit_TriggersOnConstructCompleted()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();

        foreach (var player in scenario.Players)
        {
            var recruit = player.Actions[2];
            Assert.Equal(ActionType.RecruitBasicUnit, recruit.Type);

            var timing = Assert.IsType<AfterActionTiming>(recruit.Timing);
            Assert.Equal(ActionType.Construct, timing.TriggerAction);
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

    private static ActionEntry MakeAfter(ActionType type, ActionType trigger, ActionPhase phase = ActionPhase.Completed) =>
        new(type.ToString(), type, null, new AfterActionTiming(trigger, phase));

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
        var child = MakeAfter(ActionType.GatherResources, ActionType.Construct);
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
        var b = MakeAfter(ActionType.GatherResources, ActionType.Construct);
        var c = MakeAfter(ActionType.RecruitBasicUnit, ActionType.GatherResources);

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
    public void ToggleSelection_SameNode_Deselects()
    {
        var node = ActionTreeLayoutEngine.ComputeLayout(
            [MakeFixed(ActionType.Construct)])[0];

        var result = ActionTreeLayoutEngine.ToggleSelection(current: node, clicked: node);
        Assert.Null(result);
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

