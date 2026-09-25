using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Profiles;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Tests for <see cref="ActionTreeController"/> that verify all interaction logic
/// (selection, flyout state, default timing) without requiring a MAUI runtime.
/// </summary>
public class ActionTreeControllerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PlayerSlotConfig UsaPlayer() => new() { FactionIndex = 2 };
    private static PlayerSlotConfig GlaPlayer() => new() { FactionIndex = 4 };

    private static ActionEntry MakeAction(ActionType type = ActionType.Construct,
        string? id = null, string label = "Test") =>
        new(label, type, null, new FixedTiming(100), id: id);

    // ── Selection ────────────────────────────────────────────────────────────

    [Fact]
    public void SelectNode_NewId_SetsSelectedActionId()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.SelectNode("abc123");
        Assert.Equal("abc123", ctrl.SelectedActionId);
    }

    [Fact]
    public void SelectNode_Null_ClearsSelection()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.SelectNode("abc123");
        ctrl.SelectNode(null);
        Assert.Null(ctrl.SelectedActionId);
    }

    [Fact]
    public void SelectNode_SameId_KeepsSelected()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.SelectNode("abc123");

        var eventFired = false;
        ctrl.SelectionChanged += (_, _) => eventFired = true;

        ctrl.SelectNode("abc123");

        Assert.Equal("abc123", ctrl.SelectedActionId);
        Assert.False(eventFired, "SelectionChanged should not fire when ID is unchanged");
    }

    [Fact]
    public void SelectNode_DifferentId_FiresSelectionChanged()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.SelectNode("first");

        var eventFired = false;
        ctrl.SelectionChanged += (_, _) => eventFired = true;

        ctrl.SelectNode("second");

        Assert.True(eventFired);
        Assert.Equal("second", ctrl.SelectedActionId);
    }

    // ── Flyout open/close ────────────────────────────────────────────────────

    [Fact]
    public void OpenFlyout_ClosedState_OpensFlyout()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);

        Assert.True(ctrl.FlyoutIsOpen);
        Assert.Equal(ActionType.Construct, ctrl.FlyoutType);
        Assert.NotNull(ctrl.FlyoutDraft);
    }

    [Fact]
    public void OpenFlyout_SameType_TogglesClosed()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.OpenFlyout(ActionType.Construct);

        Assert.False(ctrl.FlyoutIsOpen);
        Assert.Null(ctrl.FlyoutDraft);
    }

    [Fact]
    public void OpenFlyout_DifferentType_SwitchesFlyout()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.OpenFlyout(ActionType.RecruitBasicUnit);

        Assert.True(ctrl.FlyoutIsOpen);
        Assert.Equal(ActionType.RecruitBasicUnit, ctrl.FlyoutType);
    }

    [Fact]
    public void CancelFlyout_WhenOpen_ClosesFlyout()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.CancelFlyout();

        Assert.False(ctrl.FlyoutIsOpen);
        Assert.Null(ctrl.FlyoutDraft);
    }

    [Fact]
    public void OpenFlyout_FiresFlyoutStateChanged()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        var fireCount = 0;
        ctrl.FlyoutStateChanged += (_, _) => fireCount++;

        ctrl.OpenFlyout(ActionType.Construct);
        Assert.Equal(1, fireCount);

        ctrl.CancelFlyout();
        Assert.Equal(2, fireCount);
    }

    // ── Draft initialisation ─────────────────────────────────────────────────

    [Fact]
    public void OpenFlyout_Construct_UsaPlayer_InitialisesBarracks()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);

        Assert.Equal("AmericaBarracks", ctrl.FlyoutDraft!.BuildingDefinitionName);
    }

    [Fact]
    public void OpenFlyout_Construct_GlaPlayer_InitialisesGlaBarracks()
    {
        var ctrl = new ActionTreeController(GlaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);

        Assert.Equal("GLABarracks", ctrl.FlyoutDraft!.BuildingDefinitionName);
    }

    [Fact]
    public void OpenFlyout_RecruitUnit_UsaPlayer_InitialisesFirstUnit()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.RecruitBasicUnit);

        Assert.Equal("AmericaInfantryRanger", ctrl.FlyoutDraft!.UnitDefinitionName);
    }

    [Fact]
    public void OpenFlyout_RecruitUnit_GlaPlayer_InitialisesFirstUnit()
    {
        var ctrl = new ActionTreeController(GlaPlayer());
        ctrl.OpenFlyout(ActionType.RecruitBasicUnit);

        Assert.Equal("GLAInfantryRebel", ctrl.FlyoutDraft!.UnitDefinitionName);
    }

    // ── Draft mutation ───────────────────────────────────────────────────────

    [Fact]
    public void SetFlyoutLabel_UpdatesDraft()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.SetFlyoutLabel("My Barracks");

        Assert.Equal("My Barracks", ctrl.FlyoutDraft!.Label);
    }

    [Fact]
    public void SetFlyoutBuilding_UpdatesDraft()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.SetFlyoutBuilding("AmericaWarFactory");

        Assert.Equal("AmericaWarFactory", ctrl.FlyoutDraft!.BuildingDefinitionName);
    }

    [Fact]
    public void SetFlyoutUnit_UpdatesDraft()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.RecruitBasicUnit);
        ctrl.SetFlyoutUnit("AmericaVehicleHumvee");

        Assert.Equal("AmericaVehicleHumvee", ctrl.FlyoutDraft!.UnitDefinitionName);
    }

    // ── CommitFlyout ─────────────────────────────────────────────────────────

    [Fact]
    public void CommitFlyout_AddsActionToPlayer()
    {
        var player = UsaPlayer();
        var ctrl = new ActionTreeController(player);

        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.CommitFlyout();

        Assert.Single(player.Actions);
        Assert.Equal(ActionType.Construct, player.Actions[0].Type);
    }

    [Fact]
    public void CommitFlyout_ClosesFlyout()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.CommitFlyout();

        Assert.False(ctrl.FlyoutIsOpen);
        Assert.Null(ctrl.FlyoutDraft);
    }

    [Fact]
    public void CommitFlyout_ClearsSelection()
    {
        var player = UsaPlayer();
        player.Actions.Add(MakeAction(id: "sel01"));
        var ctrl = new ActionTreeController(player);

        ctrl.SelectNode("sel01");
        ctrl.OpenFlyout(ActionType.GatherResources);
        ctrl.CommitFlyout();

        Assert.Null(ctrl.SelectedActionId);
    }

    [Fact]
    public void CommitFlyout_FiresActionsChanged()
    {
        var ctrl = new ActionTreeController(UsaPlayer());
        ctrl.OpenFlyout(ActionType.Construct);

        var fired = false;
        ctrl.ActionsChanged += (_, _) => fired = true;

        ctrl.CommitFlyout();
        Assert.True(fired);
    }

    [Fact]
    public void CommitFlyout_PreservesCustomLabel()
    {
        var player = UsaPlayer();
        var ctrl = new ActionTreeController(player);

        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.SetFlyoutLabel("Forward Barracks");
        ctrl.CommitFlyout();

        Assert.Equal("Forward Barracks", player.Actions[0].Label);
    }

    [Fact]
    public void CommitFlyout_PreservesBuildingDefinition()
    {
        var player = UsaPlayer();
        var ctrl = new ActionTreeController(player);

        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.SetFlyoutBuilding("AmericaWarFactory");
        ctrl.CommitFlyout();

        Assert.Equal("AmericaWarFactory", player.Actions[0].BuildingDefinitionName);
    }

    [Fact]
    public void CommitFlyout_PreservesUnitDefinition()
    {
        var player = UsaPlayer();
        var ctrl = new ActionTreeController(player);

        ctrl.OpenFlyout(ActionType.RecruitBasicUnit);
        ctrl.SetFlyoutUnit("AmericaVehicleHumvee");
        ctrl.CommitFlyout();

        Assert.Equal("AmericaVehicleHumvee", player.Actions[0].UnitDefinitionName);
    }

    // ── Timing on commit ─────────────────────────────────────────────────────

    [Fact]
    public void CommitFlyout_NoExistingActions_UsesFixedTiming()
    {
        var player = UsaPlayer();
        var ctrl = new ActionTreeController(player);

        ctrl.OpenFlyout(ActionType.Construct);
        ctrl.CommitFlyout();

        Assert.IsType<FixedTiming>(player.Actions[0].Timing);
    }

    [Fact]
    public void CommitFlyout_WithExistingAction_UsesAfterActionTiming()
    {
        var player = UsaPlayer();
        var existing = MakeAction(ActionType.Construct, id: "c001");
        player.Actions.Add(existing);
        var ctrl = new ActionTreeController(player);

        ctrl.OpenFlyout(ActionType.GatherResources);
        ctrl.CommitFlyout();

        var timing = Assert.IsType<AfterActionTiming>(player.Actions[1].Timing);
        Assert.Equal("c001", timing.TriggerActionId);
    }

    [Fact]
    public void CommitFlyout_WithSelectedNode_UsesAfterSelectedNode()
    {
        var player = UsaPlayer();
        var a1 = MakeAction(ActionType.Construct, id: "c001");
        var a2 = MakeAction(ActionType.GatherResources, id: "g001");
        player.Actions.Add(a1);
        player.Actions.Add(a2);
        var ctrl = new ActionTreeController(player);

        ctrl.SelectNode("g001");
        ctrl.OpenFlyout(ActionType.RecruitBasicUnit);
        ctrl.CommitFlyout();

        var timing = Assert.IsType<AfterActionTiming>(player.Actions[2].Timing);
        Assert.Equal("g001", timing.TriggerActionId);
    }

    // ── DefaultTiming ────────────────────────────────────────────────────────

    [Fact]
    public void DefaultTiming_Construct_NoExisting_ReturnsFixed90()
    {
        var timing = ActionTreeController.DefaultTiming(ActionType.Construct, []);
        var ft = Assert.IsType<FixedTiming>(timing);
        Assert.Equal(90u, ft.Frames);
    }

    [Fact]
    public void DefaultTiming_Construct_WithExisting_AfterLast()
    {
        var existing = new[] { MakeAction(ActionType.Construct, id: "c001") };
        var timing = ActionTreeController.DefaultTiming(ActionType.Construct, existing);
        var at = Assert.IsType<AfterActionTiming>(timing);
        Assert.Equal("c001", at.TriggerActionId);
    }

    [Fact]
    public void DefaultTiming_GatherResources_AfterConstruct_UsesDispatched()
    {
        var existing = new[] { MakeAction(ActionType.Construct, id: "c001") };
        var timing = ActionTreeController.DefaultTiming(ActionType.GatherResources, existing);
        var at = Assert.IsType<AfterActionTiming>(timing);
        Assert.Equal("c001", at.TriggerActionId);
        Assert.Equal(ActionPhase.Dispatched, at.TriggerPhase);
    }

    [Fact]
    public void DefaultTiming_RecruitBasicUnit_AfterConstruct_UsesCompleted()
    {
        var existing = new[] { MakeAction(ActionType.Construct, id: "c001") };
        var timing = ActionTreeController.DefaultTiming(ActionType.RecruitBasicUnit, existing);
        var at = Assert.IsType<AfterActionTiming>(timing);
        Assert.Equal("c001", at.TriggerActionId);
        Assert.Equal(ActionPhase.Completed, at.TriggerPhase);
    }

    [Fact]
    public void DefaultTiming_AttackEnemyBase_AfterRecruit_UsesCompleted()
    {
        var existing = new[]
        {
            MakeAction(ActionType.Construct, id: "c001"),
            MakeAction(ActionType.RecruitBasicUnit, id: "r001"),
        };
        var timing = ActionTreeController.DefaultTiming(ActionType.AttackEnemyBase, existing);
        var at = Assert.IsType<AfterActionTiming>(timing);
        Assert.Equal("r001", at.TriggerActionId);
        Assert.Equal(ActionPhase.Completed, at.TriggerPhase);
    }

    [Fact]
    public void DefaultTiming_Unknown_NoExisting_ReturnsFixed300()
    {
        var timing = ActionTreeController.DefaultTiming(ActionType.AttackEnemyBase, []);
        var ft = Assert.IsType<FixedTiming>(timing);
        Assert.Equal(300u, ft.Frames);
    }

    // ── Reparent / drag-to-reparent ──────────────────────────────────────────

    [Fact]
    public void Reparent_ToOtherNode_SetsAfterActionTiming()
    {
        var player = UsaPlayer();
        var a1 = MakeAction(ActionType.Construct, id: "a1");
        var a2 = MakeAction(ActionType.GatherResources, id: "a2");
        player.Actions.Add(a1);
        player.Actions.Add(a2);
        var ctrl = new ActionTreeController(player);

        ctrl.Reparent("a2", "a1");

        var timing = Assert.IsType<AfterActionTiming>(player.Actions[1].Timing);
        Assert.Equal("a1", timing.TriggerActionId);
        Assert.Equal(ActionPhase.Completed, timing.TriggerPhase);
    }

    [Fact]
    public void Reparent_ToNull_SetsFixedTiming()
    {
        var player = UsaPlayer();
        var a1 = MakeAction(ActionType.Construct, id: "a1");
        var a2 = MakeAction(ActionType.GatherResources, id: "a2",
            label: "Gather");
        a2 = new ActionEntry(a2.Label, a2.Command, null, new AfterActionTiming("a1", ActionPhase.Completed), "a2");
        player.Actions.Add(a1);
        player.Actions.Add(a2);
        var ctrl = new ActionTreeController(player);

        ctrl.Reparent("a2", null);

        Assert.IsType<FixedTiming>(player.Actions[1].Timing);
    }

    [Fact]
    public void Reparent_WouldCreateCycle_DoesNotReparent()
    {
        var player = UsaPlayer();
        var a1 = MakeAction(ActionType.Construct, id: "a1");
        var a2 = new ActionEntry("Gather", ActionType.GatherResources, null,
            new AfterActionTiming("a1", ActionPhase.Completed), "a2");
        player.Actions.Add(a1);
        player.Actions.Add(a2);
        var ctrl = new ActionTreeController(player);

        // Trying to make a1 a child of a2 would create a cycle
        ctrl.Reparent("a1", "a2");

        // a1's timing should be unchanged (FixedTiming from factory)
        Assert.IsType<FixedTiming>(player.Actions[0].Timing);
    }

    [Fact]
    public void SimulateDrag_OntoOtherNode_Reparents()
    {
        var player = UsaPlayer();
        var a1 = MakeAction(ActionType.Construct, id: "a1");
        var a2 = MakeAction(ActionType.GatherResources, id: "a2");
        player.Actions.Add(a1);
        player.Actions.Add(a2);
        var ctrl = new ActionTreeController(player);

        // Layout places nodes in columns; get their centres from the engine
        var layout = ActionTreeLayoutEngine.ComputeLayout(player.Actions);
        var n1 = layout.First(n => n.Action.Id == "a1");
        var n2 = layout.First(n => n.Action.Id == "a2");

        ctrl.SimulateDrag(n2.CenterX, n2.CenterY, n1.CenterX, n1.CenterY);

        var timing = Assert.IsType<AfterActionTiming>(player.Actions[1].Timing);
        Assert.Equal("a1", timing.TriggerActionId);
    }

    [Fact]
    public void SimulateDrag_ToEmptySpace_MakesRoot()
    {
        var player = UsaPlayer();
        var a2 = new ActionEntry("Gather", ActionType.GatherResources, null,
            new AfterActionTiming("a1", ActionPhase.Completed), "a2");
        player.Actions.Add(a2);
        var ctrl = new ActionTreeController(player);

        var layout = ActionTreeLayoutEngine.ComputeLayout(player.Actions);
        var n2 = layout.First(n => n.Action.Id == "a2");

        // Drag to far-off empty space (no node there)
        ctrl.SimulateDrag(n2.CenterX, n2.CenterY, 9999f, 9999f);

        Assert.IsType<FixedTiming>(player.Actions[0].Timing);
    }

    [Fact]
    public void SimulateDrag_WouldCreateCycle_DoesNotReparent()
    {
        var player = UsaPlayer();
        var a1 = MakeAction(ActionType.Construct, id: "a1");
        var a2 = new ActionEntry("Gather", ActionType.GatherResources, null,
            new AfterActionTiming("a1", ActionPhase.Completed), "a2");
        player.Actions.Add(a1);
        player.Actions.Add(a2);
        var ctrl = new ActionTreeController(player);

        var layout = ActionTreeLayoutEngine.ComputeLayout(player.Actions);
        var n1 = layout.First(n => n.Action.Id == "a1");
        var n2 = layout.First(n => n.Action.Id == "a2");

        // a2 is already a child of a1; making a1 a child of a2 would cycle
        ctrl.SimulateDrag(n1.CenterX, n1.CenterY, n2.CenterX, n2.CenterY);

        Assert.IsType<FixedTiming>(player.Actions[0].Timing);
    }
}
