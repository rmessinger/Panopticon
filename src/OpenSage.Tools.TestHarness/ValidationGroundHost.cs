using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Applies a <see cref="SpawnPlan"/> to a live <see cref="IGame"/>: resolves each definition and
/// player, samples terrain height, and creates every unit/target/supply-source through
/// <see cref="GameLogic.CreateObject"/>. Orders are dispatched as real engine <see cref="Order"/>s
/// through <see cref="OrderProcessor"/> (only spawning is a shortcut). See deep dive P9 §2.2, §3.
/// </summary>
public sealed class ValidationGroundHost
{
    private readonly List<SpawnedUnit> _units = [];
    private readonly List<UnitGap> _gaps = [];
    private readonly List<GameObject> _targets = [];

    public IReadOnlyList<SpawnedUnit> Units => _units;
    public IReadOnlyList<UnitGap> Gaps => _gaps;
    public IReadOnlyList<GameObject> Targets => _targets;

    public void Spawn(SpawnPlan plan, IGame game, float targetHp = 0f)
    {
        SpawnTargets(plan, game, targetHp);
        SpawnUnits(plan, game);
        SpawnSupplySources(plan, game);
    }

    private void SpawnTargets(SpawnPlan plan, IGame game, float targetHp = 0f)
    {
        // Targets are neutral (civilian) so any faction can legally attack them.
        var civilian = game.Scene3D.Players[1];
        foreach (var placement in plan.TargetPlacements)
        {
            var obj = CreateAt(game, placement.DefinitionName, placement.Position, civilian);
            if (obj != null)
            {
                // Standardize HP so attack completion (target destruction) is feasible for the whole
                // roster and weapon-differentiated rather than dominated by a structure's native HP.
                if (targetHp > 0f)
                {
                    obj.BodyModule?.SetMaxHealth(targetHp);
                }
                _targets.Add(obj);
            }
        }
    }

    private void SpawnSupplySources(SpawnPlan plan, IGame game)
    {
        var civilian = game.Scene3D.Players[1];
        foreach (var placement in plan.SupplySourcePlacements)
        {
            CreateAt(game, placement.DefinitionName, placement.Position, civilian);
        }
    }

    private void SpawnUnits(SpawnPlan plan, IGame game)
    {
        for (var i = 0; i < plan.UnitPlacements.Count; i++)
        {
            var placement = plan.UnitPlacements[i];

            if (placement.Action.Kind == PrimaryActionKind.Attack
                && PrimaryActionCatalog.UnmeasurableAttackReasons.TryGetValue(placement.DefinitionName, out var reason))
            {
                _gaps.Add(new UnitGap(placement.DefinitionName, placement.FactionIndex,
                    placement.Action.Kind, reason));
                continue;
            }

            var player = ResolvePlayer(game, placement.PlayerIndex);
            if (player == null)
            {
                _gaps.Add(new UnitGap(placement.DefinitionName, placement.FactionIndex,
                    placement.Action.Kind, $"no player for index {placement.PlayerIndex}"));
                continue;
            }

            // Face the assigned target so the formation opens looking at what it will act on
            // (and any facing-dependent fire gating starts satisfied).
            var targetPlacement = placement.TargetIndex >= 0 && placement.TargetIndex < plan.TargetPlacements.Count
                ? plan.TargetPlacements[placement.TargetIndex]
                : null;
            var facing = targetPlacement != null
                ? MathF.Atan2(
                    targetPlacement.Position.Y - placement.Position.Y,
                    targetPlacement.Position.X - placement.Position.X)
                : (float?)null;

            var obj = CreateAt(game, placement.DefinitionName, placement.Position, player, facing);
            if (obj == null)
            {
                _gaps.Add(new UnitGap(placement.DefinitionName, placement.FactionIndex,
                    placement.Action.Kind, $"definition '{placement.DefinitionName}' not found"));
                continue;
            }

            var spawned = new SpawnedUnit(placement, obj.Id);

            // Ability units act on a dedicated subject object rather than the destructible car:
            // a damaged friendly for the heal field, a neutral building for the capture/disable
            // hacks, an enemy vehicle for the hijack. Spawned next to the unit's car-target
            // position; the observation seam completes on the per-power effect.
            if (placement.Action.Kind == PrimaryActionKind.SpecialPower)
            {
                SpawnAbilitySubject(game, spawned, obj, placement);
            }

            _units.Add(spawned);
        }
    }

