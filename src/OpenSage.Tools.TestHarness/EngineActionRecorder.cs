using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Subscribes to low-level <see cref="IGame"/> events and records every engine <see cref="Order"/>
/// (and its resolved outcome) to an <see cref="ActionResultsWriter"/>, to support diffing two
/// identically-configured runs for parity.
///
/// Unlike <see cref="OpenSage.Tools.ReplaySketch.Services.ReplayActionMonitor"/> (which re-raises
/// orders as a coarse, scenario-level <c>ActionType</c> vocabulary for deferred-action triggering),
/// this recorder logs the raw engine command with resolved vectors/positions, for every
/// <see cref="OrderType"/>:
/// <list type="bullet">
///   <item>Move: a Started entry per unit (position/direction/speed/target) and a Completed entry
///     when the unit arrives (detected by polling <see cref="AIUpdate.TargetPoints"/> emptying —
///     there is no engine "arrived" event).</item>
///   <item>Attack (object-targeted): a Started entry per attacking unit and a Completed entry when
///     the target is destroyed.</item>
///   <item>ForceAttackGround: a single Dispatched entry per affected unit (no lifecycle tracking —
///     this order is a move-then-maybe-attack composite).</item>
///   <item>Build / CreateUnit: a Started entry and a Completed entry (FIFO-matched by
///     player+definition against <c>ConstructionCompleted</c>/<c>ObjectCreated</c>).</item>
///   <item>Everything else: a single Dispatched entry with the order's raw arguments, so no order
///     type is silently missing from parity coverage.</item>
/// </list>
///
/// It also tracks general object lifecycle/state, independent of orders: a Created entry when a
/// player-owned object enters the scene, a Destroyed entry when one leaves, and a Changed entry
/// whenever a tracked object's health changes (detected by polling, since there's no engine
/// "health changed" event — see <see cref="PollHealthChanges"/>).
///
/// Finally it tracks the supply gather/dump cycle of every <see cref="SupplyAIUpdate"/> unit (the
/// Chinook, supply trucks, workers): a State entry per supply-state transition, and ApproachStart /
/// ApproachArrived entries per approach leg carrying travel frames, closest approach, peak speed,
/// and an overshoot count (the numeric signal for a unit that overshoots its dock and loops back).
/// See <see cref="PollSupplyCycles"/>.
/// </summary>
public sealed class EngineActionRecorder : IDisposable
{
    private const string ObjectLifecycleCategory = "ObjectLifecycle";
    private const string HealthCategory = "Health";
    private const string SupplyCycleCategory = "SupplyCycle";

    private readonly IGame _game;
    private readonly ActionResultsWriter _writer;

    private readonly HashSet<ObjectId> _pendingMoves = new();
    private readonly Dictionary<ObjectId, HashSet<ObjectId>> _pendingAttacksByTarget = new();
    private readonly Dictionary<(uint PlayerIndex, string DefinitionName), Queue<int>> _pendingBuilds = new();
    private readonly Dictionary<(uint PlayerIndex, string DefinitionName), Queue<int>> _pendingCreateUnits = new();
    private readonly Dictionary<ObjectId, float> _lastHealth = new();
    private readonly Dictionary<ObjectId, SupplyCycleTracker> _supplyTrackers = new();

    public EngineActionRecorder(IGame game, ActionResultsWriter writer)
    {
        _game = game;
        _writer = writer;

        _game.OrderProcessed += OnOrderProcessed;
        _game.ObjectCreated += OnObjectCreated;
        _game.ObjectDestroyed += OnObjectDestroyed;
        _game.ConstructionCompleted += OnConstructionCompleted;

        // Objects placed by the map/scenario at load time predate this recorder's subscription,
        // so ObjectCreated never fires for them. Seed their health so PollHealthChanges has a
        // baseline instead of reporting a spurious initial "change" for every one of them.
        if (_game.Scene3D != null)
        {
            foreach (var obj in _game.Scene3D.GameObjects.Objects)
            {
                if (obj.Owner != null && obj.BodyModule != null)
                {
                    _lastHealth[obj.Id] = obj.BodyModule.Health;
                }
            }
        }
    }

