using System;
using System.Numerics;
using OpenSage.Content;
using OpenSage.Data.Ini;
using OpenSage.FX;
using OpenSage.Graphics.ParticleSystems;
using OpenSage.Mathematics;

namespace OpenSage.Logic.Object;

public sealed class MissileAIUpdate : AIUpdate
{
    internal override MissileAIUpdateModuleData ModuleData { get; }

    private MissileState _state;
    private LogicFrame _nextStateChangeTime;
    private LogicFrame _fuelExhaustionFrame;

    private Vector3 _unknownPosition;
    private uint _stateMaybe;
    private uint _unknownFrame1;
    private ObjectId _launcherObjectId;
    private ObjectId _unknownObjectId;
    private bool _unknownBool1;
    private uint _unknownFrame2;
    private float _unknownFloat1;
    private WeaponTemplate _weaponTemplate;
    private FXParticleSystemTemplate _exhaustParticleSystemTemplate;
    private bool _unknownBool2;
    private Vector3 _currentPositionMaybe;
    private int _unknownInt2;
    private int _unknownInt3;

    internal FXList DetonationFX { get; set; }

    // The firing unit, assigned at spawn (see ProjectileNugget) so collision can skip it.
    internal ObjectId LauncherObjectId
    {
        get => _launcherObjectId;
        set => _launcherObjectId = value;
    }

    internal MissileAIUpdate(GameObject gameObject, IGameEngine gameEngine, MissileAIUpdateModuleData moduleData)
        : base(gameObject, gameEngine, moduleData)
    {
        ModuleData = moduleData;

        _state = MissileState.Inactive;
    }

    public override UpdateSleepTime Update()
    {
        var currentFrame = GameEngine.GameLogic.CurrentFrame;

        switch (_state)
        {
            case MissileState.Inactive:
                _nextStateChangeTime = currentFrame + ModuleData.IgnitionDelay;
                _state = MissileState.WaitingForIgnition;
                goto case MissileState.WaitingForIgnition;

            case MissileState.WaitingForIgnition:
                if (currentFrame >= _nextStateChangeTime)
                {
                    ModuleData.IgnitionFX?.Value?.Execute(
                        new FXListExecutionContext(
                            GameObject.Rotation,
                            GameObject.Translation,
                            GameEngine));

                    if (ModuleData.DistanceToTravelBeforeTurning > 0)
                    {
                        var pointToReachBeforeTurning = GameObject.Translation
                            + Vector3.TransformNormal(Vector3.UnitX, GameObject.TransformMatrix) * ModuleData.DistanceToTravelBeforeTurning;
                        AddTargetPoint(pointToReachBeforeTurning);
                    }

                    // If the target died before the missile finished igniting, there's nowhere
                    // new to aim; keep whatever target point was already queued (e.g. the
                    // pre-turn point above) instead of dereferencing a destroyed target.
                    var currentTarget = GameObject.CurrentWeapon.CurrentTarget;
                    if (currentTarget != null && !currentTarget.IsDestroyed)
                    {
                        AddTargetPoint(currentTarget.TargetPosition);
                    }

                    // Ignition impulse along the muzzle direction. ApplyForce adds mass * deltaV
                    // to the acceleration, which physics integrates into velocity exactly once
                    // (acceleration is reset at the end of each physics update).
                    if (GameObject.Physics is { } physics)
                    {
                        var forward = Vector3.TransformNormal(Vector3.UnitX, GameObject.TransformMatrix);
                        physics.ApplyForce(forward * (physics.Mass * ModuleData.InitialVelocity));
                    }

                    _fuelExhaustionFrame = currentFrame + ModuleData.FuelLifetime;

                    _state = MissileState.Moving;
                }
                break;

            case MissileState.Moving:
                SteerTowardTargetPoints(currentFrame);
                // TODO: DistanceToTargetBeforeDiving
                BezierProjectileBehavior.CheckForHit(GameObject, GameEngine, ModuleData.DetonateCallsKill, DetonationFX, _launcherObjectId);
                break;

            default:
                throw new InvalidOperationException();
        }

        // TODO(Port): Use correct value.
        return UpdateSleepTime.None;
    }

