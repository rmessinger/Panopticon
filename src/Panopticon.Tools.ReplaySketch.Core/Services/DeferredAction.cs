using System.Collections.Generic;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// An action that was not written to the pre-baked replay file because it uses
/// <see cref="AfterActionTiming"/>. The test harness injects it at runtime when the trigger
/// action reaches the specified <see cref="Phase"/>.
/// </summary>
public sealed class DeferredAction
{
    /// <summary>
    /// The game player index that owns this action (matches <c>Order.PlayerIndex</c>
    /// and <c>Player.Id</c>).
    /// </summary>
    public int PlayerIndex { get; }

    /// <summary>The <see cref="ActionType"/> whose phase transition triggers this action.</summary>
    public ActionType Trigger { get; }

    /// <summary>
    /// The phase the trigger action must reach before this action's orders are injected.
    /// </summary>
    public ActionPhase Phase { get; }

    /// <summary>
    /// The specific object/unit definition name the trigger action targets (e.g. the building
    /// name for a Construct trigger, or the unit name for a RecruitBasicUnit trigger). When
    /// non-null, matching requires the runtime event's <see cref="ReplayActionEventArgs.DefinitionName"/>
    /// to match this value, so that scenarios with multiple actions sharing the same
    /// <see cref="ActionType"/> (e.g. one Construct per building in a full build-out) each only
    /// trigger their own children. Null for trigger types that don't target a specific definition
    /// (GatherResources, AttackEnemyBase, EnemyBaseDestroyed) — those match on type/phase/player alone.
    /// </summary>
    public string? TriggerDefinitionName { get; }

    /// <summary>Pre-resolved orders to inject when the trigger fires.</summary>
    public IReadOnlyList<Order> Orders { get; }

    /// <summary>
    /// Additional frames to wait after the trigger fires before injecting the orders.
    /// </summary>
    public uint ExtraFrames { get; }

    public DeferredAction(int playerIndex, ActionType trigger, ActionPhase phase, IReadOnlyList<Order> orders,
        uint extraFrames, string? triggerDefinitionName = null)
    {
        PlayerIndex = playerIndex;
        Trigger = trigger;
        Phase = phase;
        Orders = orders;
        ExtraFrames = extraFrames;
        TriggerDefinitionName = triggerDefinitionName;
    }
}
