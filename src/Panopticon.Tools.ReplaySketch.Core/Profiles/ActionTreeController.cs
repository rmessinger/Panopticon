using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.Profiles;

/// <summary>
/// Pure-C# controller that owns the interaction logic for the action tree panel:
/// selection state, flyout (add-action) state, and default timing/position computation.
/// The MAUI view is a thin delegate; tests drive this class directly.
/// </summary>
public sealed class ActionTreeController
{
    // ── Palette metadata ─────────────────────────────────────────────────────

    /// <summary>
    /// Palette button metadata. <c>Verb</c> is used as the prefix when computing a default label
    /// for a new action; for actions without a target it doubles as the full default label.
    /// </summary>
    public static readonly (ActionType Type, string Label, string Verb)[] PaletteItems =
    [
        (ActionType.Construct,        "Construct",         "Construct"),
        (ActionType.GatherResources,  "Gather Resources",  "Gather Resources"),
        (ActionType.RecruitBasicUnit, "Recruit Unit",      "Recruit"),
        (ActionType.AttackEnemyBase,  "Attack Enemy Base", "Attack Enemy Base"),
    ];

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly PlayerSlotConfig _player;

    /// <summary>
    /// The ID of the currently selected action node, or <c>null</c> if nothing is selected.
    /// </summary>
    public string? SelectedActionId { get; private set; }

    /// <summary>Whether the add-action flyout is currently visible.</summary>
    public bool FlyoutIsOpen { get; private set; }

    /// <summary>The action type currently shown in the flyout.</summary>
    public ActionType FlyoutType { get; private set; }

