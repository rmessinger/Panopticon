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

    /// <summary>
    /// When true, a ground unit blocked by a non-progressing unit past
    /// <see cref="BlockedWaitFrames"/> repaths around it inside a bounded window (WI-N8-01).
    /// Default false; blocked units keep the stop-and-turn behavior until the operator flips it.
    /// </summary>
    public bool BlockedUnitResolution { get; set; }

    /// <summary>Authored default for <see cref="BlockedWaitFrames"/> (WI-N8-01).</summary>
    public const int DefaultBlockedWaitFrames = 15;

    /// <summary>
    /// WI-N8-01: frames without progress before the blocked-unit resolver may act. A blocker
    /// that is itself progressing resets the resolution cadence, so following traffic queues.
    /// </summary>
    public int BlockedWaitFrames { get; set; } = DefaultBlockedWaitFrames;

    /// <summary>Authored default for <see cref="LocalRepathWindowCells"/> (WI-N8-01).</summary>
    public const int DefaultLocalRepathWindowCells = 12;

    /// <summary>
    /// WI-N8-01: half-extent in cells of the local repath window; one detour search expands at
    /// most (2W+1)^2 cells, stationary units' footprints included through LocalOccupancyWindow.
    /// </summary>
    public int LocalRepathWindowCells { get; set; } = DefaultLocalRepathWindowCells;

    /// <summary>Authored default for <see cref="MaxResolutionAttempts"/> (WI-N8-01).</summary>
    public const int DefaultMaxResolutionAttempts = 3;

    /// <summary>
    /// WI-N8-01: local repath attempts before the unit gives up; afterwards it settles as
    /// GoalCrowded when inside <see cref="CrowdArrivalRadius"/> of its goal, else keeps its
    /// route and stays reported.
    /// </summary>
    public int MaxResolutionAttempts { get; set; } = DefaultMaxResolutionAttempts;

    /// <summary>Default for <see cref="CrowdArrivalRadius"/> (WI-N8-01): the mass-battle
    /// reachability arrival radius.</summary>
    public const float DefaultCrowdArrivalRadius = 60.0f;

    /// <summary>
    /// WI-N8-01 give-up and WI-N9 crowd-arrival radius in world units: how close to the goal a
    /// crowded unit may stop and still count as settled.
    /// </summary>
    public float CrowdArrivalRadius { get; set; } = DefaultCrowdArrivalRadius;

    /// <summary>
    /// When true, squad-less and cluster selections receive per-unit goal slots packed outward
    /// from the order point (WI-N9-01) instead of one shared destination. Squads keep their
    /// formation offsets either way. Default false.
    /// </summary>
    public bool DestinationSlots { get; set; }

    /// <summary>
    /// Default Chebyshev bound in cells for the WI-N9-01 slot search.
    /// <see cref="GameData.MaxCellsAdjustDestination"/> takes precedence when a game parses a
    /// nonzero value; Generals-era data leaves it 0.
    /// </summary>
    public const int DefaultDestinationSlotCells = 16;

    /// <summary>WI-N9-01: Chebyshev bound in cells for the outward slot search.</summary>
    public int DestinationSlotCells { get; set; } = DefaultDestinationSlotCells;
}
