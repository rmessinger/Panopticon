using System.Linq;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// Coordinate-based interaction tests for <see cref="ActionTreeController.SimulateTap"/>,
/// verifying hit-testing against the layout engine's node geometry.
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

    // ── SimulateDrag_WouldCreateCycle_DoesNotReparent already tested in ActionTreeControllerTests
}