    /// <summary>
    /// Polls for completion of in-flight moves. Call once per main-loop iteration — there is no
    /// engine event for "unit finished moving" (<see cref="AIUpdate.IsMoving"/> is a hardcoded
    /// stub), so this is the only available signal.
    /// </summary>
    public void PollPendingMoves()
    {
        if (_pendingMoves.Count == 0)
        {
            return;
        }

        List<ObjectId>? arrived = null;

        foreach (var unitId in _pendingMoves)
        {
            var unit = _game.Scene3D?.GameObjects.GetObjectById(unitId);
            if (unit == null)
            {
                // Destroyed mid-move; ObjectDestroyed already logged an Aborted entry and will
                // remove this id from the set. Nothing more to do here.
                continue;
            }

            if (unit.AIUpdate != null && unit.AIUpdate.TargetPoints.Count == 0)
            {
                (arrived ??= new List<ObjectId>()).Add(unitId);
                Write("Completed", OrderType.MoveTo, unit.Owner, $"unit={unitId.Index} pos={Fmt(unit.Translation)}");
            }
        }

        if (arrived != null)
        {
            foreach (var unitId in arrived)
            {
                _pendingMoves.Remove(unitId);
            }
        }
    }

    /// <summary>
    /// Polls all tracked player-owned objects for health changes and logs a Changed entry for
    /// each one whose health differs from the last observed value. Call once per main-loop
    /// iteration alongside <see cref="PollPendingMoves"/> — there is no engine "health changed"
    /// event, so polling is the only available signal.
    /// </summary>
    public void PollHealthChanges()
    {
        var objects = _game.Scene3D?.GameObjects.Objects;
        if (objects == null)
        {
            return;
        }

        foreach (var obj in objects)
        {
            if (obj.Owner == null || obj.BodyModule == null)
            {
                continue;
            }

            var health = obj.BodyModule.Health;

            // Objects under construction gain health each tick as build progress advances
            // (GameObject.AdvanceConstruction). That is build progress, not a damage/heal
            // event, so don't log it -- but keep the baseline tracking silently so the
            // transition to full health on completion doesn't read as a spurious change.
            if (obj.TestStatus(ObjectStatus.UnderConstruction))
            {
                _lastHealth[obj.Id] = health;
                continue;
            }

            if (_lastHealth.TryGetValue(obj.Id, out var previous))
            {
                if (health != previous)
                {
                    Write("Changed", HealthCategory, obj.Owner,
                        $"objectId={obj.Id.Index} def={obj.Definition.Name} previous={previous:F1} current={health:F1} max={obj.BodyModule.MaxHealth:F1}");
                    _lastHealth[obj.Id] = health;
                }
            }
            else
            {
                _lastHealth[obj.Id] = health;
            }
        }
    }

