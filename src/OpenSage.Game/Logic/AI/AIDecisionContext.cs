#nullable enable

namespace OpenSage.Logic.AI;

/// <summary>
/// Read-only view of the state an <see cref="IAIDecisionPolicy"/> may consume, wrapping the
/// access the reference implementation uses (owner bank account, owned objects, enemies,
/// asset data) behind one seam so implementations are not coupled to engine internals more
/// broadly than necessary.
/// </summary>
public readonly struct AIDecisionContext
{
    public Player Owner { get; }

    public int OwnerPlayerIndex { get; }

    /// <summary>1-based count of decision ticks elapsed for this AI player.</summary>
    public int DecisionTickCount { get; }

    internal AIDecisionContext(Player owner, int ownerPlayerIndex, int decisionTickCount)
    {
        Owner = owner;
        OwnerPlayerIndex = ownerPlayerIndex;
        DecisionTickCount = decisionTickCount;
    }
}