    private void SpawnAbilitySubject(IGame game, SpawnedUnit unit, GameObject unitObject, UnitPlacement placement)
    {
        var anchor = placement.TargetIndex >= 0 && placement.TargetIndex < _targets.Count
            ? _targets[placement.TargetIndex].Translation
            : placement.Position;
        var subjectPosition = anchor + new Vector3(SubjectOffset, 0f, 0f);

        switch (unitObject.Definition.Name)
        {
            case "AmericaVehicleMedic":
                {
                    var subject = CreateAt(game, "AmericaInfantryRanger", subjectPosition, unitObject.Owner);
                    if (subject?.BodyModule != null)
                    {
                        subject.BodyModule.InternalChangeHealth(-MathF.Max(1f, subject.BodyModule.Health - SubjectDamagedHealth));
                        unit.SubjectObjectId = subject.Id;
                        unit.ExpectedEffect = AbilityEffect.SubjectHealed;
                        unit.SubjectInitialHealth = subject.BodyModule.Health;
                    }
                    break;
                }

            case "ChinaInfantryBlackLotus":
                {
                    var subject = CreateAt(game, CaptureSubjectDefinition, subjectPosition, game.Scene3D.Players[1]);
                    if (subject != null)
                    {
                        unit.SubjectObjectId = subject.Id;
                        unit.ExpectedEffect = AbilityEffect.SubjectOwned;
                    }
                    break;
                }

            case "ChinaInfantryHacker":
                {
                    var subject = CreateAt(game, CaptureSubjectDefinition, subjectPosition, game.Scene3D.Players[1]);
                    if (subject != null)
                    {
                        unit.SubjectObjectId = subject.Id;
                        unit.ExpectedEffect = AbilityEffect.SubjectDisabled;
                    }
                    break;
                }

            case "GLAInfantryHijacker":
                {
                    // Enemy vehicle (the hijacker is GLA; USA is a roster enemy) placed at the
                    // subject position; the hijacker is ordered to walk into it.
                    var enemy = ResolvePlayer(game, HijackVehicleOwnerFactionIndex);
                    var subject = CreateAt(game, "AmericaVehicleHumvee", subjectPosition, enemy);
                    if (subject != null)
                    {
                        unit.SubjectObjectId = subject.Id;
                        unit.ExpectedEffect = AbilityEffect.SubjectOwned;
                    }
                    break;
                }
        }
    }

    private const float SubjectOffset = 30f;
    private const float SubjectDamagedHealth = 30f;
    private const string CaptureSubjectDefinition = "AmericaPowerPlant";
    private const int HijackVehicleOwnerFactionIndex = 2;

    public enum AbilityEffect
    {
        None,
        SubjectHealed,
        SubjectOwned,
        SubjectDisabled,
    }