    /// <summary>
    /// Polls every player-owned unit with a <see cref="SupplyAIUpdate"/> and records supply-cycle
    /// timing for baseline comparison: each supply-state transition (with its frame), and per
    /// approach-leg telemetry (travel frames, closest horizontal approach, peak speed, and an
    /// overshoot count). The overshoot count is the numeric signal for a unit that misses its
    /// dock and loops back: a clean arrival is 0, each close-in-then-recede reversal increments
    /// it. Call once per main-loop iteration alongside <see cref="PollPendingMoves"/> — there is
    /// no engine event for any of these.
    /// </summary>
    public void PollSupplyCycles()
    {
        var objects = _game.Scene3D?.GameObjects.Objects;
        if (objects == null)
        {
            return;
        }

        foreach (var obj in objects)
        {
            if (obj.Owner == null)
            {
                continue;
            }

            if (obj.AIUpdate is not SupplyAIUpdate supply)
            {
                continue;
            }

            var tracker = GetOrCreateSupplyTracker(obj.Id);
            var currentState = supply.SupplyGatherState;

            if (currentState != tracker.LastState)
            {
                if (tracker.EverSeen)
                {
                    Write("State", SupplyCycleCategory, obj.Owner,
                        $"unit={obj.Id.Index} def={obj.Definition.Name} state={currentState} prev={tracker.LastState}");
                }
                tracker.EverSeen = true;
                tracker.LastState = currentState;
            }

            var isApproach = currentState == SupplyAIUpdate.SupplyGatherStates.ApproachingSupplySource
                             || currentState == SupplyAIUpdate.SupplyGatherStates.ApproachingSupplyTarget;

            if (isApproach)
            {
                var toTarget = currentState == SupplyAIUpdate.SupplyGatherStates.ApproachingSupplyTarget;
                if (tracker.ActiveLeg == null || tracker.ActiveLeg.ToTarget != toTarget)
                {
                    var goal = obj.AIUpdate.TargetPoints.Count > 0
                        ? obj.AIUpdate.TargetPoints[0]
                        : obj.Translation;
                    tracker.ActiveLeg = new ApproachLeg(toTarget, CurrentFrame(), goal);
                    Write("ApproachStart", SupplyCycleCategory, obj.Owner,
                        $"unit={obj.Id.Index} def={obj.Definition.Name} leg={(toTarget ? "Dump" : "Gather")} goal={Fmt(goal)} frame={tracker.ActiveLeg.StartFrame}");
                }
                tracker.ActiveLeg.Sample(obj.Translation, obj.AIUpdate);
            }
            else if (tracker.ActiveLeg != null)
            {
                var leg = tracker.ActiveLeg;
                tracker.ActiveLeg = null;
                var travelFrames = CurrentFrame() - leg.StartFrame;
                Write("ApproachArrived", SupplyCycleCategory, obj.Owner,
                    $"unit={obj.Id.Index} def={obj.Definition.Name} leg={(leg.ToTarget ? "Dump" : "Gather")} travelFrames={travelFrames} overshoots={leg.Overshoots} closest={MathF.Sqrt(leg.ClosestDistSq):F2} maxSpeed={leg.MaxSpeed:F2}");
            }
        }
    }

    private SupplyCycleTracker GetOrCreateSupplyTracker(ObjectId id)
    {
        if (!_supplyTrackers.TryGetValue(id, out var tracker))
        {
            _supplyTrackers[id] = tracker = new SupplyCycleTracker();
        }
        return tracker;
    }

    private void OnOrderProcessed(object? sender, OrderProcessedEventArgs e)
    {
        var order = e.Order;
        var player = e.Player;

        switch (order.OrderType)
        {
            case OrderType.MoveTo:
                RecordMoveTo(order, player);
                break;

            case OrderType.AttackObject:
            case OrderType.ForceAttackObject:
                RecordAttackObject(order, player);
                break;

            case OrderType.ForceAttackGround:
                RecordForceAttackGround(order, player);
                break;

            case OrderType.BuildObject:
                RecordBuildObject(order, player);
                break;

            case OrderType.CreateUnit:
                RecordCreateUnit(order, player);
                break;

            default:
                Write("Dispatched", order.OrderType, player, $"args={order}");
                break;
        }
    }

    private void RecordMoveTo(Order order, Player player)
    {
        var targetPosition = order.Arguments[0].Value.Position;

        foreach (var unit in player.SelectedUnits)
        {
            var pos = unit.Translation;
            var direction = targetPosition - pos;
            if (direction.LengthSquared() > 0f)
            {
                direction = Vector3.Normalize(direction);
            }
            var speed = unit.AIUpdate?.CurrentLocomotorSpeed ?? 0f;

            Write("Started", OrderType.MoveTo, player,
                $"unit={unit.Id.Index} pos={Fmt(pos)} dir={Fmt(direction)} speed={speed:F2} target={Fmt(targetPosition)}");

            _pendingMoves.Add(unit.Id);
        }
    }

    private void RecordAttackObject(Order order, Player player)
    {
        var targetId = order.Arguments[0].Value.ObjectId;
        var target = _game.Scene3D?.GameObjects.GetObjectById(targetId);
        var targetPos = target != null ? Fmt(target.Translation) : "?";

        foreach (var unit in player.SelectedUnits)
        {
            if (!unit.CanAttack)
            {
                continue;
            }

            Write("Started", order.OrderType, player, $"unit={unit.Id.Index} target={targetId.Index} targetPos={targetPos}");

            if (!_pendingAttacksByTarget.TryGetValue(targetId, out var attackers))
            {
                _pendingAttacksByTarget[targetId] = attackers = new HashSet<ObjectId>();
            }
            attackers.Add(unit.Id);
        }
    }

