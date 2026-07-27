using OpenSage.Logic.Orders;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// The resolved primary action for one roster unit in the validation ground: the action kind, the
/// engine <see cref="OrderType"/> dispatched to perform it, an optional target definition name
/// (the structure a builder constructs), and the frame budget before the action times out.
/// See deep dive P9 §2.4.
/// </summary>
public sealed record PrimaryAction(
    PrimaryActionKind Kind,
    OrderType OrderType,
    string? TargetDefinitionName,
    int FrameBudget)
{
    /// <summary>Universal move-to action, the fallback every unit performs.</summary>
    public static PrimaryAction Move(int frameBudget) =>
        new(PrimaryActionKind.Move, OrderType.MoveTo, null, frameBudget);
}