    private void SteerTowardTargetPoints(LogicFrame currentFrame)
    {
        // Keep homing on a moving target: the final target point tracks its live position.
        if (ModuleData.TryToFollowTarget
            && TargetPoints.Count > 0
            && GameObject.CurrentWeapon?.CurrentTarget is { } target
            && !target.IsDestroyed)
        {
            TargetPoints[^1] = target.TargetPosition;
        }

        // Out of fuel: thrust stops and gravity takes over, unless configured to detonate.
        if (currentFrame >= _fuelExhaustionFrame)
        {
            if (ModuleData.DetonateOnNoFuel)
            {
                // Detonating means the warhead is delivered where the missile dies, not just
                // a death animation: fire it at the target before the kill.
                BezierProjectileBehavior.DeliverWarhead(GameObject, GameEngine, DetonationFX);
                GameObject.Kill(deathType: DeathType.Detonated);
            }
            return;
        }

        // The missile's THRUST locomotor does the steering: it accelerates along its forward
        // vector toward the target point (bounded by MaxThrustAngle), compensating for the
        // gravity physics applies. This mirrors AIUpdate.Update's movement block, minus the
        // infantry braking/waypoint behavior missiles don't use.
        var locomotor = CurrentLocomotor;
        if (locomotor == null || TargetPoints.Count == 0)
        {
            return;
        }

        // Consume waypoints that were reached or overshot before steering toward them: a
        // missile moving several units per frame can cross a nearby waypoint mid-step and
        // never enter its arrival radius, which would otherwise leave it trying to U-turn
        // back toward the point behind it.
        var closeEnoughDist = locomotor.LocomotorTemplate.CloseEnoughDist;
        var velocity = GameObject.Physics?.Velocity ?? Vector3.Zero;
        while (TargetPoints.Count > 0)
        {
            var toPoint = TargetPoints[0] - GameObject.Translation;
            if (toPoint.LengthSquared() < closeEnoughDist * closeEnoughDist
                || (velocity != Vector3.Zero && Vector3.Dot(toPoint, velocity) < 0))
            {
                TargetPoints.RemoveAt(0);
            }
            else
            {
                break;
            }
        }

        if (TargetPoints.Count == 0)
        {
            return;
        }

        locomotor.SetPhysicsOptions(GameObject);

        // TODO(Port): Use correct parameter values.
        var blocked = false;
        locomotor.LocoUpdateMoveTowardsPosition(
            GameObject,
            TargetPoints[0],
            (TargetPoints[0] - GameObject.Translation).Vector2XY().Length(),
            locomotor.GetMaxSpeedForCondition(GameObject.BodyModule.DamageState),
            ref blocked);
    }

    internal override void Load(StatePersister reader)
    {
        var version = reader.PersistVersion(6);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        reader.PersistVector3(ref _unknownPosition);
        reader.PersistUInt32(ref _stateMaybe);
        reader.PersistFrame(ref _unknownFrame1);

        var unknownInt1 = int.MaxValue;
        reader.PersistInt32(ref unknownInt1);
        if (unknownInt1 != int.MaxValue)
        {
            throw new InvalidStateException();
        }

        reader.PersistObjectId(ref _launcherObjectId);
        reader.PersistObjectId(ref _unknownObjectId);
        reader.PersistBoolean(ref _unknownBool1);
        reader.PersistFrame(ref _unknownFrame2);
        reader.PersistSingle(ref _unknownFloat1);

        var unknownFloat2 = 99999.0f;
        reader.PersistSingle(ref unknownFloat2);
        if (unknownFloat2 != 99999.0f)
        {
            throw new InvalidStateException();
        }

        var weaponTemplateName = _weaponTemplate?.Name;
        reader.PersistAsciiString(ref weaponTemplateName);
        _weaponTemplate = reader.AssetStore.WeaponTemplates.GetByName(weaponTemplateName);

        var exhaustParticleSystemTemplateName = _exhaustParticleSystemTemplate?.Name;
        reader.PersistAsciiString(ref exhaustParticleSystemTemplateName);
        if (reader.Mode == StatePersistMode.Read)
        {
            _exhaustParticleSystemTemplate = reader.AssetStore.FXParticleSystemTemplates.GetByName(exhaustParticleSystemTemplateName);
        }

        reader.PersistBoolean(ref _unknownBool2);
        reader.PersistVector3(ref _currentPositionMaybe);
        reader.PersistInt32(ref _unknownInt2); // 0, 0x20000
        reader.PersistInt32(ref _unknownInt3); // 1960

        if (version >= 6)
        {
            reader.SkipUnknownBytes(6);
        }
    }

