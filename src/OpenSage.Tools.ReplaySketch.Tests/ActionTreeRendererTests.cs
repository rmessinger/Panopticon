using System.Collections.Generic;
using System.Linq;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// Tests that combine <see cref="ActionTreeRenderer"/> (visual regression via golden images)
/// with coordinate-based interaction testing via <see cref="ActionTreeController.SimulateTap"/>.
/// </summary>
public class ActionTreeRendererTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PlayerSlotConfig UsaPlayer() => new() { FactionIndex = 2 };

    private static ActionEntry MakeAction(
        ActionType type = ActionType.Construct,
        string? id = null,
        string label = "Test",
        TimingConfig? timing = null) =>
        new(label, type, null, timing ?? new FixedTiming(90), id: id);

    private static PlayerSlotConfig PlayerWithActions(params ActionEntry[] actions)
    {
        var player = UsaPlayer();
        foreach (var a in actions)
            player.Actions.Add(a);
        return player;
    }

    // ── SimulateTap — interaction tests ──────────────────────────────────────

    [Fact]
    public void SimulateTap_AtNodeCenter_SelectsNode()
    {
        var action = MakeAction(label: "Build HQ");
        var player = PlayerWithActions(action);
        var ctrl = new ActionTreeController(player);

        var layout = ActionTreeLayoutEngine.ComputeLayout(player.Actions);
        var node = layout.Single();

        ctrl.SimulateTap(node.CenterX, node.CenterY);

        Assert.Equal(action.Id, ctrl.SelectedActionId);
    }

    [Fact]
    public void SimulateTap_AtNodeCorner_SelectsNode()
    {
        var action = MakeAction(label: "Corner tap");
        var player = PlayerWithActions(action);
        var ctrl = new ActionTreeController(player);

        var layout = ActionTreeLayoutEngine.ComputeLayout(player.Actions);
        var node = layout.Single();

        // Tap at the top-left corner of the node (just inside)
        ctrl.SimulateTap(
            node.CenterX - ActionTreeLayoutEngine.NodeW / 2f + 2f,
            node.CenterY - ActionTreeLayoutEngine.NodeH / 2f + 2f);

        Assert.Equal(action.Id, ctrl.SelectedActionId);
    }

    [Fact]
    public void SimulateTap_AtEmptySpace_DeSelectsNode()
    {
        var action = MakeAction();
        var player = PlayerWithActions(action);
        var ctrl = new ActionTreeController(player);

        // First select the node directly
        ctrl.SelectNode(action.Id);
        Assert.Equal(action.Id, ctrl.SelectedActionId);

        // Then tap far outside any node
        ctrl.SimulateTap(0f, 0f);

        Assert.Null(ctrl.SelectedActionId);
    }

    [Fact]
    public void SimulateTap_SameNodeTwice_KeepsSelected()
    {
        var action = MakeAction();
        var player = PlayerWithActions(action);
        var ctrl = new ActionTreeController(player);

        var layout = ActionTreeLayoutEngine.ComputeLayout(player.Actions);
        var node = layout.Single();

        var eventCount = 0;
        ctrl.SelectionChanged += (_, _) => eventCount++;

        ctrl.SimulateTap(node.CenterX, node.CenterY);  // selects
        ctrl.SimulateTap(node.CenterX, node.CenterY);  // already selected — no event

        Assert.Equal(action.Id, ctrl.SelectedActionId);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void SimulateTap_SwitchesBetweenTwoNodes()
    {
        var a1 = MakeAction(label: "First");
        var a2 = MakeAction(
            type: ActionType.GatherResources,
            label: "Second",
            timing: new AfterActionTiming(a1.Id, ActionPhase.Completed));

        var player = PlayerWithActions(a1, a2);
        var ctrl = new ActionTreeController(player);

        var layout = ActionTreeLayoutEngine.ComputeLayout(player.Actions);
        var n1 = layout.Single(n => n.Action.Id == a1.Id);
        var n2 = layout.Single(n => n.Action.Id == a2.Id);

        ctrl.SimulateTap(n1.CenterX, n1.CenterY);
        Assert.Equal(a1.Id, ctrl.SelectedActionId);

        ctrl.SimulateTap(n2.CenterX, n2.CenterY);
        Assert.Equal(a2.Id, ctrl.SelectedActionId);
    }

    [Fact]
    public void SimulateTap_OnEmptyActions_DoesNotThrow()
    {
        var ctrl = new ActionTreeController(UsaPlayer());

        var ex = Record.Exception(() => ctrl.SimulateTap(100f, 100f));
        Assert.Null(ex);
        Assert.Null(ctrl.SelectedActionId);
    }

    // ── Golden image — visual regression tests ────────────────────────────────

    [Fact]
    [Trait("Category", "Visual")]
    public void Render_EmptyGraph_MatchesGolden()
    {
        var layout = ActionTreeLayoutEngine.ComputeLayout(new List<ActionEntry>());
        GoldenImageHelper.AssertRenderedMatchesGolden(layout, "empty-graph");
    }

    [Fact]
    [Trait("Category", "Visual")]
    public void Render_SingleNode_MatchesGolden()
    {
        var action = MakeAction(id: "a1", label: "Construct");
        var layout = ActionTreeLayoutEngine.ComputeLayout(new List<ActionEntry> { action });
        GoldenImageHelper.AssertRenderedMatchesGolden(layout, "single-node");
    }

    [Fact]
    [Trait("Category", "Visual")]
    public void Render_TwoNodeChain_MatchesGolden()
    {
        var a1 = MakeAction(id: "a1", label: "Construct", type: ActionType.Construct);
        var a2 = MakeAction(
            id: "a2",
            label: "Gather Resources",
            type: ActionType.GatherResources,
            timing: new AfterActionTiming("a1", ActionPhase.Completed));

        var layout = ActionTreeLayoutEngine.ComputeLayout(new List<ActionEntry> { a1, a2 });
        GoldenImageHelper.AssertRenderedMatchesGolden(layout, "two-node-chain");
    }

    [Fact]
    [Trait("Category", "Visual")]
    public void Render_SelectedNode_HasWhiteBorder()
    {
        var action = MakeAction(id: "sel1", label: "Selected");
        var layout = ActionTreeLayoutEngine.ComputeLayout(new List<ActionEntry> { action });

        // Render once without selection
        GoldenImageHelper.AssertRenderedMatchesGolden(layout, "node-unselected");

        // Render again with this node selected
        GoldenImageHelper.AssertRenderedMatchesGolden(layout, "node-selected", selectedId: "sel1");
    }

    [Fact]
    [Trait("Category", "Visual")]
    public void Render_DispatchedEdge_UsesAmberColour()
    {
        var a1 = MakeAction(id: "b1", label: "Construct", type: ActionType.Construct);
        var a2 = MakeAction(
            id: "b2",
            label: "Gather",
            type: ActionType.GatherResources,
            timing: new AfterActionTiming("b1", ActionPhase.Dispatched));

        var layout = ActionTreeLayoutEngine.ComputeLayout(new List<ActionEntry> { a1, a2 });
        GoldenImageHelper.AssertRenderedMatchesGolden(layout, "dispatched-edge");
    }
}
