using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Mathematics;
using Panopticon.Navigation;

namespace Panopticon.Logic.Object;

/// <summary>
/// Movement-effect sink the extracted navigation logic acts on (WI-X6-01). The two call
/// shapes mirror the pre-extraction code exactly: setting Moving goes through
/// <see cref="GameObject.SetModelConditionState"/>, clearing it through the raw flag.
/// </summary>
internal interface INavigationMovementSink
{
    void SetMoving(bool moving);

    void SetSpeed(float speed);
}

/// <summary>
/// Card WI-X6-01: the navigation half of a ground unit's AI, extracted from
/// <see cref="AIUpdate"/> without behavior change. Owns the route (target points), movement
/// progress tracking, the pending async path request, give-way state, and local-repath
/// bookkeeping. Effects on the unit's visual movement state reach the owning AI through
/// <see cref="INavigationMovementSink"/>; this class never references AIUpdate.
/// </summary>
internal sealed class UnitNavigation
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly GameObject _gameObject;
    private readonly INavigationMovementSink _host;

    /// <summary>
    /// A list of positions along the path to the current target point. "Path" as in pathfinding, not waypoint path.
    /// </summary>
    public List<Vector3> TargetPoints { get; set; }

    /// <summary>WI-N6-01: movement liveness tracking for the current order; detection only.</summary>
    public MovementProgress Progress { get; } = new();

    // WI-N4-01: pending async path request. Not persisted in save files; a load drops it, so a
    // loaded unit re-issues movement through its next order.
    private PathRequestHandle _pendingPathHandle;
    private Vector3 _pendingPathTarget;

    /// <summary>WI-X6-01: true while an async path request is unresolved for this unit.</summary>
    internal bool HasPendingPath => _pendingPathHandle != null;

    // WI-N8-02: give-way bookkeeping. The original engine's _moveOutOfWay1/2; the cooldown
    // fields are Panopticon additions and are not persisted.
    private ObjectId _moveOutOfWay1;
    private ObjectId _moveOutOfWay2;
    private bool _gaveWayOnce;
    private uint _frameAtLastGiveWay;

    // WI-N8-01: blocked-unit resolver bookkeeping. Attempts reset when the unit progresses or
    // takes a new order; at most one attempt fires per BlockedWaitFrames interval.
    private const int NoLocalRepathAttempt = -1;
    private int _localRepathAttempts;
    private int _framesAtLastLocalRepathAttempt = NoLocalRepathAttempt;

    public UnitNavigation(GameObject gameObject, INavigationMovementSink host)
    {
        _gameObject = gameObject;
        _host = host;
        TargetPoints = new List<Vector3>();
    }

    internal void AddTargetPoint(in Vector3 targetPoint)
    {
        TargetPoints.Add(targetPoint);

        _host.SetMoving(true);
    }

    internal void AppendPathToTargetPoint(
        in Vector3 targetPoint,
        PathRequestOrigin origin,
        bool asyncEligible,
        bool supportsAsyncOrderPaths)
    {
        if (_gameObject.Definition.KindOf == null)
        {
            return;
        }

        Progress.Reset();
        ResetLocalRepath();

        if (!_gameObject.Definition.KindOf.Get(ObjectKinds.Aircraft) && targetPoint != _gameObject.Translation)
        {
            if (_gameObject.GameEngine.Navigation == null)
            {
                AddTargetPoint(targetPoint);
                return;
            }

            // WI-N4-02: order-driven paths may compute on the async queue; workflow movement
            // (scripts, dozer, supply, aircraft) passes asyncEligible false or opts out below.
            if (asyncEligible && supportsAsyncOrderPaths && _gameObject.GameEngine.Navigation.Options.AsyncOrderPaths)
            {
                RequestPathAsync(targetPoint);
                return;
            }

            var start = _gameObject.Translation;
            var path = _gameObject.GameEngine.Navigation.CalculatePath(start, targetPoint, out var endIsPassable, origin);
            if (path.Count == 0)
            {
                // WI-N7-01: the order produced no route; the unit settles instead of holding.
                SettleNoRoute();
                return;
            }

            path.RemoveAt(0);

            TargetPoints.AddRange(path);
            if (TargetPoints.Count > 0 && endIsPassable)
            {
                TargetPoints[TargetPoints.Count - 1] = targetPoint;
            }

            Logger.Debug("Set new target points: " + TargetPoints.Count);
            _host.SetMoving(true);
        }
        else
        {
            AddTargetPoint(targetPoint);
        }
    }

    /// <summary>
    /// Card WI-N4-01: submits this unit's path onto the async queue. Cancels any pending request
    /// first; the drain applies the completion on the logic thread with the same post-processing
    /// as <see cref="AppendPathToTargetPoint"/> (drop the start point, land exactly on a passable
    /// target, set the moving condition). Aircraft, degenerate, and no-navigation cases fall back
    /// to the synchronous <see cref="AddTargetPoint"/>. Order-eligible callers reach this via
    /// <see cref="NavigationOptions.AsyncOrderPaths"/> (WI-N4-02).
    /// </summary>
    internal void RequestPathAsync(Vector3 targetPoint)
    {
        CancelPendingPath();
        Progress.Reset();
        ResetLocalRepath();

        if (_gameObject.Definition.KindOf == null
            || _gameObject.Definition.KindOf.Get(ObjectKinds.Aircraft)
            || targetPoint == _gameObject.Translation
            || _gameObject.GameEngine.Navigation == null)
        {
            AddTargetPoint(targetPoint);
            return;
        }

        _pendingPathTarget = targetPoint;
        _pendingPathHandle = _gameObject.GameEngine.Navigation.SubmitPathRequest(
            _gameObject.Translation, targetPoint, _gameObject, OnPendingPathCompleted);
        Progress.RecordAwaitingPath();
    }

    private void OnPendingPathCompleted(PathRequestHandle handle)
    {
        _pendingPathHandle = null;

        // WI-N7-01: a worker death surfaces as a Failed completion; the unit settles.
        if (handle.Outcome == PathRequestOutcome.Failed)
        {
            SettleNoRoute();
            return;
        }

        if (!handle.TryGetResult(out var waypoints, out var endIsPassable))
        {
            return;
        }

        if (waypoints.Count == 0)
        {
            SettleNoRoute();
            return;
        }

        ApplyRoute(waypoints, endIsPassable, _pendingPathTarget);
    }

    private void ApplyRoute(IList<Vector3> waypoints, bool endIsPassable, Vector3 target)
    {
        TargetPoints.Clear();
        for (var i = 1; i < waypoints.Count; i++)
        {
            TargetPoints.Add(waypoints[i]);
        }

        if (TargetPoints.Count > 0 && endIsPassable)
        {
            TargetPoints[^1] = target;
        }

        // WI-N6-01: an empty completion is a failed order; a route resumes progress tracking.
        if (TargetPoints.Count == 0)
        {
            Progress.RecordNoRoute();
        }
        else
        {
            Progress.Reset();
            ResetLocalRepath();
        }

        _host.SetMoving(true);
    }

    /// <summary>
    /// WI-N7-01: terminal no-route state. The order can produce no route (failed search,
    /// worker death, empty async completion), so the route and moving state are cleared and the
    /// stall reason records NoRoute for attribution.
    /// </summary>
    internal void SettleNoRoute()
    {
        TargetPoints.Clear();
        _host.SetMoving(false);
        _host.SetSpeed(0);
        Progress.RecordNoRoute();
    }

    /// <summary>
    /// WI-N8-01: terminal goal-crowded state. The resolver exhausted its attempts with the
    /// goal inside the crowd-arrival radius: the route and moving state are cleared and the
    /// stall reason records GoalCrowded for attribution.
    /// </summary>
    internal void SettleGoalCrowded()
    {
        TargetPoints.Clear();
        _host.SetMoving(false);
        _host.SetSpeed(0);
        Progress.RecordGoalCrowded();
    }

    /// <summary>
    /// WI-N9-02: crowd arrival. A unit on its final segment, held off behind an arrived
    /// friendly standing within <see cref="NavigationOptions.CrowdArrivalRadius"/> of this
    /// unit's goal, stops and reports CrowdArrived instead of holding its route. Active when
    /// <see cref="NavigationOptions.BlockedUnitResolution"/> or
    /// <see cref="NavigationOptions.DestinationSlots"/> is on; a unit with a reachable free
    /// goal never settles here.
    /// </summary>
    internal bool TryCrowdArrive(Panopticon.Navigation.Navigation navigation)
    {
        var options = navigation.Options;
        if (!options.BlockedUnitResolution && !options.DestinationSlots)
        {
            return false;
        }

        // Final approach only: every remaining waypoint sits inside the arrival disk around
        // the goal (the strict last-segment test strands units whose route tail holds one
        // intermediate waypoint inside the crowd).
        if (TargetPoints.Count == 0)
        {
            return false;
        }

        var goal = TargetPoints[^1];
        for (var i = 0; i < TargetPoints.Count - 1; i++)
        {
            var toWaypoint = TargetPoints[i] - goal;
            if (toWaypoint.Vector2XY().Length() > options.CrowdArrivalRadius)
            {
                return false;
            }
        }

        var blocker = _gameObject.GameEngine.GameLogic.GetObjectById(Progress.BlockerId);
        if (blocker == null || blocker.IsDestroyed || blocker.Owner != _gameObject.Owner)
        {
            return false;
        }

        var blockerAi = blocker.AIUpdate;
        if (blockerAi == null
            || blockerAi.TargetPoints.Count > 0
            || blocker.ModelConditionFlags.Get(ModelConditionFlag.Moving))
        {
            // Not an arrived friendly: moving, ordered, or not a unit.
            return false;
        }

        var toBlocker = blocker.Translation - goal;
        if (toBlocker.Vector2XY().Length() > options.CrowdArrivalRadius)
        {
            // The blocker stands away from this unit's goal: the goal is reachable.
            return false;
        }

        TargetPoints.Clear();
        _host.SetMoving(false);
        _host.SetSpeed(0);
        Progress.RecordCrowdArrived();
        return true;
    }

    /// <summary>
    /// WI-N8-01: replaces the first <paramref name="supersededWaypoints"/> target points with
    /// the local detour from the blocked-unit resolver; the detour rejoins the kept remainder.
    /// </summary>
    internal void SpliceLocalDetour(IReadOnlyList<Vector3> detour, int supersededWaypoints)
    {
        if (supersededWaypoints > 0)
        {
            TargetPoints.RemoveRange(0, supersededWaypoints);
        }

        TargetPoints.InsertRange(0, detour);
        _host.SetMoving(true);
    }

    /// <summary>
    /// Card WI-N8-02: asks this idle, arrived, or order-less unit to step out of
    /// <paramref name="requester"/>'s way. Friendly requesters only, at most one sidestep per
    /// BlockedWaitFrames window; the sidestep is the nearest free passable cell off the
    /// requester's next segment. True when accepted.
    /// </summary>
    internal bool RequestGiveWay(GameObject requester, Panopticon.Navigation.Navigation navigation, in Vector2 segmentStart, in Vector2 segmentEnd)
    {
        if (requester.Owner != _gameObject.Owner
            || TargetPoints.Count > 0
            || _gameObject.ModelConditionFlags.Get(ModelConditionFlag.Moving)
            || Progress.Reason != NavigationStallReason.None)
        {
            return false;
        }

        if (RecentlyGaveWay(navigation.Options.BlockedWaitFrames))
        {
            return false;
        }

        var sidestep = GiveWayPlanner.FindSidestep(
            _gameObject,
            requester,
            navigation,
            segmentStart,
            segmentEnd,
            navigation.Options.LocalRepathWindowCells,
            resumePoint: null);
        if (sidestep == null)
        {
            return false;
        }

        _moveOutOfWay1 = requester.Id;
        AcceptSidestep(sidestep.Value);
        return true;
    }

    /// <summary>
    /// Card WI-N8-02: a mutually blocked unit with the higher object id yields by sidestepping
    /// off <paramref name="other"/>'s next segment while keeping its route: the sidestep point
    /// is inserted before the remaining waypoints.
    /// </summary>
    internal bool SidestepWhileKeepingRoute(GameObject other, Panopticon.Navigation.Navigation navigation, in Vector2 segmentStart, in Vector2 segmentEnd)
    {
        if (RecentlyGaveWay(navigation.Options.BlockedWaitFrames))
        {
            return false;
        }

        // WI-N11-01: the resume leg back to the first remaining waypoint is validated with
        // the approach leg, so the yield cannot splice a drive through impassable cells.
        var sidestep = GiveWayPlanner.FindSidestep(
            _gameObject,
            other,
            navigation,
            segmentStart,
            segmentEnd,
            navigation.Options.LocalRepathWindowCells,
            TargetPoints.Count > 0 ? TargetPoints[0].Vector2XY() : null);
        if (sidestep == null)
        {
            return false;
        }

        _moveOutOfWay1 = other.Id;
        _gaveWayOnce = true;
        _frameAtLastGiveWay = CurrentLogicFrame();
        ResetLocalRepath();
        Progress.Reset();
        TargetPoints.Insert(0, sidestep.Value);
        return true;
    }

    /// <summary>True within the BlockedWaitFrames window after the last accepted sidestep.</summary>
    internal bool RecentlyGaveWay(int blockedWaitFrames)
    {
        return _gaveWayOnce && CurrentLogicFrame() - _frameAtLastGiveWay < (uint)blockedWaitFrames;
    }

    private void AcceptSidestep(Vector3 sidestep)
    {
        _gaveWayOnce = true;
        _frameAtLastGiveWay = CurrentLogicFrame();
        ResetLocalRepath();
        AddTargetPoint(sidestep);
    }

    private uint CurrentLogicFrame()
    {
        return (uint)(_gameObject.GameEngine.Game.MapTime.TotalTime.TotalMilliseconds
            / _gameObject.GameEngine.MsPerLogicFrame);
    }

    /// <summary>
    /// WI-N8-01: attempt cadence for the blocked-unit resolver. True when a new attempt may
    /// fire now; false with <paramref name="gaveUp"/> true once the attempt budget is spent.
    /// </summary>
    internal bool TryBeginLocalRepathAttempt(int framesWithoutProgress, int blockedWaitFrames, int maxAttempts, out bool gaveUp)
    {
        if (_localRepathAttempts >= maxAttempts)
        {
            gaveUp = true;
            return false;
        }

        if (_framesAtLastLocalRepathAttempt != NoLocalRepathAttempt
            && framesWithoutProgress - _framesAtLastLocalRepathAttempt < blockedWaitFrames)
        {
            gaveUp = false;
            return false;
        }

        _localRepathAttempts++;
        _framesAtLastLocalRepathAttempt = framesWithoutProgress;
        gaveUp = false;
        return true;
    }

    internal void ResetLocalRepath()
    {
        _localRepathAttempts = 0;
        _framesAtLastLocalRepathAttempt = NoLocalRepathAttempt;
    }

    /// <summary>
    /// WI-N7-01: pending-path watchdog. A pending async order path older than
    /// <see cref="NavigationOptions.PendingPathTimeoutFrames"/> cancels and falls back to one
    /// synchronous search; a failed fallback settles the unit. Called each tick while a handle
    /// is pending.
    /// </summary>
    internal void CheckPendingPathTimeout()
    {
        if (_pendingPathHandle == null)
        {
            return;
        }

        var navigation = _gameObject.GameEngine.Navigation;
        if (navigation == null
            || _pendingPathHandle.WaitedFrames <= navigation.Options.PendingPathTimeoutFrames)
        {
            return;
        }

        var target = _pendingPathTarget;
        CancelPendingPath();

        var path = navigation.CalculatePath(
            _gameObject.Translation, target, out var endIsPassable, PathRequestOrigin.Unit);
        if (path.Count == 0)
        {
            SettleNoRoute();
            return;
        }

        ApplyRoute(path, endIsPassable, target);
    }

    internal void CancelPendingPath()
    {
        _pendingPathHandle?.Cancel();
        _pendingPathHandle = null;
    }

    /// <summary>
    /// WI-X6-01: persists exactly the two fields AIUpdate used to persist between _repulsor2
    /// and the locomotor set. Everything else here is run-only state.
    /// </summary>
    internal void Persist(StatePersister reader)
    {
        reader.PersistObjectId(ref _moveOutOfWay1);
        reader.PersistObjectId(ref _moveOutOfWay2);
    }
}
