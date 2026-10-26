using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Panopticon.Data.Ini;
using Panopticon.Logic.AI;
using Panopticon.Logic.Map;
using Panopticon.Mathematics;
using Panopticon.Navigation;
using Panopticon.Scripting;

namespace Panopticon.Logic.Object;

public class AIUpdate : UpdateModule
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private AIUpdateStateMachine _stateMachine;

    private protected AIUpdateStateMachine StateMachine => _stateMachine ??= CreateStateMachine();

    private readonly LocomotorSet _locomotorSet;
    private LocomotorSetType _currentLocomotorSetType;

    public LocomotorSetType CurrentLocomotorSetType => _currentLocomotorSetType;

    public Locomotor CurrentLocomotor { get; protected set; }

    internal virtual AIUpdateModuleData ModuleData { get; }

    private readonly TurretAIUpdate _turretAIUpdate;

    /// <summary>
    /// An enumerator of the waypoints if the unit is currently following a waypoint path.
    /// The path (as in pathfinding) to the next waypoint can contain multiple <see cref="TargetPoints"/>.
    /// </summary>
    private IEnumerator<Vector3> _waypointEnumerator;

    private Vector3? _targetDirection;
    private bool _ignoreWeaponTargetForMovement;
    internal const uint AttackMoveSearchIntervalFrames = 10;
    private readonly Predicate<GameObject> _attackMoveCandidateFilter;
    private uint _nextAttackMoveSearchFrame;

    private uint _priorWaypointId;
    private uint _currentWaypointId;
    private bool _isAIDead;
    private bool _isRecruitable;
    private uint _nextEnemyScanTime;
    private ObjectId _currentVictimId;
    private float _desiredSpeed;
    private CommandSourceType _lastCommandSource;
    private GuardTargetType _guardTargetType0;
    private GuardTargetType _guardTargetType1;
    private Vector3 _locationToGuard;
    private ObjectId _objectToGuard;
    private PolygonTrigger _areaToGuard;
    private AttackPriority _attackInfo;
    private readonly List<Vector3> _waypointQueue = new(16);
    private int _waypointIndex;
    private bool _executingWaypointQueue;
    private Waypoint _completedWaypoint;
    private bool _waitingForPath;
    private PathfindingPath _path;
    private ObjectId _requestedVictimId;
    private Vector3 _requestedDestination;
    private Vector3 _requestedDestination2;
    private ObjectId _ignoreObstacleId;
    private float _pathExtraDistance;
    private Point2D _pathfindGoalCell;
    private Point2D _pathfindCurrentCell;
    private LogicFrame _ignoreCollisionsUntil;
    private LogicFrame _queueForPathFrame;
    private Vector3 _finalPosition;
    private bool _doFinalPosition;
    private bool _isAttackPath;
    private bool _isFinalGoal;

    private bool _isApproachPath;
    private bool _isSafePath;
    private bool _movementComplete;
    private bool _upgradedLocomotors;
    private bool _canPathThroughUnits;
    private bool _randomlyOffsetMoodCheck;
    private ObjectId _repulsor1;
    private ObjectId _repulsor2;
    private ObjectId _moveOutOfWay1;
    private ObjectId _moveOutOfWay2;
    private LocomotorGoalType _locomotorGoalType;
    private Vector3 _locomotorGoalData;
    private WhichTurretType _turretSyncFlag;
    private AttitudeType _attitude;
    private LogicFrame _nextMoodCheckTime;
    private ObjectId _crateCreated;

    public ObjectId IgnoredObstacleID => _ignoreObstacleId;

    /// <summary>
    /// A list of positions along the path to the current target point. "Path" as in pathfinding, not waypoint path.
    /// </summary>
    public List<Vector3> TargetPoints { get; set; }

    /// <summary>WI-N6-01: movement liveness tracking for the current order; detection only.</summary>
    public MovementProgress Progress { get; } = new();

    public float CurrentLocomotorSpeed
    {
        get
        {
            if (CurrentLocomotor != null)
            {
                return CurrentLocomotor.GetMaxSpeedForCondition(GameObject.BodyModule.DamageState);
            }

            Logger.Info("No current locomotor!");
            return 0.0f;
        }
    }

    protected override UpdateOrder UpdateOrder => UpdateOrder.Order0;

    // TODO(Port): Implement this.
    public bool IsMoving => false;

    public virtual bool IsIdle
    {
        get
        {
            var stateMachine = StateMachine;
            return stateMachine.CurrentStateId == AIStateIds.Idle || stateMachine.IsInIdleState;
        }
    }

    internal AIUpdate(GameObject gameObject, IGameEngine gameEngine, AIUpdateModuleData moduleData)
        : base(gameObject, gameEngine)
    {
        _attackMoveCandidateFilter = IsAttackMoveCandidate;
        ModuleData = moduleData;

        TargetPoints = new List<Vector3>();

        _locomotorSet = new LocomotorSet(gameObject, gameEngine);
        _currentLocomotorSetType = LocomotorSetType.Invalid;
        // StateMachine.InitializeDefaultState();

        SetLocomotor(LocomotorSetType.Normal);

        // We specifically use the moduleData argument and not the property here, because the property is virtual
        // and might be overridden in a derived class, causing it to be unintialised despite the assignment above.
        if (moduleData.Turret != null)
        {
            _turretAIUpdate = moduleData.Turret.CreateTurretAIUpdate(gameObject, gameEngine);
        }
    }

    /// <summary>
    /// Whether the turret (if any) is trained on the current target, including pitch
    /// (e.g. the SCUD launcher's erector arm reaching FirePitch). Weapons on
    /// turret-controlled slots wait for this before firing so projectiles launch from the
    /// aimed launch bone. Always true for objects without a turret.
    /// </summary>
    internal bool IsTurretAimed => _turretAIUpdate?.IsAimed ?? true;

    private protected virtual AIUpdateStateMachine CreateStateMachine()
    {
        var result = new AIUpdateStateMachine(GameObject, GameEngine, this);
        result.InitializeDefaultState();
        return result;
    }

    internal void SetLocomotor(LocomotorSetType type)
    {
        if (_currentLocomotorSetType == type)
        {
            return;
        }

        if (!GameObject.Definition.LocomotorSets.TryGetValue(type, out var locomotorSetTemplate))
        {
            return;
        }

        _locomotorSet.Reset();
        _locomotorSet.Initialize(locomotorSetTemplate);

        // TODO: Use actual surface type.
        CurrentLocomotor = _locomotorSet.GetLocomotorForSurfaces(Surfaces.Ground)
            ?? _locomotorSet.GetLocomotorForSurfaces(Surfaces.Air);

        // Taxiing aircraft drive over the airfield structure footprint, which is
        // impassable ground for the pathfinder (runway and parking bones included).
        CurrentLocomotor?.SetAllowInvalidPosition(type == LocomotorSetType.Taxiing);
    }

    public bool HasLocomotorForSurface(Surfaces surfaceType)
    {
        return _locomotorSet.GetLocomotorForSurfaces(surfaceType) != null;
    }

    internal void AddTargetPoint(in Vector3 targetPoint)
    {
        TargetPoints.Add(targetPoint);

        GameObject.SetModelConditionState(ModelConditionFlag.Moving);
    }

    internal void AppendPathToTargetPoint(in Vector3 targetPoint, PathRequestOrigin origin = PathRequestOrigin.Unit, bool asyncEligible = false)
    {
        if (GameObject.Definition.KindOf == null)
        {
            return;
        }

        Progress.Reset();
        ResetLocalRepath();

        if (!GameObject.Definition.KindOf.Get(ObjectKinds.Aircraft) && targetPoint != GameObject.Translation)
        {
            if (GameEngine.Navigation == null)
            {
                AddTargetPoint(targetPoint);
                return;
            }

            // WI-N4-02: order-driven paths may compute on the async queue; workflow movement
            // (scripts, dozer, supply, aircraft) passes asyncEligible false or opts out below.
            if (asyncEligible && SupportsAsyncOrderPaths && GameEngine.Navigation.Options.AsyncOrderPaths)
            {
                RequestPathAsync(targetPoint);
                return;
            }

            var start = GameObject.Translation;
            var path = GameEngine.Navigation.CalculatePath(start, targetPoint, out var endIsPassable, origin);
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
            GameObject.SetModelConditionState(ModelConditionFlag.Moving);
        }
        else
        {
            AddTargetPoint(targetPoint);
        }
    }

    // WI-N4-01: pending async path request. Not persisted in save files; a load drops it, so a
    // loaded unit re-issues movement through its next order.
    private PathRequestHandle _pendingPathHandle;
    private Vector3 _pendingPathTarget;

    // WI-N8-01: blocked-unit resolver bookkeeping. Attempts reset when the unit progresses or
    // takes a new order; at most one attempt fires per BlockedWaitFrames interval.
    private const int NoLocalRepathAttempt = -1;
    private int _localRepathAttempts;
    private int _framesAtLastLocalRepathAttempt = NoLocalRepathAttempt;

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

        if (GameObject.Definition.KindOf == null
            || GameObject.Definition.KindOf.Get(ObjectKinds.Aircraft)
            || targetPoint == GameObject.Translation
            || GameEngine.Navigation == null)
        {
            AddTargetPoint(targetPoint);
            return;
        }

        _pendingPathTarget = targetPoint;
        _pendingPathHandle = GameEngine.Navigation.SubmitPathRequest(
            GameObject.Translation, targetPoint, GameObject, OnPendingPathCompleted);
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

        GameObject.SetModelConditionState(ModelConditionFlag.Moving);
    }

    /// <summary>
    /// WI-N7-01: terminal no-route state. The order can produce no route (failed search,
    /// worker death, empty async completion), so the route and moving state are cleared and the
    /// stall reason records NoRoute for attribution.
    /// </summary>
    internal void SettleNoRoute()
    {
        TargetPoints.Clear();
        GameObject.ModelConditionFlags.Set(ModelConditionFlag.Moving, false);
        GameObject.Speed = 0;
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
        GameObject.ModelConditionFlags.Set(ModelConditionFlag.Moving, false);
        GameObject.Speed = 0;
        Progress.RecordGoalCrowded();
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
        GameObject.SetModelConditionState(ModelConditionFlag.Moving);
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
    /// synchronous search; a failed fallback settles the unit. Called from <see cref="Update"/>
    /// each tick while a handle is pending.
    /// </summary>
    internal void CheckPendingPathTimeout()
    {
        if (_pendingPathHandle == null)
        {
            return;
        }

        var navigation = GameEngine.Navigation;
        if (navigation == null
            || _pendingPathHandle.WaitedFrames <= navigation.Options.PendingPathTimeoutFrames)
        {
            return;
        }

        var target = _pendingPathTarget;
        CancelPendingPath();

        var path = navigation.CalculatePath(
            GameObject.Translation, target, out var endIsPassable, PathRequestOrigin.Unit);
        if (path.Count == 0)
        {
            SettleNoRoute();
            return;
        }

        ApplyRoute(path, endIsPassable, target);
    }

    private void CancelPendingPath()
    {
        _pendingPathHandle?.Cancel();
        _pendingPathHandle = null;
    }

    /// <summary>
    /// Computes an approach point on the perimeter of <paramref name="buildTarget"/>'s bounding sphere,
    /// in the direction from the building toward this unit. The returned point is reachable once the
    /// building is constructed (i.e. it is outside the building footprint).
    /// </summary>
    protected Vector3 ComputePerimeterApproachPoint(GameObject buildTarget)
    {
        var buildingCenter = buildTarget.Translation;
        var toSelf = GameObject.Translation - buildingCenter;

        Vector2 dir2D;
        if (toSelf.X * toSelf.X + toSelf.Y * toSelf.Y > 0.01f)
        {
            var len = MathF.Sqrt(toSelf.X * toSelf.X + toSelf.Y * toSelf.Y);
            dir2D = new Vector2(toSelf.X / len, toSelf.Y / len);
        }
        else
        {
            // Dozer is on top of the building center; fall back to the building's forward axis.
            var forward = Vector3.Transform(Vector3.UnitX, buildTarget.Rotation);
            var len = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
            dir2D = len > 0.001f ? new Vector2(forward.X / len, forward.Y / len) : Vector2.UnitX;
        }

        var buildingRadius = buildTarget.RoughCollider?.WorldBounds.Radius ?? 0f;
        return new Vector3(
            buildingCenter.X + dir2D.X * buildingRadius,
            buildingCenter.Y + dir2D.Y * buildingRadius,
            buildingCenter.Z);
    }

    /// <summary>
    /// Whether order-driven movement may use the async path queue (WI-N4-02). Workflow
    /// subclasses that drive their own movement state machines opt out to keep synchronous
    /// paths: dozer, supply, hacker, and jet updates.
    /// </summary>
    internal virtual bool SupportsAsyncOrderPaths => true;

    internal virtual void SetTargetPoint(Vector3 targetPoint, bool asyncEligible = false)
    {
        ResetMovementIntent();
        if (GameObject.ParentHorde != null)
        {
            return;
        }
        else if (GameObject.Definition.KindOf.Get(ObjectKinds.Horde))
        {
            var targetDirection = targetPoint - GameObject.Translation;
            GameObject.FindBehavior<HordeContainBehavior>()?.SetTargetPoints(targetPoint, targetDirection);
        }

        TargetPoints.Clear();
        AppendPathToTargetPoint(targetPoint, asyncEligible: asyncEligible);
    }

    internal void ConfigureAttackMove(bool enabled)
    {
        // Aircraft retain their specialized taxi, takeoff, and attack movement.
        _isAttackPath = enabled && this is not JetAIUpdate;
        _nextAttackMoveSearchFrame = 0;
        _ignoreWeaponTargetForMovement = !enabled;
    }

    internal void ResetMovementIntent()
    {
        // WI-N4-01: a new movement intent invalidates any pending async path.
        CancelPendingPath();
        _isAttackPath = false;
        _ignoreWeaponTargetForMovement = false;
    }

    private void AcquireAttackMoveTarget()
    {
        var weapon = GameObject.CurrentWeapon;
        if (!_isAttackPath || weapon == null)
        {
            return;
        }

        if (weapon.CurrentTarget?.TargetType == WeaponTargetType.Object
            && (weapon.CurrentTarget.IsDestroyed || weapon.CurrentTarget.GetTargetObject()?.IsEffectivelyDead == true))
        {
            weapon.SetTarget(null);
        }

        if (weapon.HasValidTarget)
        {
            return;
        }

        var frame = GameEngine.GameLogic.CurrentFrame.Value;
        if (frame < _nextAttackMoveSearchFrame)
        {
            return;
        }

        // Align repeat scans to a stable per-object phase instead of synchronizing the army.
        var phase = GameObject.Id.Index % AttackMoveSearchIntervalFrames;
        _nextAttackMoveSearchFrame = frame + AttackMoveSearchIntervalFrames
            - (frame + AttackMoveSearchIntervalFrames - phase) % AttackMoveSearchIntervalFrames;

        GameObject nearest = null;
        var nearestDistance = float.MaxValue;
        var enemies = GameObject.Owner?.Enemies;
        if (enemies == null)
        {
            return;
        }

        var translation = GameObject.Translation;
        foreach (var candidate in GameEngine.Quadtree.FindNearby(GameObject, GameObject.Transform, weapon.Template.AttackRange, _attackMoveCandidateFilter))
        {
            var distance = Vector3.DistanceSquared(translation, candidate.Translation);
            if (distance < nearestDistance && weapon.IsObjectInRange(candidate))
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        if (nearest != null)
        {
            weapon.SetTarget(new WeaponTarget(GameEngine.Scene3D.GameObjects, nearest.Id));
        }
    }

    private bool IsAttackMoveCandidate(GameObject candidate) => !candidate.IsEffectivelyDead
        && candidate.Owner != null
        && GameObject.Owner?.Enemies.Contains(candidate.Owner) == true
        && GameObject.CurrentWeapon.Template.AntiMask.CanAttackObject(candidate);

    internal void FollowWaypoints(IEnumerable<Vector3> waypoints)
    {
        ResetMovementIntent();
        TargetPoints.Clear();
        _waypointEnumerator = waypoints.GetEnumerator();
        MoveToNextWaypointOrStop();
    }

    protected virtual void ArrivedAtDestination()
    {
    }

    /// <summary>
    /// Ceases all behavior by the unit, like when the stop button is pressed.
    /// </summary>
    internal virtual void Stop()
    {
        ResetMovementIntent();
        StopMovingOnly();
    }

    /// <summary>
    /// Stops moving, but does not clear any additional state machine behavior that might be running (e.g. construction, supply gathering, etc)
    /// </summary>
    private void StopMovingOnly()
    {
        _isAttackPath = false;
        GameObject.ModelConditionFlags.Set(ModelConditionFlag.Moving, false);
        _waypointEnumerator?.Dispose();
        _waypointEnumerator = null;
        TargetPoints.Clear();
        GameObject.Speed = 0;
        Progress.Reset();
        ResetLocalRepath();
    }

    internal void SetTargetDirection(Vector3 targetDirection)
    {
        _targetDirection = targetDirection;
    }

    /// <summary>
    /// If the unit is currently following a waypoint path, set the next waypoint as target, otherwise stop.
    /// </summary>
    /// <remarks>
    /// It might be necessary to select a path randomly (if there are branches), so this method should only
    /// be called from <see cref="LogicTick"/>.</remarks>
    private void MoveToNextWaypointOrStop()
    {
        if (_waypointEnumerator != null && _waypointEnumerator.MoveNext())
        {
            AddTargetPoint(_waypointEnumerator.Current);
        }
        else
        {
            ArrivedAtDestination();
            StopMovingOnly();
        }
    }

    public void AIEvacuateInstantly(bool exposeStealthUnits, CommandSourceType commandSource)
    {
        // TODO(Port): Implement this.
    }

    public void AIIdle(CommandSourceType commandSource)
    {
        // TODO(Port): Implement this.
    }

    public override UpdateSleepTime Update()
    {
        if (_pendingPathHandle != null)
        {
            Progress.RecordAwaitingPath();
            CheckPendingPathTimeout();
        }

        StateMachine.Update();
        AcquireAttackMoveTarget();

        // Turrets discard targets while moving, so pause before updating their aim.
        if (_isAttackPath && GameObject.CurrentWeapon?.HasValidTarget == true)
        {
            GameObject.ModelConditionFlags.Set(ModelConditionFlag.Moving, false);
            GameObject.Speed = 0;
        }

        if (_turretAIUpdate != null)
        {
            _turretAIUpdate.Update(ModuleData.AutoAcquireEnemiesWhenIdle);
        }
        else
        {
            var target = GameObject.CurrentWeapon?.CurrentTarget;
            if (target != null && !target.IsDestroyed)
            {
                var directionToTarget = (target.TargetPosition - GameObject.Translation);
                SetTargetDirection(directionToTarget);
            }
        }

        if (GameObject.ModelConditionFlags.Get(ModelConditionFlag.Moving))
        {
            GameEngine.Quadtree.Update(GameObject);
        }

        if (CurrentLocomotor != null)
        {
            CurrentLocomotor.SetPhysicsOptions(GameObject);

            if (TargetPoints.Count > 0)
            {
                // If the current weapon target is already in range, stop moving so the unit can fire.
                if (!_ignoreWeaponTargetForMovement && GameObject.CurrentWeapon?.HasValidTarget == true)
                {
                    // Attack-move pauses above without discarding the remaining route.
                    if (!_isAttackPath)
                    {
                        StopMovingOnly();
                    }
                }
                else
                {
                    GameObject.ModelConditionFlags.Set(ModelConditionFlag.Moving, true);
                    Vector3? nextPoint = null;

                    if (TargetPoints.Count > 1)
                    {
                        nextPoint = TargetPoints[1];
                    }

                    // TODO(Port): Use correct parameter values.
                    var blocked = false;
                    var toTarget = TargetPoints[0] - GameObject.Translation;
                    // onPathDistToGoal governs the locomotor's braking window and must always be
                    // horizontal-only: Locomotor's own braking math (and e.g.
                    // MoveTowardsPositionWings) operates in 2D, with altitude handled separately
                    // via PreferredHeight. Passing a 3D distance here inflates the value with
                    // altitude, so the braking check never engages and the unit overshoots the
                    // goal and loops back. CloseEnoughDist3D only affects the arrival check below.
                    var onPathDistToGoal = toTarget.Vector2XY().Length();
                    CurrentLocomotor.LocoUpdateMoveTowardsPosition(
                        GameObject,
                        TargetPoints[0],
                        onPathDistToGoal,
                        CurrentLocomotor.GetMaxSpeedForCondition(GameObject.BodyModule.DamageState),
                        ref blocked,
                        out var blocker);

                    // WI-N6-01: the distance was sampled before the physics step, so this
                    // compares consecutive move steps toward the same waypoint.
                    Progress.RecordMoveStep(onPathDistToGoal, blocked, blocker);

                    if (Progress.Reason == NavigationStallReason.None)
                    {
                        ResetLocalRepath();
                    }
                    else
                    {
                        var resolutionNav = GameEngine.Navigation;
                        if (resolutionNav != null
                            && resolutionNav.Options.BlockedUnitResolution
                            && Progress.Reason == NavigationStallReason.BlockedByUnit
                            && Progress.FramesWithoutProgress >= resolutionNav.Options.BlockedWaitFrames)
                        {
                            // WI-N8-01: bounded local repath around a non-progressing blocker.
                            BlockedMovementResolver.TryResolve(GameObject, this, resolutionNav);
                        }
                    }

                    var use3DArrival = CurrentLocomotor.LocomotorTemplate.CloseEnoughDist3D;
                    var distToTargetSq = use3DArrival
                        ? toTarget.LengthSquared()
                        : toTarget.Vector2XY().LengthSquared();
                    var closeEnoughDist = CurrentLocomotor.LocomotorTemplate.CloseEnoughDist;
                    var reachedPosition = distToTargetSq < closeEnoughDist * closeEnoughDist;

                    // this should be moved to LogicTick
                    if (reachedPosition)
                    {
                        Logger.Debug($"Reached point {TargetPoints[0]}");
                        TargetPoints.RemoveAt(0);
                        Progress.Reset();
                        if (TargetPoints.Count == 0)
                        {
                            MoveToNextWaypointOrStop();
                        }
                    }
                }
            }
            else if (_targetDirection.HasValue)
            {
                var targetYaw = MathUtility.GetYawFromDirection(new Vector2(_targetDirection.Value.X, _targetDirection.Value.Y));
                CurrentLocomotor.LocoUpdateMoveTowardsAngle(GameObject, targetYaw);

                // TODO(Port): This isn't right.
                var reachedAngle = MathUtility.CalculateAngleDelta(targetYaw, GameObject.Yaw) < 0.1f;

                if (!reachedAngle)
                {
                    // TODO(Port): Use correct value.
                    return UpdateSleepTime.None;
                }

                _targetDirection = null;
                Stop();
            }
            else
            {
                // maintain position (jets etc)
                CurrentLocomotor.LocoUpdateMaintainCurrentPosition(GameObject);
            }
        }

        // TODO(Port): Use correct value.
        return UpdateSleepTime.None;
    }

    internal override void DrawInspector()
    {
        base.DrawInspector();

        if (ImGui.TreeNodeEx("Target points"))
        {
            for (var i = 0; i < TargetPoints.Count; i++)
            {
                var targetPoint = TargetPoints[i];
                if (ImGui.InputFloat3("Target point", ref targetPoint))
                {
                    TargetPoints[i] = targetPoint;
                }
            }

            ImGui.TreePop();
        }

        ImGui.SeparatorText("Locomotor");

        CurrentLocomotor?.DrawInspector();
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(4);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        reader.PersistUInt32(ref _priorWaypointId);
        reader.PersistUInt32(ref _currentWaypointId);
        reader.PersistObject(StateMachine);
        reader.PersistBoolean(ref _isAIDead);
        reader.PersistBoolean(ref _isRecruitable);
        reader.PersistUInt32(ref _nextEnemyScanTime);
        reader.PersistObjectId(ref _currentVictimId);
        reader.PersistSingle(ref _desiredSpeed);
        reader.PersistEnum(ref _lastCommandSource);
        reader.PersistEnum(ref _guardTargetType0);

        // Original game accidentally used 8 bytes for _guardTargetType0.
        // The extra 4 bytes are actually the same value as _guardTargetType1.
        var garbageBytes0 = 0;
        reader.PersistInt32(ref garbageBytes0);

        reader.PersistEnum(ref _guardTargetType1);

        // Original game accidentally used 8 bytes for _guardTargetType1.
        // These 4 bytes contain whatever happens to be in the 4 bytes of memory
        // following _guardTargetType1... which is the first float value in
        // m_locationToGuard.
        var garbageBytes1 = 0;
        reader.PersistInt32(ref garbageBytes1);

        reader.PersistVector3(ref _locationToGuard);
        reader.PersistObjectId(ref _objectToGuard);

        var triggerName = _areaToGuard?.Name ?? "";
        reader.PersistAsciiString(ref triggerName, "AreaToGuard");
        if (reader.Mode == StatePersistMode.Read && triggerName != "")
        {
            _areaToGuard = GameEngine.Scene3D.MapFile.PolygonTriggers.GetPolygonTriggerByName(triggerName);
        }

        var attackName = _attackInfo?.Name ?? "";
        reader.PersistAsciiString(ref attackName);
        if (reader.Mode == StatePersistMode.Read && attackName != "")
        {
            _attackInfo = GameEngine.Game.Scripting.GetAttackInfo(attackName);
        }

        reader.PersistListWithUInt32Count(
            _waypointQueue,
            static (StatePersister persister, ref Vector3 item) =>
            {
                persister.PersistVector3(ref item);
            });

        reader.PersistInt32(ref _waypointIndex);
        reader.PersistBoolean(ref _executingWaypointQueue);

        var completedWaypointId = (uint)(_completedWaypoint?.ID ?? (int)Waypoint.InvalidId);
        reader.PersistUInt32(ref completedWaypointId);
        if (reader.Mode == StatePersistMode.Read)
        {
            GameEngine.Scene3D.Waypoints.TryGetById((int)completedWaypointId, out _completedWaypoint);
        }

        reader.PersistBoolean(ref _waitingForPath);

        var hasPath = _path != null;
        reader.PersistBoolean(ref hasPath);
        if (hasPath)
        {
            _path ??= new PathfindingPath();
            reader.PersistObject(_path);
        }

        reader.PersistObjectId(ref _requestedVictimId);
        reader.PersistVector3(ref _requestedDestination);
        reader.PersistVector3(ref _requestedDestination2);

        reader.PersistObjectId(ref _ignoreObstacleId);
        reader.PersistSingle(ref _pathExtraDistance);
        reader.PersistPoint2D(ref _pathfindGoalCell);
        reader.PersistPoint2D(ref _pathfindCurrentCell);
        reader.PersistLogicFrame(ref _ignoreCollisionsUntil);
        reader.PersistLogicFrame(ref _queueForPathFrame);

        reader.PersistVector3(ref _finalPosition);
        reader.PersistBoolean(ref _doFinalPosition);

        reader.PersistBoolean(ref _isAttackPath);
        reader.PersistBoolean(ref _isFinalGoal);
        reader.PersistBoolean(ref _isApproachPath);
        reader.PersistBoolean(ref _isSafePath);
        reader.PersistBoolean(ref _movementComplete);
        reader.PersistBoolean(ref _isSafePath); // Yes, an exact copy of two lines above
        reader.PersistBoolean(ref _upgradedLocomotors);
        reader.PersistBoolean(ref _canPathThroughUnits);
        reader.PersistBoolean(ref _randomlyOffsetMoodCheck);

        reader.PersistObjectId(ref _repulsor1);
        reader.PersistObjectId(ref _repulsor2);

        reader.PersistObjectId(ref _moveOutOfWay1);
        reader.PersistObjectId(ref _moveOutOfWay2);

        reader.PersistObject(_locomotorSet);

        var currentLocomotorTemplateName = CurrentLocomotor?.LocomotorTemplate.Name ?? "";
        reader.PersistAsciiString(ref currentLocomotorTemplateName);
        if (reader.Mode == StatePersistMode.Read)
        {
            CurrentLocomotor = currentLocomotorTemplateName != ""
                ? _locomotorSet.GetLocomotor(currentLocomotorTemplateName)
                : null;
        }

        reader.PersistEnum(ref _currentLocomotorSetType);
        reader.PersistEnum(ref _locomotorGoalType);
        reader.PersistVector3(ref _locomotorGoalData);

        if (ModuleData.Turret != null)
        {
            reader.PersistObject(_turretAIUpdate, "TurretAI");
        }

        reader.PersistEnum(ref _turretSyncFlag);
        reader.PersistEnum(ref _attitude);
        reader.PersistLogicFrame(ref _nextMoodCheckTime);
        reader.PersistObjectId(ref _crateCreated);
    }

    private enum LocomotorGoalType
    {
        None = 0,
        PositionOnPath = 1,
        PositionExplicit = 2,
        Angle = 3,
    }

    private enum WhichTurretType
    {
        Invalid = -1,
        Main = 0,
        Alt = 1,
    }
}

public class AIUpdateModuleData : UpdateModuleData
{
    internal static AIUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    internal static readonly IniParseTable<AIUpdateModuleData> FieldParseTable = new IniParseTable<AIUpdateModuleData>
    {
        { "Turret", (parser, x) => x.Turret = TurretAIUpdateModuleData.Parse(parser) },
        { "AltTurret", (parser, x) => x.AltTurret = TurretAIUpdateModuleData.Parse(parser) },
        { "TurretsLinked", (parser, x) => x.TurretsLinked = parser.ParseBoolean() },
        { "AutoAcquireEnemiesWhenIdle", (parser, x) => x.AutoAcquireEnemiesWhenIdle = parser.ParseEnumBitArray<AutoAcquireEnemiesType>() },
        { "MoodAttackCheckRate", (parser, x) => x.MoodAttackCheckRate = parser.ParseInteger() },
        { "ForbidPlayerCommands", (parser, x) => x.ForbidPlayerCommands = parser.ParseBoolean() },
        { "AILuaEventsList", (parser, x) => x.AILuaEventsList = parser.ParseString() },
        { "HoldGroundCloseRangeDistance", (parser, x) => x.HoldGroundCloseRangeDistance = parser.ParseInteger() },
        { "MinCowerTime", (parser, x) => x.MinCowerTime = parser.ParseInteger() },
        { "MaxCowerTime", (parser, x) => x.MaxCowerTime = parser.ParseInteger() },
        { "CanAttackWhileContained", (parser, x) => x.CanAttackWhileContained = parser.ParseBoolean() },
        { "RampageTime", (parser, x) => x.RampageTime = parser.ParseInteger() },
        { "TimeToEjectPassengersOnRampage", (parser, x) => x.TimeToEjectPassengersOnRampage = parser.ParseInteger() },
        { "AttackPriority", (parser, x) => x.AttackPriority = parser.ParseString() },
        { "SpecialContactPoints", (parser, x) => x.SpecialContactPoints = parser.ParseEnumBitArray<ContactPointType>() },
        { "FadeOnPortals", (parser, x) => x.FadeOnPortals = parser.ParseBoolean() },
        { "StopChaseDistance", (parser, x) => x.StopChaseDistance = parser.ParseInteger() },
        { "RampageRequiresAflame", (parser, x) => x.RampageRequiresAflame = parser.ParseBoolean() },
        { "MoveForNoOne", (parser, x) => x.MoveForNoOne = parser.ParseBoolean() },
        { "StandGround", (parser, x) => x.StandGround = parser.ParseBoolean() },
        { "BurningDeathTime", (parser, x) => x.BurningDeathTime = parser.ParseInteger() }
    };

    /// <summary>
    /// Allows the use of TurretMoveStart and TurretMoveLoop within the UnitSpecificSounds
    /// section of the object.
    /// </summary>
    public TurretAIUpdateModuleData Turret { get; private set; }

    public TurretAIUpdateModuleData AltTurret { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public bool TurretsLinked { get; private set; }

    public BitArray<AutoAcquireEnemiesType> AutoAcquireEnemiesWhenIdle { get; private set; }
    public int MoodAttackCheckRate { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public bool ForbidPlayerCommands { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public string AILuaEventsList { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public int HoldGroundCloseRangeDistance { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public int MinCowerTime { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public int MaxCowerTime { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool CanAttackWhileContained { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public int RampageTime { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public int TimeToEjectPassengersOnRampage { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public string AttackPriority { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public BitArray<ContactPointType> SpecialContactPoints { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool FadeOnPortals { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public int StopChaseDistance { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool RampageRequiresAflame { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool MoveForNoOne { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool StandGround { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public int BurningDeathTime { get; private set; }

    internal override BehaviorModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new AIUpdate(gameObject, gameEngine, this);
    }
}

public enum AutoAcquireEnemiesType
{
    [IniEnum("YES")]
    Yes,

    [IniEnum("NO")]
    No,

    /// <summary>
    /// Attack buildings in addition to units.
    /// </summary>
    [IniEnum("ATTACK_BUILDINGS")]
    AttackBuildings,

    /// <summary>
    /// Don't counter-attack.
    /// </summary>
    [IniEnum("NotWhileAttacking")]
    NotWhileAttacking,

    [IniEnum("Stealthed")]
    Stealthed,
}

public abstract class BaseAITargetChooserData
{

}

public sealed class UnitAITargetChooserData : BaseAITargetChooserData
{

}

internal sealed class PathfindingPath : IPersistableObject
{
    private readonly List<PathPoint> _points = new();
    private bool _unknownBool1;
    private uint _unknownInt2;
    private bool _unknownBool2;

    public void Persist(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.PersistListWithUInt32Count(
            _points,
            static (StatePersister persister, ref PathPoint item) =>
            {
                persister.PersistObjectValue(ref item);
            });

        reader.PersistBoolean(ref _unknownBool1);
        reader.SkipUnknownBytes(4);
        reader.PersistUInt32(ref _unknownInt2); // 1
        reader.PersistBoolean(ref _unknownBool2);
    }
}

internal struct PathPoint : IPersistableObject
{
    private uint _id;
    private Vector3 _position;
    private uint _unknownInt1;
    private bool _unknownBool1;
    private uint _nextId;

    public void Persist(StatePersister reader)
    {
        reader.PersistUInt32(ref _id);
        reader.PersistVector3(ref _position);
        reader.PersistUInt32(ref _unknownInt1);
        reader.PersistBoolean(ref _unknownBool1);
        reader.PersistUInt32(ref _nextId);
    }
}

public enum GuardTargetType
{
    /// <summary>
    /// Guard a location.
    /// </summary>
    Location = 0,

    /// <summary>
    /// Guard an object.
    /// </summary>
    Object = 1,

    /// <summary>
    /// Guard a polygon trigger.
    /// </summary>
    Area = 2,

    /// <summary>
    /// Currently not guarding.
    /// </summary>
    None = 3,
}

public enum AttitudeType
{
    Sleep = -2,
    Passive = -1,
    Normal = 0,
    Alert = 1,
    Aggressive = 2,
    Invalid = 3,
}
