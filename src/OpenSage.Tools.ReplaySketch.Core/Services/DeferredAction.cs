using System.Collections.Generic;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

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

    /// <summary>Pre-resolved orders to inject when the trigger fires.</summary>
    public IReadOnlyList<Order> Orders { get; }

    /// <summary>
    /// Additional frames to wait after the trigger fires before injecting the orders.
    /// </summary>
    public uint ExtraFrames { get; }

    public DeferredAction(int playerIndex, ActionType trigger, ActionPhase phase, IReadOnlyList<Order> orders, uint extraFrames)
    {
        PlayerIndex = playerIndex;
        Trigger = trigger;
        Phase = phase;
        Orders = orders;
        ExtraFrames = extraFrames;
    }
}