    private void RecordForceAttackGround(Order order, Player player)
    {
        var targetPosition = order.Arguments[0].Value.Position;

        const float searchRadius = 300f;
        var targetObject = _game.Scene3D?.GameObjects.Objects
            .Where(o => o.Owner != null
                && player.Enemies.Contains(o.Owner)
                && Vector3.DistanceSquared(o.Translation, targetPosition) <= searchRadius * searchRadius)
            .MinBy(o => Vector3.DistanceSquared(o.Translation, targetPosition));

        var units = player.SelectedUnits.Count > 0
            ? (IEnumerable<GameObject>)player.SelectedUnits
            : _game.Scene3D?.GameObjects.Objects.Where(u => u.Owner == player && u.CanAttack) ?? Array.Empty<GameObject>();

        foreach (var unit in units)
        {
            if (!unit.CanAttack)
            {
                continue;
            }

            var details = targetObject != null
                ? $"unit={unit.Id.Index} targetPos={Fmt(targetPosition)} target={targetObject.Id.Index}"
                : $"unit={unit.Id.Index} targetPos={Fmt(targetPosition)}";

            Write("Dispatched", order.OrderType, player, details);
        }
    }

    private void RecordBuildObject(Order order, Player player)
    {
        var objectDefinitionId = (uint)order.Arguments[0].Value.Integer;
        var objectDefinition = _game.AssetStore.ObjectDefinitions.GetByInstanceId(objectDefinitionId);
        if (objectDefinition == null)
        {
            return;
        }

        var position = order.Arguments[1].Value.Position;
        var angle = order.Arguments[2].Value.Float;

        Write("Started", OrderType.BuildObject, player, $"def={objectDefinition.Name} pos={Fmt(position)} angle={angle:F2}");

        var key = (player.Id, objectDefinition.Name);
        if (!_pendingBuilds.TryGetValue(key, out var queue))
        {
            _pendingBuilds[key] = queue = new Queue<int>();
        }
        queue.Enqueue(0);
    }

    private void RecordCreateUnit(Order order, Player player)
    {
        var objectDefinitionId = (uint)order.Arguments[0].Value.Integer;
        var objectDefinition = _game.AssetStore.ObjectDefinitions.GetByInstanceId(objectDefinitionId);
        if (objectDefinition == null)
        {
            return;
        }

        Write("Started", OrderType.CreateUnit, player, $"def={objectDefinition.Name}");

        var key = (player.Id, objectDefinition.Name);
        if (!_pendingCreateUnits.TryGetValue(key, out var queue))
        {
            _pendingCreateUnits[key] = queue = new Queue<int>();
        }
        queue.Enqueue(0);
    }

    private void OnObjectCreated(object? sender, GameObjectCreatedEventArgs e)
    {
        var obj = e.GameObject;
        if (obj.Owner == null)
        {
            return;
        }

        var health = obj.BodyModule?.Health ?? 0f;
        _lastHealth[obj.Id] = health;
        Write("Created", ObjectLifecycleCategory, obj.Owner,
            $"objectId={obj.Id.Index} def={obj.Definition.Name} pos={Fmt(obj.Translation)} health={health:F1}");

        var key = (obj.Owner.Id, obj.Definition.Name);
        if (_pendingCreateUnits.TryGetValue(key, out var queue) && queue.Count > 0)
        {
            queue.Dequeue();
            Write("Completed", OrderType.CreateUnit, obj.Owner, $"def={obj.Definition.Name} objectId={obj.Id.Index} pos={Fmt(obj.Translation)}");
        }
    }

    private void OnObjectDestroyed(object? sender, GameObjectDestroyedEventArgs e)
    {
        var obj = e.GameObject;

        _lastHealth.Remove(obj.Id);

        if (obj.Owner != null)
        {
            Write("Destroyed", ObjectLifecycleCategory, obj.Owner,
                $"objectId={obj.Id.Index} def={obj.Definition.Name} pos={Fmt(obj.Translation)}");
        }

        if (_pendingMoves.Remove(obj.Id))
        {
            Write("Aborted", OrderType.MoveTo, obj.Owner, $"unit={obj.Id.Index} pos={Fmt(obj.Translation)}");
        }

        if (_pendingAttacksByTarget.TryGetValue(obj.Id, out var attackers))
        {
            foreach (var attackerId in attackers)
            {
                Write("Completed", OrderType.AttackObject, obj.Owner, $"unit={attackerId.Index} target={obj.Id.Index}");
            }
            _pendingAttacksByTarget.Remove(obj.Id);
        }
    }

