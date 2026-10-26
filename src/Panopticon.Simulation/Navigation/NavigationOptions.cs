namespace Panopticon.Navigation;

/// <summary>
/// Behavior-tuning knobs for <see cref="Navigation"/> (WI-N2-01). Defaults are measured
/// choices; see the WI-N2 results in the Track N implementation plan.
/// </summary>
public sealed class NavigationOptions
{
    /// <summary>Measured default for <see cref="MaxLandingRadiusCells"/> (WI-N2-01).</summary>
    public const int DefaultMaxLandingRadiusCells = 16;

    /// <summary>
    /// Ring bound in cells for blocked-follower landing searches: the first passable cell at or
    /// within this Chebyshev radius of the blocked offset destination, inside the destination's
    /// connected region, wins and the follower retraces the leader path onto it. Zero disables
    /// landing; every blocked follower then pays for its own full search, the pre-N2 behavior.
    /// </summary>
    public int MaxLandingRadiusCells { get; set; } = DefaultMaxLandingRadiusCells;

    /// <summary>Measured default for <see cref="SelectionClusterCells"/> (WI-N2-02).</summary>
    public const int DefaultSelectionClusterCells = 100;

    /// <summary>
    /// When true, squad-less multi-unit ground selections move as position clusters (WI-N2-02):
    /// one shared leader path per cell bucket via GroupMovement.MoveSquad. Default false; player
    /// orders, replays, and golden baselines keep per-unit behavior until the operator flips it.
    /// </summary>
    public bool GroupSelectionMoves { get; set; }

    /// <summary>
    /// Cell size in world units for the fixed position buckets of group selection moves. Units
    /// whose positions fall in the same cell share one leader search.
    /// </summary>
    public int SelectionClusterCells { get; set; } = DefaultSelectionClusterCells;

    /// <summary>
    /// When true, order-driven ground paths compute on the async worker queue (WI-N4-02): unit
    /// order paths, squad leader paths, and blocked-follower fallbacks submit instead of
    /// searching inline, and the logic-thread path span on order ticks drops to submission
    /// cost. Aircraft, dozer, supply, and script-specific movement keep synchronous paths.
    /// Default false; completion frames vary between runs, so it stays off for
    /// determinism-enforcing baselines.
    /// </summary>
    public bool AsyncOrderPaths { get; set; }

    /// <summary>Measured default for <see cref="PendingPathTimeoutFrames"/> (WI-N7-01): twice
    /// the async frame bound.</summary>
    public const int DefaultPendingPathTimeoutFrames = 60;

    /// <summary>
    /// WI-N7-01: a pending async order path older than this many frames cancels and falls back
    /// to one synchronous search, so no unit holds forever on a stalled request. Only relevant
    /// with <see cref="AsyncOrderPaths"/> on.
    /// </summary>
    public int PendingPathTimeoutFrames { get; set; } = DefaultPendingPathTimeoutFrames;

    /// <summary>
    /// When true, squad followers validate their formation-offset retrace route against
    /// impassable cells (WI-N7-02): a follower whose offset route crosses an impassable cell
    /// lands on the leader route near the destination through the bounded ring search, or falls
    /// back to its own search when no landing exists. Default false; the retrace behavior is
    /// unchanged until the operator flips it.
    /// </summary>
    public bool ValidateOffsetRoutes { get; set; }
}