    /// <summary>
    /// Commands each unit's primary action on a single frame and records the dispatch with the
    /// timer. Call only after the settle stage has elapsed. Attack units are placed within weapon
    /// range by the builder, so the weapon is commanded directly: this isolates the measured
    /// duration to the weapon's own fire rate and damage (the time to destroy the standardized-HP
    /// target), and avoids an engine limitation where the AttackObject order's move step suppresses
    /// fire for several units. Other actions dispatch through OrderProcessor as real engine orders.
    /// </summary>
    public void IssueOrders(IGame game, UnitActionTimer timer)
    {
        var processor = new OrderProcessor(game);
        var currentFrame = CurrentLogicFrame(game);

        foreach (var unit in _units)
        {
            var player = ResolvePlayer(game, unit.Placement.PlayerIndex)!;
            var playerIndex = game.Scene3D.GetPlayerIndex(player);

            // Link the unit to its assigned target so the observation seam can poll target health
            // for attack-completion detection. Uses the same TargetIndex BuildOrder resolves below.
            var targetIdx = unit.Placement.TargetIndex;
            if (targetIdx >= 0 && targetIdx < _targets.Count)
            {
                unit.TargetObjectId = _targets[targetIdx].Id;
            }

            // OrderProcessor applies non-attack orders to the player's current selection, so select
            // just this unit before dispatching. The validation ground issues one action per unit.
            var unitObject = game.Scene3D.GameObjects.GetObjectById(unit.ObjectId);
            if (unitObject != null)
            {
                player.SelectUnits(new[] { unitObject });
            }

            var targetObject = unit.TargetObjectId.HasValue
                ? game.Scene3D.GameObjects.GetObjectById(unit.TargetObjectId.Value)
                : null;

            if (unit.Placement.Action.Kind == PrimaryActionKind.Attack && unitObject != null && targetObject != null)
            {
                unitObject.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, targetObject.Id));
            }
            else
            {
                var order = BuildOrder(unit, playerIndex, game, unitObject, timer);
                if (order == null)
                {
                    // BuildOrder records the precise reason for null (e.g. which engine piece is
                    // missing) before returning it.
                    continue;
                }

                try
                {
                    processor.Process([order]);
                }
                catch (NotImplementedException)
                {
                    // The engine's SpecialPower applicators throw for unimplemented powers. The
                    // order itself was well-formed, so surface the missing engine piece rather
                    // than the generic "no order builder" gap.
                    timer.RecordGap(unit.Placement.DefinitionName, unit.Placement.FactionIndex,
                        unit.Placement.Action.Kind,
                        $"engine applicator does not implement {order.OrderType} for this unit's power");
                    continue;
                }
                catch (ArgumentOutOfRangeException)
                {
                    timer.RecordGap(unit.Placement.DefinitionName, unit.Placement.FactionIndex,
                        unit.Placement.Action.Kind,
                        $"engine has no {order.OrderType} handler for this unit's power");
                    continue;
                }
            }