    /// <summary>
    /// The draft <see cref="ActionEntry"/> being configured in the flyout.
    /// <c>null</c> when the flyout is closed.
    /// </summary>
    public ActionEntry? FlyoutDraft { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when <see cref="PlayerSlotConfig.Actions"/> has changed (action added).</summary>
    public event EventHandler? ActionsChanged;

    /// <summary>Fired when <see cref="SelectedActionId"/> has changed.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Fired when flyout open/close state or draft content has changed.</summary>
    public event EventHandler? FlyoutStateChanged;

    // ── Construction ──────────────────────────────────────────────────────────

    public ActionTreeController(PlayerSlotConfig player)
    {
        _player = player;
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the selected node.  Pass <c>null</c> to deselect.
    /// Clicking the already-selected node keeps it selected (does not deselect).
    /// </summary>
    public void SelectNode(string? actionId)
    {
        if (SelectedActionId == actionId) return;
        SelectedActionId = actionId;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Simulates a canvas tap at logical coordinates (<paramref name="x"/>, <paramref name="y"/>).
    /// Computes the current layout internally and selects whichever node (if any) is hit.
    /// Tapping empty space deselects the current node.
    /// </summary>
    public void SimulateTap(float x, float y)
    {
        var layout = ActionTreeLayoutEngine.ComputeLayout(_player.Actions);
        string? hitId = null;
        foreach (var node in layout)
        {
            if (ActionTreeLayoutEngine.HitTest(node, x, y))
            {
                hitId = node.Action.Id;
                break;
            }
        }

        SelectNode(hitId);
    }

    // ── Drag-to-reparent ──────────────────────────────────────────────────────

    /// <summary>
    /// Simulates a drag gesture from (<paramref name="fromX"/>, <paramref name="fromY"/>) to
    /// (<paramref name="toX"/>, <paramref name="toY"/>) in logical canvas coordinates.
    /// If a node is hit at the start position and the end position is a different node,
    /// the source is reparented under the target (<see cref="AfterActionTiming"/>).
    /// Dropping on empty space makes the source a root node (<see cref="FixedTiming"/>).
    /// No-ops when the source cannot be found or a cycle would result.
    /// </summary>
    public void SimulateDrag(float fromX, float fromY, float toX, float toY)
    {
        var layout = ActionTreeLayoutEngine.ComputeLayout(_player.Actions);

        string? sourceId = null;
        string? targetId = null;

        foreach (var node in layout)
        {
            if (sourceId == null && ActionTreeLayoutEngine.HitTest(node, fromX, fromY))
                sourceId = node.Action.Id;
            if (targetId == null && ActionTreeLayoutEngine.HitTest(node, toX, toY))
                targetId = node.Action.Id;
        }

        if (sourceId == null) return;

        Reparent(sourceId, targetId == sourceId ? null : targetId);
    }

    /// <summary>
    /// Re-parents <paramref name="sourceId"/> under <paramref name="newParentId"/>.
    /// Passing <c>null</c> for <paramref name="newParentId"/> promotes the source to a root
    /// node by assigning a <see cref="FixedTiming"/>.
    /// Silently ignores requests that would create a cycle in the action graph.
    /// </summary>
    public void Reparent(string sourceId, string? newParentId)
    {
        var source = _player.Actions.FirstOrDefault(a => a.Id == sourceId);
        if (source == null) return;

        if (newParentId != null && IsCyclicReparent(sourceId, newParentId)) return;

        source.Timing = newParentId != null
            ? new AfterActionTiming(newParentId, ActionPhase.Completed)
            : new FixedTiming(300);

        ActionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns <c>true</c> if making <paramref name="nodeId"/> a child of
    /// <paramref name="newParentId"/> would create a cycle — i.e., <paramref name="nodeId"/>
    /// is already an ancestor of <paramref name="newParentId"/>.
    /// </summary>
    private bool IsCyclicReparent(string nodeId, string newParentId)
    {
        var byId = _player.Actions.ToDictionary(a => a.Id);
        var visited = new HashSet<string>();
        var current = newParentId;

        while (current != null && !visited.Contains(current))
        {
            if (current == nodeId) return true;
            visited.Add(current);
            current = byId.TryGetValue(current, out var a) && a.Timing is AfterActionTiming aat
                ? aat.TriggerActionId
                : null;
        }

        return false;
    }

    // ── Add-action flyout ─────────────────────────────────────────────────────

    /// <summary>
    /// Opens the flyout for <paramref name="type"/>, or closes it if already open
    /// for the same type (toggle behaviour).
    /// </summary>
    public void OpenFlyout(ActionType type)
    {
        if (FlyoutIsOpen && FlyoutType == type)
        {
            CloseFlyout();
            return;
        }

        FlyoutType = type;
        FlyoutDraft = CreateDraft(type);
        FlyoutIsOpen = true;
        FlyoutStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Discards the draft and closes the flyout without adding an action.</summary>
    public void CancelFlyout()
    {
        CloseFlyout();
    }

    private void CloseFlyout()
    {
        FlyoutIsOpen = false;
        FlyoutDraft = null;
        FlyoutStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Draft mutation helpers ─────────────────────────────────────────────────

    public void SetFlyoutLabel(string label)
    {
        if (FlyoutDraft != null)
            FlyoutDraft.Label = label;
    }

    public void SetFlyoutBuilding(string definitionName)
    {
        if (FlyoutDraft != null)
            FlyoutDraft.BuildingDefinitionName = definitionName;
    }

    public void SetFlyoutUnit(string definitionName)
    {
        if (FlyoutDraft != null)
            FlyoutDraft.UnitDefinitionName = definitionName;
    }

    // ── Commit ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finalises the draft, assigns timing, appends to the player's action list,
    /// clears selection, and fires the appropriate events.
    /// </summary>
    public void CommitFlyout()
    {
        if (FlyoutDraft == null) return;

        TimingConfig timing;
        var triggerAction = SelectedActionId != null
            ? _player.Actions.FirstOrDefault(a => a.Id == SelectedActionId)
            : null;

        timing = triggerAction != null
            ? new AfterActionTiming(triggerAction.Id, ActionPhase.Completed)
            : DefaultTiming(FlyoutType, _player.Actions);

        FlyoutDraft.Timing = timing;
        _player.Actions.Add(FlyoutDraft);
        FlyoutDraft = null;

        var hadSelection = SelectedActionId != null;
        SelectedActionId = null;
        FlyoutIsOpen = false;

        FlyoutStateChanged?.Invoke(this, EventArgs.Empty);
        if (hadSelection)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        ActionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Default timing/position (public static so tests can exercise directly) ─

    public static TimingConfig DefaultTiming(ActionType type, IReadOnlyList<ActionEntry> existing)
    {
        string? FindId(ActionType triggerType) =>
            existing.FirstOrDefault(a => a.Type == triggerType)?.Id;

        AfterActionTiming AfterLast(ActionPhase phase = ActionPhase.Completed) =>
            new(existing[^1].Id, phase);

        return type switch
        {
            ActionType.Construct => existing.Count == 0
                ? new FixedTiming(90)
                : AfterLast(),
            ActionType.GatherResources => FindId(ActionType.Construct) is { } id1
                ? new AfterActionTiming(id1, ActionPhase.Dispatched)
                : existing.Count > 0 ? AfterLast() : new FixedTiming(300),
            ActionType.RecruitBasicUnit => FindId(ActionType.Construct) is { } id2
                ? new AfterActionTiming(id2, ActionPhase.Completed)
                : existing.Count > 0 ? AfterLast() : new FixedTiming(300),
            ActionType.AttackEnemyBase => FindId(ActionType.RecruitBasicUnit) is { } id3
                ? new AfterActionTiming(id3, ActionPhase.Completed)
                : existing.Count > 0 ? AfterLast() : new FixedTiming(300),
            _ => existing.Count > 0 ? AfterLast() : new FixedTiming(300),
        };
    }

    public static PositionSpec? DefaultPosition(ActionType type) => type switch
    {
        ActionType.RecruitBasicUnit => null,
        _ => new LandmarkRelativePosition(LandmarkType.OwnBase, 2f, new FixedAngle(45f)),
    };

    // ── Private helpers ───────────────────────────────────────────────────────

    private ActionEntry CreateDraft(ActionType type)
    {
        var item = PaletteItems.FirstOrDefault(p => p.Type == type);
        var factionIndex = _player.FactionIndex;

        var buildingDefinitionName = type == ActionType.Construct
            ? BuildingCatalog.ForFaction(factionIndex)
                .FirstOrDefault(b => b.DisplayName == "Barracks")?.DefinitionName
                ?? BuildingCatalog.ForFaction(factionIndex).FirstOrDefault()?.DefinitionName
            : null;

        var unitDefinitionName = type == ActionType.RecruitBasicUnit
            ? UnitCatalog.ForFaction(factionIndex).FirstOrDefault()?.DefinitionName
            : null;

        // Default label is "[Verb] [TargetDisplayName]" for actions that target a specific
        // object (e.g. "Construct Barracks", "Recruit Ranger"), or just the verb for others.
        var label = type switch
        {
            ActionType.Construct when buildingDefinitionName != null
                => $"{item.Verb} {BuildingCatalog.GetDisplayName(buildingDefinitionName, factionIndex)}",
            ActionType.RecruitBasicUnit when unitDefinitionName != null
                => $"{item.Verb} {UnitCatalog.GetDisplayName(unitDefinitionName, factionIndex)}",
            _ => item.Verb,
        };

        return new ActionEntry(
            label: label,
            type: type,
            position: DefaultPosition(type),
            timing: new FixedTiming(0),  // overwritten in CommitFlyout
            buildingDefinitionName: buildingDefinitionName,
            unitDefinitionName: unitDefinitionName);
    }
}
