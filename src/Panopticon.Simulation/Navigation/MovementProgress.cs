using Panopticon.Logic.Object;

namespace Panopticon.Navigation;

/// <summary>Reason a unit made no progress toward its current target point.</summary>
public enum NavigationStallReason
{
    None,

    BlockedByUnit,

    AwaitingPath,

    NoRoute,

    GoalCrowded,

    CrowdArrived,
}

/// <summary>
/// Card WI-N6-01: per-unit movement liveness tracking. AIUpdate records each move step; the
/// TestHarness reads the reason, blocker, and frame count for stall attribution and wait-for
/// cycles. Detection only; resolution behavior belongs to N7-N9.
/// </summary>
public sealed class MovementProgress
{
    /// <summary>Minimum shrink of the distance to the current waypoint that counts as progress.</summary>
    public const float ProgressEpsilon = 0.05f;

    private float _lastWaypointDistance = -1.0f;

    public int FramesWithoutProgress { get; private set; }

    public NavigationStallReason Reason { get; private set; }

    public ObjectId BlockerId { get; private set; }

    /// <summary>Called for each move step; the distance is measured before the physics step.</summary>
    public void RecordMoveStep(float distanceToWaypoint, bool blockedByUnit, ObjectId blocker)
    {
        var progressed = _lastWaypointDistance < 0.0f
            || distanceToWaypoint <= _lastWaypointDistance - ProgressEpsilon;
        if (progressed)
        {
            FramesWithoutProgress = 0;
            Reason = NavigationStallReason.None;
            BlockerId = default;
        }
        else
        {
            FramesWithoutProgress++;
            if (blockedByUnit && blocker != default)
            {
                Reason = NavigationStallReason.BlockedByUnit;
                BlockerId = blocker;
            }
            else
            {
                Reason = NavigationStallReason.None;
                BlockerId = default;
            }
        }

        _lastWaypointDistance = distanceToWaypoint;
    }

    /// <summary>Called once per logic tick while a pending async path request exists.</summary>
    public void RecordAwaitingPath()
    {
        FramesWithoutProgress++;
        Reason = NavigationStallReason.AwaitingPath;
        BlockerId = default;
    }

    /// <summary>Called when an order produced an empty route, sync or async.</summary>
    public void RecordNoRoute()
    {
        FramesWithoutProgress++;
        Reason = NavigationStallReason.NoRoute;
        BlockerId = default;
    }

    /// <summary>
    /// Called when the blocked-unit resolver gives up near a crowded goal (WI-N8-01): the unit
    /// clears its route and stops inside the crowd-arrival radius.
    /// </summary>
    public void RecordGoalCrowded()
    {
        FramesWithoutProgress++;
        Reason = NavigationStallReason.GoalCrowded;
        BlockerId = default;
    }

    /// <summary>
    /// Called when a final-segment unit settles behind an arrived friendly near its goal
    /// (WI-N9-02): the route is cleared and the arrival is reported.
    /// </summary>
    public void RecordCrowdArrived()
    {
        FramesWithoutProgress++;
        Reason = NavigationStallReason.CrowdArrived;
        BlockerId = default;
    }

    /// <summary>Clears all tracking; called on new orders, waypoint arrivals, and stops.</summary>
    public void Reset()
    {
        FramesWithoutProgress = 0;
        Reason = NavigationStallReason.None;
        BlockerId = default;
        _lastWaypointDistance = -1.0f;
    }
}