    private enum MissileState
    {
        Inactive,
        WaitingForIgnition,
        Moving,
    }
}

public sealed class MissileAIUpdateModuleData : AIUpdateModuleData
{
    internal new static MissileAIUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private new static readonly IniParseTable<MissileAIUpdateModuleData> FieldParseTable = AIUpdateModuleData.FieldParseTable.Concat(new IniParseTable<MissileAIUpdateModuleData>
    {
        { "TryToFollowTarget", (parser, x) => x.TryToFollowTarget = parser.ParseBoolean() },
        { "FuelLifetime", (parser, x) => x.FuelLifetime = parser.ParseTimeMillisecondsToLogicFrames() },
        { "DetonateOnNoFuel", (parser, x) => x.DetonateOnNoFuel = parser.ParseBoolean() },
        { "InitialVelocity", (parser, x) => x.InitialVelocity = parser.ParseVelocityToLogicFrames() },
        { "IgnitionDelay", (parser, x) => x.IgnitionDelay = parser.ParseTimeMillisecondsToLogicFrames() },
        { "DistanceToTravelBeforeTurning", (parser, x) => x.DistanceToTravelBeforeTurning = parser.ParseInteger() },
        { "DistanceToTargetBeforeDiving", (parser, x) => x.DistanceToTargetBeforeDiving = parser.ParseInteger() },
        { "DistanceToTargetForLock", (parser, x) => x.DistanceToTargetForLock = parser.ParseInteger() },
        { "GarrisonHitKillRequiredKindOf", (parser, x) => x.GarrisonHitKillRequiredKindOf = parser.ParseEnum<ObjectKinds>() },
        { "GarrisonHitKillForbiddenKindOf", (parser, x) => x.GarrisonHitKillForbiddenKindOf = parser.ParseEnum<ObjectKinds>() },
        { "GarrisonHitKillCount", (parser, x) => x.GarrisonHitKillCount = parser.ParseInteger() },
        { "GarrisonHitKillFX", (parser, x) => x.GarrisonHitKillFX = parser.ParseFXListReference() },
        { "DetonateCallsKill", (parser, x) => x.DetonateCallsKill = parser.ParseBoolean() },
        { "IgnitionFX", (parser, x) => x.IgnitionFX = parser.ParseFXListReference() },
        { "KillSelfDelay", (parser, x) => x.KillSelfDelay = parser.ParseInteger() },
        { "DistanceScatterWhenJammed", (parser, x) => x.DistanceScatterWhenJammed = parser.ParseInteger() },
    });

    public bool TryToFollowTarget { get; private set; }
    public LogicFrameSpan FuelLifetime { get; private set; }
    public bool DetonateOnNoFuel { get; private set; }
    public float InitialVelocity { get; private set; }
    public LogicFrameSpan IgnitionDelay { get; private set; }
    public int DistanceToTravelBeforeTurning { get; private set; }
    public int DistanceToTargetBeforeDiving { get; private set; }
    public int DistanceToTargetForLock { get; private set; }
    public ObjectKinds GarrisonHitKillRequiredKindOf { get; private set; }
    public ObjectKinds GarrisonHitKillForbiddenKindOf { get; private set; }
    public int GarrisonHitKillCount { get; private set; }
    public LazyAssetReference<FXList> GarrisonHitKillFX { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public bool DetonateCallsKill { get; private set; }
    public LazyAssetReference<FXList> IgnitionFX { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public int KillSelfDelay { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public int DistanceScatterWhenJammed { get; private set; }

    internal override BehaviorModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new MissileAIUpdate(gameObject, gameEngine, this);
    }
}