            timer.OnDispatched(
                unitKey: unit.ObjectId.Index,
                unit.Placement.DefinitionName,
                unit.Placement.FactionIndex,
                unit.Placement.Action,
                frame: currentFrame,
                targetKey: unit.TargetObjectId?.Index);
        }
    }

    private Order? BuildOrder(SpawnedUnit unit, int playerIndex, IGame game, GameObject? unitObject, UnitActionTimer timer)
    {
        var placement = unit.Placement;
        var targetIndex = placement.TargetIndex;
        var target = targetIndex >= 0 && targetIndex < _targets.Count ? _targets[targetIndex] : null;

        return placement.Action.Kind switch
        {
            PrimaryActionKind.Attack when target != null =>
                AttackObjectOrder(playerIndex, target.Id),
            PrimaryActionKind.Move =>
                MoveToOrder(playerIndex, target?.Translation ?? placement.Position),
            PrimaryActionKind.Build =>
                Order.CreateBuildObject(playerIndex, (int)ResolveDefinitionId(game, placement.Action.TargetDefinitionName),
                    placement.Position, 0f),
            PrimaryActionKind.SpecialPower =>
                BuildSpecialPowerOrder(unit, playerIndex, game, target, unitObject, timer),
            // Gather needs per-unit argument construction (supply-point id, host-side unit
            // selection) that is not yet implemented; surfaced as a named gap instead of a
            // malformed, argument-less order that crashes OrderProcessor.
            PrimaryActionKind.Gather =>
                null,
            _ => MoveToOrder(playerIndex, placement.Position),
        };
    }

    /// <summary>
    /// Builds a real SpecialPower order for the unit's first special-power module, mapped to the
    /// applicator family the power belongs to (location powers vs object powers), targeting the
    /// unit's ability subject where one was spawned. The Hijacker has no power module (its hijack
    /// is a context-mode command): it is ordered to walk into its subject vehicle and completes on
    /// the ownership transfer. Well-formed orders are dispatched and only fail when the engine
    /// applicator itself is unimplemented, which IssueOrders reports precisely.
    /// </summary>
    private Order? BuildSpecialPowerOrder(SpawnedUnit unit, int playerIndex, IGame game, GameObject? target, GameObject? unitObject, UnitActionTimer timer)
    {
        if (unitObject == null)
        {
            timer.RecordGap(unit.Placement.DefinitionName, unit.Placement.FactionIndex,
                unit.Placement.Action.Kind, "unit object missing at order time");
            return null;
        }

        var subject = unit.SubjectObjectId.HasValue
            ? game.Scene3D.GameObjects.GetObjectById(unit.SubjectObjectId.Value)
            : null;

        var powerModule = unitObject.FindBehaviors<SpecialPowerModule>().FirstOrDefault();
        if (powerModule == null)
        {
            if (unitObject.FindBehavior<HijackerUpdate>() != null && subject != null)
            {
                return MoveToOrder(playerIndex, subject.Transform.Translation);
            }

            timer.RecordGap(unit.Placement.DefinitionName, unit.Placement.FactionIndex,
                unit.Placement.Action.Kind, "no SpecialPower module on the unit and no hijacker subject");
            return null;
        }

        var powerType = powerModule.SpecialPowerType;
        switch (powerType)
        {
            case SpecialPowerType.CleanupArea:
                // Location power: the ambulance detox field is cast at the heal subject's
                // position (or the assigned target's position when no subject was spawned).
                var position = subject?.Transform.Translation ?? target?.Translation ?? unitObject.Translation;
                return Order.CreateSpecialPowerAtLocation(playerIndex, (int)powerType, position,
                    SpecialPowerOrderFlags.NeedTargetPosition, ObjectId.Invalid);

            case SpecialPowerType.BlackLotusCaptureBuilding:
            case SpecialPowerType.HackerDisableBuilding:
                var powerTarget = subject ?? target;
                if (powerTarget == null)
                {
                    timer.RecordGap(unit.Placement.DefinitionName, unit.Placement.FactionIndex,
                        unit.Placement.Action.Kind, $"{powerType} needs a target object; none assigned");
                    return null;
                }
                return Order.CreateSpecialPowerAtObject(playerIndex, (int)powerType, powerTarget.Id,
                    SpecialPowerOrderFlags.NeedTargetEnemyObject
                    | SpecialPowerOrderFlags.NeedTargetNeutralObject, ObjectId.Invalid);

            default:
                timer.RecordGap(unit.Placement.DefinitionName, unit.Placement.FactionIndex,
                    unit.Placement.Action.Kind, $"{powerType} has no validation-ground order mapping yet");
                return null;
        }
    }

    private static Order AttackObjectOrder(int playerIndex, ObjectId targetId)
    {
        var order = new Order(playerIndex, OrderType.AttackObject);
        order.AddObjectIdArgument(targetId);
        return order;
    }

    private static Order MoveToOrder(int playerIndex, Vector3 position)
    {
        var order = new Order(playerIndex, OrderType.MoveTo);
        order.AddPositionArgument(position);
        return order;
    }

    private static GameObject? CreateAt(IGame game, string definitionName, Vector3 planarPosition, Player owner, float? facingYaw = null)
    {
        var definition = game.AssetStore.ObjectDefinitions.GetByName(definitionName);
        if (definition == null)
        {
            return null;
        }

        var height = game.Scene3D.Terrain.GetGroundHeight(new Vector2(planarPosition.X, planarPosition.Y)) ?? 0f;
        var position = new Vector3(planarPosition.X, planarPosition.Y, height);
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        // Yaw 0 faces +X (MathUtility.GetYawFromDirection); targets sit on the +Y target row, so
        // units spawned without a facing would open 90 degrees off their targets.
        var rotation = facingYaw.HasValue
            ? System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, facingYaw.Value)
            : gameObject.Rotation;
        gameObject.UpdateTransform(position, rotation);
        // Colliders are built at the origin and only refreshed on the next logic tick; a run held
        // paused at start (ValidationGroundHoldOrders) never ticks logic, so frustum culling would
        // cull every spawned model off-camera until resume. Refresh immediately after placement.
        gameObject.UpdateColliders();
        return gameObject;
    }

    private static Player? ResolvePlayer(IGame game, int playerIndex)
    {
        var players = game.Scene3D.Players;
        return playerIndex >= 0 && playerIndex < players.Count ? players[playerIndex] : null;
    }

    private static uint ResolveDefinitionId(IGame game, string? definitionName)
    {
        if (string.IsNullOrEmpty(definitionName))
        {
            return 0;
        }

        return game.AssetStore.ObjectDefinitions.GetByName(definitionName)?.InstanceId ?? 0;
    }

    private static uint CurrentLogicFrame(IGame game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));

    /// <summary>
    /// Clears the weapon target of any unit whose assigned target has been destroyed, so the unit
    /// stops firing once it has finished its command. Without this, a unit that destroys its target
    /// keeps firing (and its turret auto-acquires new enemies), flooding the simulation with
    /// projectiles and preventing timely completion.
    /// </summary>
    public void DisarmUnitsWhoseTargetIsDestroyed(IGame game)
    {
        foreach (var unit in _units)
        {
            if (!unit.TargetObjectId.HasValue)
            {
                continue;
            }

            var target = game.Scene3D.GameObjects.GetObjectById(unit.TargetObjectId.Value);
            if (target == null || target.IsEffectivelyDead)
            {
                game.Scene3D.GameObjects.GetObjectById(unit.ObjectId)?.CurrentWeapon?.SetTarget(null);
            }
        }
    }

    /// <summary>
    /// Builds the per-frame observation seam the timer polls: for each tracked unit, reads the
    /// live-game signals that map to its action's completion predicate.
    /// </summary>
    public System.Func<uint, UnitObservation> BuildObservationSeam(IGame game)
    {
        return unitKey =>
        {
            var unit = _units.Find(u => u.ObjectId.Index == unitKey);
            if (unit == null)
            {
                return default;
            }

            var obj = game.Scene3D.GameObjects.GetObjectById(unit.ObjectId);
            if (obj == null)
            {
                return default;
            }

            var target = unit.TargetObjectId.HasValue
                ? game.Scene3D.GameObjects.GetObjectById(unit.TargetObjectId.Value)
                : null;

            var subject = unit.SubjectObjectId.HasValue
                ? game.Scene3D.GameObjects.GetObjectById(unit.SubjectObjectId.Value)
                : null;

            return new UnitObservation
            {
                TargetPointsEmpty = obj.AIUpdate?.TargetPoints.Count == 0,
                TargetHealth = target?.BodyModule?.Health,
                TargetDestroyed = target == null || target.IsEffectivelyDead,
                BuildComplete = obj.IsEffectivelyDead == false && obj.BuildProgress >= 1.0f,
                SpecialPowerEffectObserved = subject != null && IsAbilityEffectObserved(unit, obj, subject),
            };
        };
    }

    private static bool IsAbilityEffectObserved(SpawnedUnit unit, GameObject unitObject, GameObject subject)
    {
        return unit.ExpectedEffect switch
        {
            AbilityEffect.SubjectHealed =>
                subject.BodyModule != null && subject.BodyModule.Health > unit.SubjectInitialHealth,
            AbilityEffect.SubjectOwned =>
                subject.Owner != null && subject.Owner == unitObject.Owner,
            AbilityEffect.SubjectDisabled =>
                subject.IsDisabledByType(DisabledType.UserParalyzed),
            _ => false,
        };
    }

    public sealed record SpawnedUnit(UnitPlacement Placement, ObjectId ObjectId)
    {
        public ObjectId? TargetObjectId { get; set; }

        public ObjectId? SubjectObjectId { get; set; }

        public AbilityEffect ExpectedEffect { get; set; }

        public float SubjectInitialHealth { get; set; }
    }
}