    private void OnConstructionCompleted(object? sender, GameObjectConstructionCompletedEventArgs e)
    {
        var obj = e.GameObject;
        if (obj.Owner == null)
        {
            return;
        }

        var key = (obj.Owner.Id, obj.Definition.Name);
        if (_pendingBuilds.TryGetValue(key, out var queue) && queue.Count > 0)
        {
            queue.Dequeue();
            Write("Completed", OrderType.BuildObject, obj.Owner, $"def={obj.Definition.Name} pos={Fmt(obj.Translation)} objectId={obj.Id.Index}");
        }
    }

    private void Write(string state, OrderType orderType, Player? player, string details)
        => Write(state, orderType.ToString(), player, details);

    private void Write(string state, string category, Player? player, string details)
    {
        _writer.WriteLine(CurrentFrame(), state, category, player?.Name ?? "?", details);
    }

    private uint CurrentFrame()
        => (uint)(_game.MapTime.TotalTime.TotalMilliseconds / _game.GameEngine.MsPerLogicFrame);

    private static string Fmt(in Vector3 v) => $"({v.X:F2},{v.Y:F2},{v.Z:F2})";

    public void Dispose()
    {
        _game.OrderProcessed -= OnOrderProcessed;
        _game.ObjectCreated -= OnObjectCreated;
        _game.ObjectDestroyed -= OnObjectDestroyed;
        _game.ConstructionCompleted -= OnConstructionCompleted;
    }

    private sealed class SupplyCycleTracker
    {
        public SupplyAIUpdate.SupplyGatherStates LastState = SupplyAIUpdate.SupplyGatherStates.Default;
        public bool EverSeen;
        public ApproachLeg? ActiveLeg;
    }

    /// <summary>
    /// Accumulates telemetry for one supply approach leg (a unit moving toward a supply source to
    /// gather, or a supply center to dump). Distances are horizontal (2D) so that aircraft holding
    /// an altitude are judged by their ground-track approach, not their cruise height.
    /// </summary>
    private sealed class ApproachLeg
    {
        public readonly bool ToTarget;
        public readonly uint StartFrame;
        public float ClosestDistSq = float.MaxValue;
        public float MaxSpeed;
        public int Overshoots;

        private Vector3 _goal;
        private float _prevDistSq;
        private bool _hasPrev;
        private bool _wasDecreasing;

        public ApproachLeg(bool toTarget, uint startFrame, Vector3 goal)
        {
            ToTarget = toTarget;
            StartFrame = startFrame;
            _goal = goal;
        }

        public void Sample(Vector3 unitPos, AIUpdate aiUpdate)
        {
            // The supply state machine can reissue the target point mid-leg (e.g. a waiting-bone
            // reshuffle at a dock). Treat a large goal jump as a fresh close-in to avoid counting
            // the discontinuity as an overshoot.
            if (aiUpdate.TargetPoints.Count > 0)
            {
                var newGoal = aiUpdate.TargetPoints[0];
                if ((newGoal - _goal).Vector2XY().LengthSquared() > 4.0f)
                {
                    _goal = newGoal;
                    _hasPrev = false;
                }
            }

            var speed = aiUpdate.CurrentLocomotorSpeed;
            if (speed > MaxSpeed)
            {
                MaxSpeed = speed;
            }

            var dSq = (unitPos - _goal).Vector2XY().LengthSquared();
            if (dSq < ClosestDistSq)
            {
                ClosestDistSq = dSq;
            }

            if (_hasPrev)
            {
                var decreasing = dSq < _prevDistSq;
                if (_wasDecreasing && !decreasing)
                {
                    Overshoots++;
                }
                _wasDecreasing = decreasing;
            }
            _hasPrev = true;
            _prevDistSq = dSq;
        }
    }
}
